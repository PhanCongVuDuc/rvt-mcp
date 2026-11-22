// Usage:
//   stdio (default):  RvtMcp.Server.exe              — spawned by Claude/GPT/Cursor
//   HTTP SSE:          RvtMcp.Server.exe --http 8200  — for Ollama/LM Studio/custom
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Targeting;
using RvtMcp.Plugin; // RvtMcpConfig
using RvtMcp.Server.Prompts;
using RvtMcp.ToolCatalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // rvt-mcp addin list|call - a command line, not an MCP server (AddinCli).
            if (args.Length > 0 && args[0] == "addin")
            {
                Environment.ExitCode = await AddinCli.RunAsync(args.Skip(1).ToArray(), Console.Out, Console.Error);
                return;
            }

            if (args.Any(a => a == "--help" || a == "-h"))
            {
                PrintHelp();
                return;
            }

            // Config precedence: JSON < env < CLI. --target stays a separate CLI parse.
            AuthToken.CleanupLegacyDiscoveryFiles();
            var config = RvtMcpConfig.Load(args);
            config.ValidateResponseLimits();
            ServerState.Config = config;
            var unknownTools = UnknownAllowlistedTools(config);
            if (unknownTools.Length > 0)
                Console.Error.WriteLine("[RvtMcp] --tools names match no tool: " + string.Join(", ", unknownTools));
            ServerState.ToolCatalog = ServerToolCatalogBuilder.Build(
                ToolsetFilter.Resolve(config), config);
            if (!string.IsNullOrWhiteSpace(config.Target))
            {
                if (!TryResolveTargetSelector(config.Target.Trim(), out var selector, out var targetError))
                {
                    Console.Error.WriteLine(
                        "[RvtMcp] Invalid --target value '" + config.Target + "'. " + targetError);
                    Environment.Exit(1);
                    return;
                }
                RevitTargetBinding.Initialize(selector);
            }

            // Warm up the target-binding code paths on a background thread so the
            // first tool call's connect+handshake isn't penalized by one-time JIT.
            // The scan may delete proven-dead descriptor files (same as a list call)
            // but must not bind, connect, or change binding state.
            new Thread(WarmUpTargetBinding) { IsBackground = true, Name = "RvtMcp.TargetWarmup" }.Start();

            // Initialize memory system (shared across tool classes + resources)
            var session = new Memory.SessionContext(config.EnableCallLogOrDefault);
            ToolGateway.Session = session;
            RevitResources.Session = session;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                ServerToast.Instance = ServerToast.TryCreate(config);

            await RunStdio(config);
        }

        /// <summary>JIT warm-up: exercise selector parse, descriptor scan/probe,
        /// binding plan and the §6.10 envelope paths once, off the tool-call path.</summary>
        private static void WarmUpTargetBinding()
        {
            try
            {
                SelectorParser.TryParse(HostProduct.Revit, "auto", out _, out _, out _);
                var scan = RevitTargetBinding.Scan();
                var binding = RevitTargetBinding.Binding;
                binding.CaptureGeneration();
                binding.PlanCall(0, scan.Live);
                MetaTools.TargetEnvelope(new Bimwright.Targeting.TargetPayload
                {
                    Code = Bimwright.Targeting.TargetCode.NoTarget,
                    Message = "warmup",
                    NextStep = "warmup",
                    Candidates = scan.Live.Count == 0
                        ? new List<Bimwright.Targeting.CandidateInfo>
                            { new Bimwright.Targeting.CandidateInfo("warmup", 2024, 0, false, null) }
                        : Array.Empty<Bimwright.Targeting.CandidateInfo>()
                });
                ToolGateway.WarmUpWire();
            }
            catch { /* warm-up is best-effort */ }
        }

        private static async Task RunStdio(RvtMcpConfig config)
        {
            var enabled = ToolsetFilter.Resolve(config);
            var builder = Host.CreateApplicationBuilder();
            // stdio MCP stdout must contain JSON-RPC only. ClearProviders() removes default
            // host logging, but the MCP SDK (and any transitive package) may re-add a Console
            // provider that writes to stdout. AddConsole with LogToStandardErrorThreshold=Trace
            // forces every console log line — from any provider re-added downstream — to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            var mcp = builder.Services
                .AddMcpServer(ConfigureMcpServerOptions)
                .WithStdioServerTransport();
            mcp = RuntimeToolFilter.Register(mcp, config, ToolGateway.Session);
            mcp = RegisterToolsets(mcp, enabled, config);
            mcp.WithResources<RevitResources>();
            mcp.WithPrompts<RevitPrompts>();
            var app = builder.Build();
            await app.RunAsync();
        }

        private static void PrintHelp()
        {
            var usage = string.Join("\n", new[]
            {
                "rvt-mcp — Revit MCP server (bimwright.dev)",
                "",
                "Usage: rvt-mcp [options]",
                "       rvt-mcp addin list [--target <selector>]",
                "       rvt-mcp addin call <addin> <command> [--args <json>] [--target <selector>] [--read-only] [--timeout <seconds>]",
                "         Commands other add-ins registered in a running Revit (e.g. Sonny.Revit). JSON on stdout, one error line",
                "         on stderr; exit 0 ok, 1 the command or rvt-mcp refused, 2 bad arguments or no Revit reachable.",
                "         With several Revit sessions running, --target (year | pid:<n> | id:revit-<year>-<pid>) is required.",
                "",
                "Routing:",
                "  --target <selector>     Pin the initial target: auto | a 4-digit calendar",
                "                          year (2022..2027) | pid:<n> | id:revit-<year>-<pid>.",
                "                          Legacy R-codes (R22..R27) are rejected in v0.5+.",
                "                          Default: auto — ordered scan of revit-*.json files in",
                "                          %LOCALAPPDATA%\\Bimwright\\rvt-mcp\\.",
                "",
                "Tool exposure (A3 Progressive Disclosure):",
                "  --toolsets <csv>        Comma list of toolsets to enable. Default: " + string.Join(",", ToolsetFilter.DefaultOn) + ".",
                "                          Known toolsets: " + string.Join(", ", ToolsetFilter.KnownToolsets),
                "                          Use 'all' to expose every toolset.",
                "  --tools <csv>           Expose only these tools (names with or without 'revit_'). Without",
                "                          --toolsets every toolset is searched; --read-only still applies.",
                "  --read-only             Expose only tools annotated ReadOnly=true (no document/file writes).",
                "  --enable-send-code      Expose arbitrary C# execution (default ON).",
                "  --disable-send-code     Hide send_code_to_revit. --read-only also hides it.",
                "  --enable-call-log       Persist tool-call params/results in server and plugin logs (default OFF).",
                "  --disable-call-log      Suppress call logs, including the send_code body journal.",
                "",
                "Response limits (UTF-8 bytes, integers >=1024; warn <= strong <= budget <= max):",
                "  --enable-response-guard / --disable-response-guard (default ON)",
                "  --response-warn-bytes <n>         Default 65536.",
                "  --response-strong-warn-bytes <n>  Default 262144.",
                "  --response-budget-bytes <n>       Default 716800 (pretty-print headroom).",
                "  --max-response-bytes <n>          Default 1048576; remains active with guard OFF.",
                "  --spill-retention-hours <n>       Hours a spill file is kept before cleanup (1-8760).",
                "                                    Default 36; a missing or invalid value uses 36.",
                "",
                "Transport security (S7):",
                "  --allow-lan-bind        (plugin-side only — set BIMWRIGHT_ALLOW_LAN_BIND env var in",
                "                          the Revit process environment; server-side flag is documented",
                "                          here for future cross-process propagation.)",
                "",
                "Env vars (override JSON, overridden by CLI):",
                "  BIMWRIGHT_TARGET, BIMWRIGHT_TOOLSETS, BIMWRIGHT_TOOLS, BIMWRIGHT_READ_ONLY,",
                "  BIMWRIGHT_ALLOW_LAN_BIND,",
                "  BIMWRIGHT_ENABLE_SEND_CODE, BIMWRIGHT_ENABLE_CALL_LOG, BIMWRIGHT_ENABLE_RESPONSE_GUARD,",
                "  BIMWRIGHT_RESPONSE_WARN_BYTES, BIMWRIGHT_RESPONSE_STRONG_WARN_BYTES,",
                "  BIMWRIGHT_RESPONSE_BUDGET_BYTES, BIMWRIGHT_MAX_RESPONSE_BYTES,",
                "  BIMWRIGHT_SPILL_RETENTION_HOURS",
                "",
                "Config file (lowest precedence):",
                "  %LOCALAPPDATA%\\Bimwright\\rvt-mcp\\rvtmcp.config.json",
                "",
                "Other:",
                "  -h, --help              Show this help and exit.",
            });
            Console.WriteLine(usage);
        }

        // MCP InitializeResult metadata. ServerInstructions is the single most important
        // signal for Tool Search discoverability — Claude Code/Desktop's Tool Search ranks
        // servers by (1) instructions, (2) literal tool name. Without this populated, an
        // agent asking "list Revit tools" returns nothing even though 224 tools are exposed.
        // Anthropic truncates this field at 2KB; the keyword-dense first paragraph carries
        // the discoverability load if the SDK or proxy truncates later.
        // InformationalVersion carries "+githash"; report clean semver to MCP clients.
        internal static readonly string ServerVersion =
            (Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0").Split('+')[0];

        private static void ConfigureMcpServerOptions(ModelContextProtocol.Server.McpServerOptions opts)
        {
            opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "rvt-mcp",
                Title = "Revit MCP",
                Version = ServerVersion,
                Description = "Model Context Protocol gateway for Autodesk Revit 2022-2027",
                WebsiteUrl = "https://github.com/bimwright/rvt-mcp"
            };
            // A newer release known from the add-in's cached daily check (no network here).
            opts.ServerInstructions = ServerInstructionsText;
        }

        // Anthropic Tool Search truncates this at 2 KB; the constant below is kept
        // under 2048 UTF-8 bytes. Lead with the keyword paragraph (highest discriminative
        // signal for queries like "list Revit tools"), then a compact toolset-name index
        // — 2 examples per toolset — so semantic search for individual ops still resolves.
        private const string ServerInstructionsText =
@"rvt-mcp — MCP gateway for Autodesk Revit 2022-2027: run C# in Revit, open models, capture/export views and sheets, call other add-ins' commands.

Multi-Revit: list via revit_list_available_targets (a year can repeat); pin with revit_switch_target(""pid:<n>""); never guess. Do not retry export after 60s timeout.

Tools (prefix revit_):
- meta: send_code_to_revit, open_model, list_recent_models, list_available_targets, get_current_target, switch_target, list_addin_commands, call_addin_command
- query: get_current_view_info
- view: capture_view_image, activate_view
- export: export_pdf, export_image
- sheets: list_sheets";

        /// <summary>Parse a --target/BIMWRIGHT_TARGET/config selector (§6.1); R-codes get the educational error.</summary>
        internal static bool TryResolveTargetSelector(string raw, out TargetSelector selector, out string error)
        {
            if (SelectorParser.TryParse(HostProduct.Revit, raw, out selector, out var selError, out var selMessage))
            {
                error = null;
                return true;
            }
            error = "Expected: auto | a 4-digit Revit calendar year 2022..2027 | pid:<n> | id:revit-<year>-<pid>. "
                + (selError == SelectorError.RCode
                    ? "Note: legacy R-codes (R22..R27) are no longer accepted in v0.5; use the year directly."
                    : selMessage ?? "Unrecognized selector.");
            return false;
        }

        internal static bool IncludeSendCode(HashSet<string> enabled, RvtMcpConfig config)
        {
            var sendCodeOn = config == null || config.EnableSendCodeOrDefault;
            return sendCodeOn
                && enabled.Contains("meta");
        }

        internal static IEnumerable<MethodInfo> ResolveRegisteredToolMethods(HashSet<string> enabled, RvtMcpConfig config)
        {
            var allow = ToolsetFilter.ToolAllowlist(config);
            return ResolveRegisteredToolTypes(enabled, config)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
                .Where(method => config?.ReadOnlyOrDefault != true
                    || method.GetCustomAttribute<McpServerToolAttribute>().ReadOnly
                    // revit_switch_target is annotated ReadOnly=false (it mutates session
                    // routing) but must stay exposed in read-only sessions so the agent
                    // can always recover the target binding (spec §6.8/§6.11).
                    || method.GetCustomAttribute<McpServerToolAttribute>().Name == "revit_switch_target"
                    // revit_call_addin_command stays too: it refuses an add-in command declared readOnly=false itself
                    // (AddinMetaTools.ReadOnlyRefusal) and the plugin checks again.
                    || method.GetCustomAttribute<McpServerToolAttribute>().Name == "revit_call_addin_command")
                .Where(method => allow == null || allow.Contains(method.GetCustomAttribute<McpServerToolAttribute>().Name));
        }

        /// <summary>Allowlisted names that match no tool in any toolset — reported on stderr so a typo is visible.</summary>
        internal static string[] UnknownAllowlistedTools(RvtMcpConfig config)
        {
            var allow = ToolsetFilter.ToolAllowlist(config);
            if (allow == null) return Array.Empty<string>();
            var everything = new RvtMcpConfig { Toolsets = new List<string> { "all" } };
            var known = new HashSet<string>(ResolveRegisteredToolMethods(ToolsetFilter.Resolve(everything), everything)
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>().Name), StringComparer.OrdinalIgnoreCase);
            return allow.Where(n => !known.Contains(n)).OrderBy(n => n).ToArray();
        }

        internal static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled, RvtMcpConfig config)
        {
            return mcp.WithTools(ResolveRegisteredToolMethods(enabled, config)
                .Select(method => McpServerTool.Create(method, (object)null)));
        }

        internal static Type[] ResolveRegisteredToolTypes(HashSet<string> enabled, RvtMcpConfig config)
        {
            var types = new List<Type>();
            if (enabled.Contains("query"))      types.Add(typeof(QueryTools));
            if (enabled.Contains("query"))      types.Add(typeof(AddinQueryTools));
            if (enabled.Contains("view"))       types.Add(typeof(ViewTools));
            if (enabled.Contains("export"))     types.Add(typeof(ExportTools));
            if (enabled.Contains("sheets"))      types.Add(typeof(SheetsTools));
            if (IncludeSendCode(enabled, config)) types.Add(typeof(SendCodeTools));
            if (enabled.Contains("meta"))       types.Add(typeof(MetaTools));
            if (enabled.Contains("meta"))       types.Add(typeof(AddinMetaTools));
            return types.ToArray();
        }
    }

    /// <summary>
    /// Shared plugin-connection plumbing used by every toolset class. Owns the socket/
    /// pipe lifecycle, response read loop, pending-request correlation, and session
    /// call recording. Toolset classes contain only the MCP tool-method shells.
    /// Every wire command is gated by <see cref="RevitTargetBinding"/> (spec §6):
    /// a call pins the binding generation when it arrives, and the command is only
    /// written to the connection while the pinned generation still matches.
    /// </summary>
    internal static partial class ToolGateway
    {
        public static Memory.SessionContext Session { get; set; }

        /// <summary>Calendar year of the bound Revit instance, or null when nothing is bound.</summary>
        public static string CurrentRevitVersion =>
            RevitTargetBinding.Snapshot().Record?.HostYear.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Plugin-side wait is the primary timeout authority; the server adds a
        // short grace so the plugin's structured timeout response wins the race.
        private static readonly TimeSpan TransportGrace = TimeSpan.FromSeconds(5);

        // Tests assign this to record the wire call without opening a socket.
        internal static Func<string, object, int?, Task<JObject>> SendOverride;

        public static async Task<JObject> SendToRevit(string command, object parameters = null, int? timeoutSeconds = null)
        {
            var config = ServerState.Config ?? new RvtMcpConfig();
            var rejection = ToolReadPolicy.Rejection(command, config.ReadOnlyOrDefault, config.EnableSendCodeOrDefault);
            if (rejection != null) throw new InvalidOperationException(rejection);
            var sendOverride = SendOverride;
            if (sendOverride != null)
                return await sendOverride(command, parameters, timeoutSeconds);

            var callCtx = TargetCallContext.Current;
            long pinned;
            if (callCtx != null && callCtx.InToolCall)
            {
                pinned = callCtx.PinnedGeneration;
            }
            else
            {
                // Code outside a tool call must never trigger first selection (§6.4);
                // it may reuse the existing binding only.
                var snap = RevitTargetBinding.Snapshot();
                if (snap.State == BindingState.None || snap.Record == null)
                    throw new InvalidOperationException(
                        "No Revit target is bound; this call did not come through an MCP tool request.");
                pinned = snap.Generation;
            }

            var id = $"req-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var binding = RevitTargetBinding.Binding;

            // Establishment can take seconds (scan, descriptor wait, socket connect,
            // catalog handshake) — run it off the caller's context.
            var (conn, effectiveGeneration) = await Task.Run(() => EnsureConnectedFor(pinned, callCtx));
            (conn, effectiveGeneration) = await WriteRequest(
                conn, effectiveGeneration, id, command, parameters, timeoutSeconds, config, tcs, callCtx);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int budgetSeconds = timeoutSeconds ?? 60;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(budgetSeconds) + TransportGrace);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _pending.TryRemove(id, out _);
                sw.Stop();
                throw new TimeoutException($"Request timed out ({budgetSeconds}s). Revit may be in a modal dialog.");
            }

            sw.Stop();
            string responseLine;
            try
            {
                responseLine = await tcs.Task;
            }
            catch (ConnectionInterruptedException)
            {
                throw new TargetException(binding.Interrupted(effectiveGeneration));
            }
            var response = JObject.Parse(responseLine);
            return InterpretResponse(response);
        }

        internal static JObject InterpretResponse(JObject response)
        {
            var changes = ChangeSummary.ForAgent(response["changes"]);
            ChangeCaptureContext.Record(changes);

            if (response.Value<bool>("success"))
            {
                var data = response["data"] as JObject ?? new JObject();
                var responseWarning = response.Value<string>("warning");
                if (!string.IsNullOrWhiteSpace(responseWarning))
                    data["_response_warning"] = responseWarning;
                if (changes != null) data["_changes"] = changes;
                return data;
            }
            else
            {
                var error = response.Value<string>("error") ?? "Unknown error from Revit";
                throw new InvalidOperationException(UnknownCommandGuidance(error, CurrentRevitVersion) ?? error);
            }
        }

        /// <summary>
        /// A plugin that predates a tool answers "Unknown command: &lt;name&gt;". Say what that means and what to do,
        /// because the same server can reach several Revit years that carry different add-in builds.
        /// Returns null when the text is not that error.
        /// </summary>
        internal static string UnknownCommandGuidance(string error, string revitYear)
        {
            const string prefix = "Unknown command: ";
            if (string.IsNullOrWhiteSpace(error) || !error.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            var command = error.Substring(prefix.Length).Trim();
            if (command.Length == 0 || command.Any(char.IsWhiteSpace))
                return null;

            var year = string.IsNullOrWhiteSpace(revitYear) ? "this Revit" : "Revit " + revitYear;
            return $"The add-in loaded in {year} does not know the command '{command}'. It is most likely an older build than this server. "
                 + "Install the add-in from the same release as the server for that Revit year and restart Revit. "
                 + "If another Revit instance already has the newer add-in, call revit_list_available_targets to see its pid, "
                 + "then pin it with revit_switch_target(\"pid:<n>\") — never guess.";
        }

        /// <summary>
        /// Long-run tools accept a per-call plugin wait of 1-900 seconds (default 600).
        /// Out-of-range values are rejected rather than clamped so a wrong unit
        /// (e.g. 600000 milliseconds) surfaces as an error instead of a silent cap.
        /// </summary>
        internal static string ValidateTimeoutSeconds(int timeoutSeconds)
        {
            if (timeoutSeconds < 1 || timeoutSeconds > 900)
                return $"Error: timeout_seconds must be between 1 and 900 seconds (got {timeoutSeconds}). The default is 600 and the unit is seconds, not milliseconds.";
            return null;
        }
    }

    // =====================================================================
    // Toolset classes — one per aspect #3 §A3 group. Registration happens in
    // Program.RegisterToolsets() driven by config.Toolsets. Each method wraps
    // ToolGateway.SendToRevit with a catch-all that surfaces the error to the
    // MCP client as plain text instead of throwing.
    // =====================================================================

    [McpServerToolType, Toolset("query")]
    public class QueryTools
    {
        [McpServerTool(Name = "revit_get_current_view_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description("Get active view info. Returns viewName, viewType (FloorPlan/Section/3D/Sheet), level, scale, detailLevel, displayStyle. Call before creating elements to know active level.")]
        public static async Task<string> GetCurrentViewInfo()
        {
            try
            {
                var result = await ToolGateway.SendToRevit("get_current_view_info");
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

    }

    [McpServerToolType, Toolset("view")]
    public class ViewTools
    {
        [McpServerTool(Name = "revit_capture_view_image", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), System.ComponentModel.Description("Export a view to a raster image. output_path is optional; if provided it must be absolute and inside %TEMP% or %LOCALAPPDATA%\\Bimwright\\rvt-mcp\\captures\\. Params: view_id (optional, default active), output_path (optional, defaults under captures), pixel_size (default 1600), image_format ('png'|'jpeg', default 'png')." +
            " May overwrite existing output files. Revit Undo cannot restore them; back up the destination first.")]
        public static async Task<string> CaptureViewImage(
            string output_path = null,
            long? view_id = null, int pixel_size = 1600, string image_format = "png")
        {
            var blocked = ServerState.BlockIfReadOnly("capture_view_image");
            if (blocked != null) return blocked;

            try
            {
                var result = await ToolGateway.SendToRevit("capture_view_image", new
                {
                    view_id,
                    output_path,
                    pixel_size,
                    image_format
                });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        [McpServerTool(Name = "revit_activate_view", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description("Set the active view in Revit UI. UI-only operation. Params: view_id OR view_name (required).")]
        public static async Task<string> ActivateView(long? view_id = null, string view_name = null)
        {
            var blocked = ServerState.BlockIfReadOnly("activate_view");
            if (blocked != null) return blocked;

            try
            {
                var result = await ToolGateway.SendToRevit("activate_view", new { view_id, view_name });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

    }

    [McpServerToolType, Toolset("export")]
    public class ExportTools
    {
        [McpServerTool(Name = "revit_export_pdf", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), System.ComponentModel.Description("Export sheets or views to PDF. outputFolder must be an existing absolute path. viewIds defaults to the active view. combine=true produces one combined PDF. Do not retry on 60s timeout; Revit may still be running the command." +
            " May overwrite existing output files. Revit Undo cannot restore them; back up the destination first.")]
        public static async Task<string> ExportPdf(string outputFolder, long[] viewIds = null, bool combine = false, string fileName = "")
        {
            try
            {
                var result = await ToolGateway.SendToRevit("export_pdf", new { output_folder = outputFolder, view_ids = viewIds, combine, file_name = fileName });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        [McpServerTool(Name = "revit_export_image", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), System.ComponentModel.Description("Export a view to a raster image. outputPath is an absolute file path (.png/.jpg). viewId defaults to the active view. pixelSize sets the longer dimension. imageFormat: png|jpeg." +
            " May overwrite existing output files. Revit Undo cannot restore them; back up the destination first.")]
        public static async Task<string> ExportImage(string outputPath, long? viewId = null, int pixelSize = 2048, string imageFormat = "png")
        {
            try
            {
                var result = await ToolGateway.SendToRevit("export_image", new { output_path = outputPath, view_id = viewId, pixel_size = pixelSize, image_format = imageFormat });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

    }

    [McpServerToolType, Toolset("meta")]
    public class SendCodeTools
    {
        [McpServerTool(Name = "revit_send_code_to_revit"), System.ComponentModel.Description("Compile + run C# inside Revit for workflows not covered by typed tools. Variables: doc (Document), uidoc (UIDocument), app (UIApplication) - doc and uidoc are null when no model is open, which is legal: use app.OpenAndActivateDocument(path) to open one, or prefer the typed revit_open_model tool. Write a C# body ending in return; helper type declarations may accompany the body. Or provide a complete public McpDynamicScript with public static object Run(UIApplication app). For transactions/StairsEditScope.Commit, RvtMcp.Plugin.SafeFailuresPreprocessor records/deletes warnings and rolls back errors; report HadWarnings/Messages as committed_with_warnings; inspect HadErrors and commit status before reporting success. Output above 700 KiB auto-spills to a local same-machine file with schema and preview; there is no output parameter. Remote clients receive preview but cannot read the local file. Namespaces: System, System.Linq, System.Collections.Generic, Autodesk.Revit.DB, Autodesk.Revit.UI.")]
        public static async Task<string> SendCodeToRevit(string code)
        {
            try
            {
                var result = await ToolGateway.SendToRevit("send_code_to_revit", new { code });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }
    }

    [McpServerToolType, Toolset("meta")]
    public class MetaTools
    {
        [McpServerTool(Name = "revit_list_recent_models", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description(
            "List this Revit's recent models from its Revit.ini Recent File List. Works when no document is open. " +
            "Does not open a file. Ask the user which path to open, then call revit_open_model with that absolute path. " +
            "Returns up to 50 entries, most recent first, each with index, path, file name, and whether the file still exists.")]
        public static async Task<string> ListRecentModels()
        {
            try
            {
                var result = await ToolGateway.SendToRevit("list_recent_models");
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        [McpServerTool(Name = "revit_open_model", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), System.ComponentModel.Description(
            "Open a Revit model, template or family from disk and make it the active document. " +
            "THIS IS THE TOOL TO CALL WHEN NO DOCUMENT IS OPEN. Other tools then report that Revit is running with no document and name revit_list_recent_models. Ask the user which recent file to open before calling this. " +
            "path: an absolute path on the machine Revit runs on, NOT on the MCP client's machine. " +
            "A workshared .rvt is never opened directly: a new local copy is always created from its central model " +
            "(same as Revit's Open > 'Create New Local') under the Revit.ini ProjectPath folder as <central>_<username>.rvt - " +
            "no ProjectPath means the call stops and reports it. " +
            "A file whose header cannot be read is never opened because its worksharing state is unknown. " +
            "If a local with that name already exists it is renamed with a timestamp - never deleted or overwritten - " +
            "unless it is open in the session, has changes not yet saved to central, or its header cannot be read, " +
            "in which case the call stops and asks the user. " +
            "There is no detach option - detaching from central or opening a central directly must go through revit_send_code_to_revit. " +
            "activate=true (default) opens it in the UI and makes it active; false opens it in the background with no view, where tools reading the active document will not see it. " +
            "worksets (all|none|lastViewed) applies to workshared models only. audit=true is slow - use it on a suspect file. " +
            "A model Revit already has open is reported back (and activated when activate=true) rather than reopened. " +
            "Two known refusals: a file already loaded as a link in an open document cannot be opened standalone (close the host document or unload the link first), " +
            "and a central saved in an older Revit version cannot create a local - if that fails after an existing local was renamed aside, the error names the timestamped path it was kept as; nothing is lost. " +
            "timeout_seconds: 1-900, default 600 - opening a model that pulls in many links commonly exceeds 60s (measured: a 20 MB model with 5 links took 64s). " +
            "Returns: {opened, was_already_open, title, path, saved_in_version, is_workshared, is_family, header_worksharing, central_path, local_path, local_file_created, renamed_existing_local, activated, active_view}. " +
            "NOTE: opening a model saved in an older Revit upgrades it in memory - that only reaches disk if something saves it.")]
        public static async Task<string> OpenModel(string path, bool activate = true, bool audit = false, string worksets = null, int timeout_seconds = 600)
        {
            var blocked = ServerState.BlockIfReadOnly("open_model");
            if (blocked != null) return blocked;

            var timeoutError = ToolGateway.ValidateTimeoutSeconds(timeout_seconds);
            if (timeoutError != null) return timeoutError;

            try
            {
                var parameters = new { path, activate, audit, worksets };
                var result = await ToolGateway.SendToRevit("open_model", parameters, timeout_seconds);
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        /// <summary>§6.9 binding block shared by get_current_target and switch_target.</summary>
        internal static JObject BindingJson(BindingSnapshot snap, IReadOnlyList<TargetCandidate> live)
        {
            var record = snap.Record;
            var windowTitle = record != null
                ? live.FirstOrDefault(c => c.Pid == record.Pid)?.WindowTitle
                : null;
            return new JObject
            {
                ["selector"] = snap.Selector.Canonical,
                ["binding"] = snap.State == BindingState.None || record == null
                    ? "none"
                    : record.Kind == BindingKind.Explicit ? "explicit" : "implicit",
                ["binding_state"] = snap.State switch
                {
                    BindingState.Bound => "bound",
                    BindingState.Lost => "lost",
                    _ => "none",
                },
                ["generation"] = snap.Generation,
                ["bound_target_id"] = record?.TargetId,
                ["bound_pid"] = record != null ? (JToken)record.Pid : JValue.CreateNull(),
                ["bound_host_year"] = record != null ? (JToken)record.HostYear : JValue.CreateNull(),
                ["identity_verified"] = record?.IdentityVerified ?? false,
                ["window_title"] = windowTitle != null ? (JToken)windowTitle : JValue.CreateNull(),
                ["next_target_id"] = (JToken)RevitTargetBinding.NextTargetId(live) ?? JValue.CreateNull(),
                ["confirmation_required"] = snap.ConfirmationRequired,
                ["last_switch"] = snap.LastSwitch != null
                    ? new JObject
                    {
                        ["from"] = snap.LastSwitch.From,
                        ["to"] = snap.LastSwitch.To,
                        ["at_utc"] = snap.LastSwitch.AtUtc.ToString("o")
                    }
                    : JValue.CreateNull()
            };
        }

        /// <summary>§6.10 rvt envelope around a target payload: {success:false, error:"CODE: …", target:{…}}.</summary>
        internal static JObject TargetEnvelope(TargetPayload payload)
        {
            var json = payload.ToJson();
            FillWindowTitles(json);
            return new JObject
            {
                ["success"] = false,
                ["error"] = payload.Message,
                ["target"] = json
            };
        }

        /// <summary>
        /// The connect path scans without window titles (probe skips MainWindowTitle);
        /// fill them here from the live process so error payloads still carry
        /// <c>window_title</c> for agent disambiguation (§6.9).
        /// </summary>
        internal static void FillWindowTitles(JObject targetJson)
        {
            Fill(targetJson["previous"] as JObject);
            Fill(targetJson["current"] as JObject);
            if (targetJson["candidates"] is JArray candidates)
                foreach (var c in candidates.OfType<JObject>()) Fill(c);

            static void Fill(JObject o)
            {
                if (o == null || o["window_title"] != null || o["pid"] is not JValue pidTok) return;
                var title = RevitTargetBinding.WindowTitle(pidTok.Value<int>());
                if (title != null) o["window_title"] = title;
            }
        }

        [McpServerTool(Name = "revit_list_available_targets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description(
            "List every Revit instance currently running with the rvt-mcp plugin loaded. " +
            "Reads the discovery directory %LOCALAPPDATA%\\Bimwright\\rvt-mcp\\ — both per-instance revit-<year>-<pid>.json and legacy revit-<year>.json files. " +
            "Multiple instances may share a year — identify the one you want by pid and window_title, then pin it with revit_switch_target(\"pid:<n>\"). Never guess. " +
            "Use this BEFORE revit_switch_target. " +
            "Returns: {discovery_dir, count, targets: [{year, transport, port, pipe_name, pid, discovery_file, is_currently_connected, target_id, host_year, process_start_utc, identity_verified, source, is_current, window_title}]}. " +
            "If count == 0, no Revit is running or no plugin is loaded — instruct the user to start Revit and enable the rvt-mcp plugin.")]
        public static string ListAvailableTargets()
        {
            try
            {
                var dir = AuthToken.DiscoveryDir();
                var scan = RevitTargetBinding.Scan();
                var snap = RevitTargetBinding.Snapshot();
                var connectedId = ToolGateway.ConnectedTargetId;
                var targets = scan.Live.Select(c => new
                {
                    year = c.HostYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    transport = c.Descriptor.Transport,
                    port = c.Descriptor.Transport == "tcp" ? c.Descriptor.Port : null,
                    pipe_name = c.Descriptor.Transport == "pipe" ? c.Descriptor.PipeName : null,
                    pid = c.Pid,
                    discovery_file = Path.Combine(dir, c.Descriptor.FileName),
                    is_currently_connected = string.Equals(c.TargetId, connectedId, StringComparison.OrdinalIgnoreCase),
                    target_id = c.TargetId,
                    host_year = c.HostYear,
                    process_start_utc = c.ObservedStartUtc?.ToString("o"),
                    identity_verified = c.IdentityVerified,
                    source = c.Descriptor.Source == DescriptorSource.PerInstance ? "per_instance" : "legacy",
                    is_current = snap.Record != null && string.Equals(snap.Record.TargetId, c.TargetId, StringComparison.OrdinalIgnoreCase),
                    window_title = c.WindowTitle
                }).ToArray();
                return JsonConvert.SerializeObject(new
                {
                    discovery_dir = dir,
                    count = targets.Length,
                    targets,
                    note = targets.Length == 0
                        ? "No revit-*.json descriptors found. Start Revit and ensure the rvt-mcp plugin is loaded (Add-Ins ribbon)."
                        : "Pin an instance with revit_switch_target — 'version' accepts a 4-digit year, 'pid:<n>' or 'id:<target_id>'. When several instances share a year, always pin with pid:<n>."
                }, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        [McpServerTool(Name = "revit_get_current_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description(
            "Report which Revit instance this MCP server will route the NEXT command to. " +
            "Returns: {pinned_target, currently_connected_year (or null), currently_connected_pid (or null), discovery_dir, selector, binding, binding_state, generation, bound_target_id, bound_pid, bound_host_year, identity_verified, window_title, next_target_id, confirmation_required, last_switch}. " +
            "When confirmation_required is true the previous bound Revit is gone — call revit_switch_target to confirm a candidate before repeating the call. " +
            "Use to verify routing before sending Revit-modifying commands when multiple Revits are open.")]
        public static string GetCurrentTarget()
        {
            try
            {
                var snap = RevitTargetBinding.Snapshot();
                var live = RevitTargetBinding.Scan().Live;
                var block = BindingJson(snap, live);
                block["pinned_target"] = snap.Selector.Canonical;
                block["currently_connected_year"] = ToolGateway.ConnectedTargetId != null
                    ? ToolGateway.CurrentRevitVersion
                    : null;
                block["currently_connected_pid"] = ToolGateway.ConnectedPid;
                block["discovery_dir"] = AuthToken.DiscoveryDir();
                return JsonConvert.SerializeObject(block, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        [McpServerTool(Name = "revit_switch_target", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description(
            "Switch the active Revit binding to a specific instance when multiple Revits are running — including several instances of the same year. " +
            "version: a selector — 'auto', a 4-digit calendar year, 'pid:<n>', or 'id:revit-<year>-<pid>'. " +
            "When several instances share a year, ALWAYS pin with pid:<n> after listing. " +
            "pid: optional; when given it wins over version (same as version='pid:<n>') - kept for callers that pass (version, pid). " +
            "DO NOT pass R-codes like 'R22' or 'R24' — they are rejected with an educational error. " +
            "DO NOT guess. ALWAYS call revit_list_available_targets first to see which instances are actually running (pid, window_title). " +
            "A valid switch after TARGET_CHANGED confirms the proposed replacement — calls keep failing until you confirm. " +
            "Switching to a different instance closes the old connection; any command still in flight ends as TARGET_INTERRUPTED. " +
            "Returns: {ok, previous: {target_id, pid}|null, binding: {…}, verified, activeView} plus legacy previousTarget/newTarget strings. " +
            "verify=true (default): immediately attempts get_current_view_info against the new target to confirm connectivity; " +
            "set verify=false to skip when the new target's document isn't in a view yet (e.g., Revit just launched).")]
        public static async Task<string> SwitchTarget(string version, bool verify = true, int? pid = null)
        {
            try
            {
                if (pid.HasValue) version = "pid:" + pid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var snapBefore = RevitTargetBinding.Snapshot();

                // Parse the selector first — an invalid one changes nothing (§6.8).
                if (!SelectorParser.TryParse(HostProduct.Revit, version, out var selector, out var selError, out var selMessage))
                {
                    var payload = TargetPayload.InvalidSelector(HostProduct.Revit, version,
                        selError == SelectorError.RCode
                            ? "Invalid version format '" + version + "'. v0.5+ uses 4-digit calendar years, NOT R-codes. " +
                              "Translate: R22=2022, R23=2023, R24=2024, R25=2025, R26=2026, R27=2027. " +
                              "BEFORE calling this tool again, call revit_list_available_targets to see exactly which instances are running on this machine (pid, window_title). " +
                              "Do not guess."
                            : "Invalid version '" + version + "'. Allowed: auto | 2022..2027 | pid:<n> | id:revit-<year>-<pid>. " +
                              (selMessage != null ? "(" + selMessage + ") " : "") +
                              "Call revit_list_available_targets first to see which instances are actually running on this machine.");
                    var invalidEnv = TargetEnvelope(payload);
                    invalidEnv["ok"] = false;
                    invalidEnv["allowed_versions"] = new JArray(AuthToken.AllVersions);
                    invalidEnv["recommended_next_tool"] = "revit_list_available_targets";
                    return JsonConvert.SerializeObject(invalidEnv, Formatting.Indented);
                }

                var live = RevitTargetBinding.Scan().Live;
                var result = ToolGateway.SwitchBinding(selector, live);
                if (!result.Ok)
                {
                    var failEnv = TargetEnvelope(result.Failure);
                    failEnv["ok"] = false;
                    failEnv["allowed_versions"] = new JArray(AuthToken.AllVersions);
                    failEnv["recommended_next_tool"] = result.Failure.RecommendedNextTool;
                    return JsonConvert.SerializeObject(failEnv, Formatting.Indented);
                }

                // This call's pinned generation follows the binding it just established,
                // so the verify probe below is sent under the new generation.
                var callCtx = TargetCallContext.Current;
                if (callCtx != null) callCtx.PinnedGeneration = RevitTargetBinding.Snapshot().Generation;

                var previous = result.Previous != null
                    ? (JToken)new JObject { ["target_id"] = result.Previous.TargetId, ["pid"] = result.Previous.Pid }
                    : JValue.CreateNull();
                var previousTarget = snapBefore.Record?.HostYear.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "auto";
                var newTarget = result.Current?.HostYear.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "auto";

                if (!verify)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        ok = true,
                        previous,
                        binding = BindingJson(RevitTargetBinding.Snapshot(), live),
                        verified = false,
                        previousTarget,
                        newTarget,
                        note = "Target updated. Next tool call will connect to new target."
                    }, Formatting.Indented);
                }

                try
                {
                    var probe = await ToolGateway.SendToRevit("get_current_view_info");
                    return JsonConvert.SerializeObject(new
                    {
                        ok = true,
                        previous,
                        binding = BindingJson(RevitTargetBinding.Snapshot(), live),
                        verified = true,
                        activeView = probe.Value<string>("viewName"),
                        previousTarget,
                        newTarget
                    }, Formatting.Indented);
                }
                catch (Exception verifyEx)
                {
                    // The switch itself succeeded; a verify failure must not replace this
                    // meta-tool response with the probe's target envelope.
                    if (callCtx != null) callCtx.Payload = null;
                    return JsonConvert.SerializeObject(new
                    {
                        ok = true,
                        previous,
                        binding = BindingJson(RevitTargetBinding.Snapshot(), live),
                        verified = false,
                        verifyError = verifyEx.Message,
                        verify_target = (verifyEx as TargetException)?.Payload is { } vp
                            ? MetaTools.TargetEnvelope(vp)["target"] : null,
                        previousTarget,
                        newTarget,
                        note = "Target set, but verify failed. The next tool call may still succeed."
                    }, Formatting.Indented);
                }
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

    }

    internal static class McpJsonInput
    {
        // MCP binds untyped array items to System.Text.Json.JsonElement. Newtonsoft
        // must read their JSON contents, including elements nested in native objects.
        public static JToken FromObject(object value)
        {
            var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
            serializer.Converters.Add(new JsonElementConverter());
            return JToken.FromObject(value, serializer);
        }

        internal sealed class JsonElementConverter : Newtonsoft.Json.JsonConverter<JsonElement>
        {
            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, JsonElement value, Newtonsoft.Json.JsonSerializer serializer)
            {
                if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null)
                    writer.WriteNull();
                else
                    JToken.Parse(value.GetRawText()).WriteTo(writer);
            }

            public override JsonElement ReadJson(JsonReader reader, Type objectType, JsonElement existingValue,
                bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer) => throw new NotSupportedException();
        }

        public static JArray RequiredArray(object value, string parameterName)
        {
            var array = OptionalArray(value, parameterName);
            if (array == null)
                throw new ArgumentException($"{parameterName} must be a JSON array.");
            return array;
        }

        public static JArray OptionalArray(object value, string parameterName)
        {
            if (value == null)
                return null;

            if (value is JArray jArray)
                return jArray;

            if (value is JToken token)
            {
                if (token.Type == JTokenType.Null)
                    return null;
                if (token.Type == JTokenType.Array)
                    return (JArray)token;
                throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                    return null;
                if (element.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
                return JArray.Parse(element.GetRawText());
            }

            if (value is string)
                throw new ArgumentException($"{parameterName} must be a JSON array, not a string.");

            var tokenFromObject = FromObject(value);
            if (tokenFromObject.Type == JTokenType.Array)
                return (JArray)tokenFromObject;

            throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
        }

        public static JObject OptionalObject(object value, string parameterName)
        {
            if (value == null)
                return null;

            if (value is JObject jObject)
                return jObject;

            if (value is JToken token)
            {
                if (token.Type == JTokenType.Null)
                    return null;
                if (token.Type == JTokenType.Object)
                    return (JObject)token;
                throw new ArgumentException($"{parameterName} must be a JSON object when supplied.");
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                    return null;
                if (element.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException($"{parameterName} must be a JSON object when supplied.");
                return JObject.Parse(element.GetRawText());
            }

            if (value is string)
                throw new ArgumentException($"{parameterName} must be a JSON object, not a string.");

            var tokenFromObject = FromObject(value);
            if (tokenFromObject.Type == JTokenType.Object)
                return (JObject)tokenFromObject;

            throw new ArgumentException($"{parameterName} must be a JSON object when supplied.");
        }
    }

    [McpServerToolType, Toolset("sheets")]
    public class SheetsTools
    {
        private static JArray NormalizeOptionalJsonArray(object value, string parameterName)
        {
            if (value == null)
                return null;

            if (value is JArray jArray)
                return jArray;

            if (value is JToken token)
            {
                if (token.Type == JTokenType.Null)
                    return null;
                if (token.Type == JTokenType.Array)
                    return (JArray)token;
                throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                    return null;
                if (element.ValueKind != JsonValueKind.Array)
                    throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
                return JArray.Parse(element.GetRawText());
            }

            if (value is string)
                throw new ArgumentException($"{parameterName} must be a JSON array, not a string.");

            try
            {
                var tokenFromObject = McpJsonInput.FromObject(value);
                if (tokenFromObject.Type == JTokenType.Null)
                    return null;
                if (tokenFromObject.Type == JTokenType.Array)
                    return (JArray)tokenFromObject;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"{parameterName} could not be converted to a JSON array: {ex.Message}");
            }

            throw new ArgumentException($"{parameterName} must be a JSON array when supplied.");
        }

        [McpServerTool(Name = "revit_list_sheets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), System.ComponentModel.Description("List sheets matching filters, with viewport and schedule counts, title blocks, and revisions")]
        public static async Task<string> ListSheets(string numberFilter = "", string namePattern = "", bool includeRevisions = true, bool includeViewports = false, bool includePlaceholders = true, int limit = 1000)
        {
            try
            {
                var result = await ToolGateway.SendToRevit("list_sheets", new
                {
                    number_filter = numberFilter,
                    name_pattern = namePattern,
                    include_revisions = includeRevisions,
                    include_viewports = includeViewports,
                    include_placeholders = includePlaceholders,
                    limit
                });
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

    }

}

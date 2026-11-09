using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Targeting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;

namespace RvtMcp.Server
{
    internal enum AddinCliVerb
    {
        List,
        Call
    }

    internal sealed class AddinCliRequest
    {
        public AddinCliVerb Verb { get; set; }
        public string Addin { get; set; }
        public string Command { get; set; }
        public JObject Args { get; set; } = new JObject();
        public string Target { get; set; }
        public bool ReadOnly { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// <c>rvt-mcp addin list|call</c>: the add-in commands of a running Revit from a terminal (contract §4), through the same
    /// plugin commands and target selection as the MCP tools. stdout: JSON (the catalog, or the add-in's envelope as it
    /// came); stderr: one line on failure. Exit 0 ok, 1 the command or rvt-mcp refused, 2 bad arguments or no Revit
    /// reachable. With several Revit sessions and no --target it lists them and stops - it never guesses.
    /// </summary>
    internal static class AddinCli
    {
        public const int UsageExitCode = 2;
        public const string Usage =
            "usage: rvt-mcp addin list [--target <selector>] | rvt-mcp addin call <addin> <command> [--args <json>] [--target <selector>] [--read-only] [--timeout <seconds>]";

        public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (!TryParse(args, out var request, out var parseError))
            {
                stderr.WriteLine("rvt-mcp addin: " + parseError);
                stderr.WriteLine(Usage);
                return UsageExitCode;
            }

            var selector = TargetSelector.Auto;
            if (request.Target != null
                && !SelectorParser.TryParse(HostProduct.Revit, request.Target, out selector, out _, out var selectorMessage))
            {
                stderr.WriteLine($"rvt-mcp addin: invalid --target '{request.Target}': {selectorMessage ?? "expected auto | <year> | pid:<n> | id:revit-<year>-<pid>"}");
                return UsageExitCode;
            }

            var live = RevitTargetBinding.ScanLive();
            var matching = live.Where(candidate => selector.Matches(candidate.Descriptor)).ToList();
            if (matching.Count == 0)
            {
                stderr.WriteLine("rvt-mcp addin: no running Revit with rvt-mcp matches '" + selector.Canonical + "'. " + Sessions(live));
                return UsageExitCode;
            }

            if (request.Target == null && matching.Count > 1)
            {
                stderr.WriteLine("rvt-mcp addin: several Revit sessions are running; pick one with --target pid:<n>. " + Sessions(live));
                return UsageExitCode;
            }

            RevitTargetBinding.Initialize(selector);
            ServerState.Config = new RvtMcpConfig { ReadOnly = request.ReadOnly, EnableChangeHistory = false };
            var context = TargetCallContext.Begin();
            try
            {
                if (request.Verb == AddinCliVerb.List)
                {
                    var catalogs = await ToolGateway.SendToRevit("list_addin_commands");
                    stdout.WriteLine(JsonConvert.SerializeObject(catalogs, Formatting.Indented));
                    return 0;
                }

                if (request.ReadOnly)
                {
                    var refusal = await AddinMetaTools.ReadOnlyRefusal(request.Addin, request.Command);
                    if (refusal != null)
                    {
                        stderr.WriteLine("rvt-mcp addin: " + refusal.Substring("Error: ".Length));
                        return 1;
                    }
                }

                var envelope = await ToolGateway.SendToRevit("call_addin_command",
                    new { addin = request.Addin, command = request.Command, args = request.Args, entry = "cli", read_only = request.ReadOnly },
                    request.TimeoutSeconds);
                stdout.WriteLine(JsonConvert.SerializeObject(envelope, Formatting.Indented));
                var exit = ExitCodeFor(envelope);
                if (exit != 0)
                    stderr.WriteLine("rvt-mcp addin: " + (envelope["error"]?.Value<string>("code") ?? "error") + ": " + envelope["error"]?.Value<string>("message"));
                return exit;
            }
            catch (TargetException ex)
            {
                stderr.WriteLine("rvt-mcp addin: " + ex.Message);
                return UsageExitCode;
            }
            catch (Exception ex) when (ex is IOException || ex is System.Net.Sockets.SocketException || ex is UnauthorizedAccessException)
            {
                stderr.WriteLine("rvt-mcp addin: cannot reach Revit: " + ex.Message);
                return UsageExitCode;
            }
            catch (Exception ex)
            {
                // A refusal of rvt-mcp (addin_not_loaded, read_only_blocked, addin_protocol_mismatch...) or a timeout.
                stderr.WriteLine("rvt-mcp addin: " + ex.Message);
                return 1;
            }
            finally
            {
                TargetCallContext.End(context);
            }
        }

        /// <summary>0 for an envelope with ok=true, 1 otherwise.</summary>
        public static int ExitCodeFor(JObject envelope)
        {
            return envelope != null && envelope.Value<bool?>("ok") == true ? 0 : 1;
        }

        public static bool TryParse(string[] args, out AddinCliRequest request, out string error)
        {
            request = new AddinCliRequest();
            error = null;
            if (args == null || args.Length == 0)
            {
                error = "missing 'list' or 'call'.";
                return false;
            }

            var positional = new List<string>();
            for (var index = 1; index < args.Length; index++)
            {
                var arg = args[index];
                string Value()
                {
                    return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[++index] : null;
                }

                switch (arg)
                {
                    case "--target":
                        request.Target = Value();
                        if (request.Target == null) { error = "--target needs a value."; return false; }
                        break;
                    case "--args":
                        var json = Value();
                        if (json == null) { error = "--args needs a JSON object."; return false; }
                        try
                        {
                            if (!(JToken.Parse(json) is JObject parsed)) { error = "--args must be a JSON object."; return false; }
                            request.Args = parsed;
                        }
                        catch (JsonException ex) { error = "--args is not valid JSON: " + ex.Message; return false; }
                        break;
                    case "--read-only":
                        request.ReadOnly = true;
                        break;
                    case "--timeout":
                        if (!int.TryParse(Value(), out var seconds) || seconds < 1) { error = "--timeout needs a number of seconds."; return false; }
                        request.TimeoutSeconds = seconds;
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) { error = $"unknown option '{arg}'."; return false; }
                        positional.Add(arg);
                        break;
                }
            }

            switch (args[0])
            {
                case "list":
                    request.Verb = AddinCliVerb.List;
                    if (positional.Count > 0) { error = "'list' takes no other argument."; return false; }
                    return true;
                case "call":
                    request.Verb = AddinCliVerb.Call;
                    if (positional.Count != 2) { error = "'call' needs <addin> and <command>."; return false; }
                    request.Addin = positional[0];
                    request.Command = positional[1];
                    return true;
                default:
                    error = $"unknown verb '{args[0]}': use 'list' or 'call'.";
                    return false;
            }
        }

        private static string Sessions(IReadOnlyList<TargetCandidate> live)
        {
            return live.Count == 0
                ? "No Revit session with rvt-mcp is running."
                : "Running: " + string.Join("; ", live.Select(c => $"{c.TargetId} (Revit {c.HostYear}, pid {c.Pid}{(c.WindowTitle == null ? "" : ", " + c.WindowTitle)})"));
        }
    }
}

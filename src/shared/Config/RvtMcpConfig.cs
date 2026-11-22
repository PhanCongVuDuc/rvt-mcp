using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Single POCO read by both processes: the server consumes <see cref="Target"/> /
    /// <see cref="Toolsets"/> / <see cref="ReadOnly"/>; the plugin consumes <see cref="AllowLanBind"/>.
    ///
    /// Precedence (high → low): CLI args > env vars (BIMWRIGHT_*) > JSON file.
    /// Fields stay nullable so "not set" is distinguishable from "explicitly default-valued";
    /// resolved defaults are exposed via the *OrDefault accessors.
    /// </summary>
    public class RvtMcpConfig
    {
        public const string EnvTarget                    = "BIMWRIGHT_TARGET";
        public const string EnvToolsets                  = "BIMWRIGHT_TOOLSETS";
        public const string EnvTools                     = "BIMWRIGHT_TOOLS";
        public const string EnvReadOnly                  = "BIMWRIGHT_READ_ONLY";
        public const string EnvAllowLanBind              = "BIMWRIGHT_ALLOW_LAN_BIND";
        public const string EnvCacheSendCodeBodies       = "BIMWRIGHT_CACHE_SEND_CODE_BODIES";
        public const string EnvEnableToast               = "BIMWRIGHT_ENABLE_TOAST";
        public const string EnvUiLanguage                = "BIMWRIGHT_UI_LANGUAGE";
        public const string EnvEnableSendCode = "BIMWRIGHT_ENABLE_SEND_CODE";
        public const string EnvEnableCallLog = "BIMWRIGHT_ENABLE_CALL_LOG";
        public const string EnvEnableResponseGuard = "BIMWRIGHT_ENABLE_RESPONSE_GUARD";
        public const string EnvResponseWarnBytes = "BIMWRIGHT_RESPONSE_WARN_BYTES";
        public const string EnvResponseStrongWarnBytes = "BIMWRIGHT_RESPONSE_STRONG_WARN_BYTES";
        public const string EnvResponseBudgetBytes = "BIMWRIGHT_RESPONSE_BUDGET_BYTES";
        public const string EnvMaxResponseBytes = "BIMWRIGHT_MAX_RESPONSE_BYTES";
        public const string EnvSpillRetentionHours = "BIMWRIGHT_SPILL_RETENTION_HOURS";

        public const bool DefaultReadOnly                  = false;
        public const bool DefaultAllowLanBind              = false;
        public const bool DefaultCacheSendCodeBodies       = false;
        public const bool DefaultEnableToast               = true;
        public const int DefaultToastIdleSeconds           = 20;
        public const int DefaultSpillRetentionHours        = 36;
        public const int MaxSpillRetentionHours            = 24 * 365;

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("toolsets")]
        public List<string> Toolsets { get; set; }

        /// <summary>Per-tool allowlist (with or without the <c>revit_</c> prefix). When set, only these tools are
        /// registered; with no <see cref="Toolsets"/> it implies every toolset, so a listed tool is never hidden by
        /// toolset defaults. Read-only and send-code switches still apply on top.</summary>
        [JsonProperty("tools")]
        public List<string> Tools { get; set; }

        [JsonProperty("readOnly")]
        public bool? ReadOnly { get; set; }

        [JsonProperty("enableSendCode")]
        public bool? EnableSendCode { get; set; }

        [JsonProperty("enableCallLog")]
        public bool? EnableCallLog { get; set; }

        [JsonProperty("enableResponseGuard")]
        public bool? EnableResponseGuard { get; set; }

        [JsonProperty("responseWarnBytes")]
        public int? ResponseWarnBytes { get; set; }

        [JsonProperty("responseStrongWarnBytes")]
        public int? ResponseStrongWarnBytes { get; set; }

        [JsonProperty("responseBudgetBytes")]
        public int? ResponseBudgetBytes { get; set; }

        [JsonProperty("maxResponseBytes")]
        public int? MaxResponseBytes { get; set; }

        /// <summary>
        /// Hours a spill file is kept after it is written (default 36). Read leniently: a missing,
        /// non-numeric or out-of-range value means the default, so a bad edit never loses a spill.
        /// </summary>
        [JsonProperty("spillRetentionHours")]
        public JToken SpillRetentionHours { get; set; }

        [JsonProperty("allowLanBind")]
        public bool? AllowLanBind { get; set; }

        [JsonProperty("cacheSendCodeBodies")]
        public bool? CacheSendCodeBodies { get; set; }

        [JsonProperty("enableToast")]
        public bool? EnableToast { get; set; }

        /// <summary>Seconds of quiet before the next call raises a new Windows notification. Settings accepts 10, 20, 30, or 60 seconds.</summary>
        [JsonProperty("toastIdleSeconds")]
        public int? ToastIdleSeconds { get; set; }

        /// <summary>
        /// Plugin UI language override: "auto" or a shipped locale code (en, ja, …).
        /// Read by the plugin only; the server ignores it.
        /// </summary>
        [JsonProperty("uiLanguage")]
        public string UiLanguage { get; set; }

        /// <summary>
        /// Add-in live entries (<c>&lt;namespace&gt;.&lt;Class&gt;.&lt;Method&gt;</c>) send_code binds snippets through.
        /// Written by a repo's tooling (spex revit.ps1), so it may change while Revit is open - read it with
        /// <see cref="LiveEntriesFromFile"/> on every call, not from the startup snapshot.
        /// </summary>
        [JsonProperty("liveEntries")]
        public List<string> LiveEntries { get; set; }

        public static List<string> LiveEntriesFromFile(string configFilePath = null)
        {
            var path = configFilePath ?? ConfigFilePathOverride ?? DefaultConfigFilePath;
            var config = LoadFromJsonFile(path);
            return config?.LiveEntries ?? new List<string>();
        }

        public bool ReadOnlyOrDefault              => ReadOnly           ?? DefaultReadOnly;
        public bool EnableSendCodeOrDefault => !ReadOnlyOrDefault && (EnableSendCode ?? true);
        public bool EnableCallLogOrDefault => EnableCallLog ?? false;
        public bool EnableResponseGuardOrDefault => EnableResponseGuard ?? true;
        public int ResponseWarnBytesOrDefault => PositiveBytes(ResponseWarnBytes, 64 * 1024, "responseWarnBytes");
        public int ResponseStrongWarnBytesOrDefault => PositiveBytes(ResponseStrongWarnBytes, 256 * 1024, "responseStrongWarnBytes");
        public int ResponseBudgetBytesOrDefault => PositiveBytes(ResponseBudgetBytes, 700 * 1024, "responseBudgetBytes");
        public int MaxResponseBytesOrDefault => PositiveBytes(MaxResponseBytes, 1024 * 1024, "maxResponseBytes");

        public int SpillRetentionHoursOrDefault => NormalizeSpillRetentionHours(SpillRetentionHours);

        public static int NormalizeSpillRetentionHours(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.String))
                return DefaultSpillRetentionHours;
            var hours = ParseSpillRetentionHours(value.Type == JTokenType.String
                ? value.Value<string>() : value.ToString(Formatting.None));
            return hours ?? DefaultSpillRetentionHours;
        }

        internal static int? ParseSpillRetentionHours(string text)
        {
            if (!int.TryParse(text?.Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var hours))
                return null;
            return hours >= 1 && hours <= MaxSpillRetentionHours ? hours : (int?)null;
        }

        private static int PositiveBytes(int? value, int fallback, string name)
        {
            if (value.HasValue && value.Value < 1024)
                throw new ArgumentException(name + " must be at least 1024 bytes.");
            return value ?? fallback;
        }

        public void ValidateResponseLimits()
        {
            if (ResponseWarnBytesOrDefault > ResponseStrongWarnBytesOrDefault
                || ResponseStrongWarnBytesOrDefault > ResponseBudgetBytesOrDefault
                || ResponseBudgetBytesOrDefault > MaxResponseBytesOrDefault)
                throw new ArgumentException("Response limits must satisfy warn <= strong warning <= budget <= transport cap.");
        }

        // Only session controls cross the authenticated server/plugin boundary. Host UI
        // and code-body retention settings remain owned by the plugin's local config.
        public RvtMcpConfig ToRuntimeOptions()
        {
            return new RvtMcpConfig
            {
                ReadOnly = ReadOnlyOrDefault,
                EnableSendCode = EnableSendCodeOrDefault,
                EnableCallLog = EnableCallLogOrDefault,
                EnableResponseGuard = EnableResponseGuardOrDefault,
                ResponseWarnBytes = ResponseWarnBytesOrDefault,
                ResponseStrongWarnBytes = ResponseStrongWarnBytesOrDefault,
                ResponseBudgetBytes = ResponseBudgetBytesOrDefault,
                MaxResponseBytes = MaxResponseBytesOrDefault,
                SpillRetentionHours = new JValue(SpillRetentionHoursOrDefault)
            };
        }

        public RvtMcpConfig WithRuntimeOptions(RvtMcpConfig runtime)
        {
            if (runtime == null) return this;
            var result = (RvtMcpConfig)MemberwiseClone();
            // A client may tighten host policy, never relax it.
            result.ReadOnly = ReadOnlyOrDefault || runtime.ReadOnlyOrDefault;
            result.EnableSendCode = EnableSendCodeOrDefault && runtime.EnableSendCodeOrDefault;
            result.EnableCallLog = runtime.EnableCallLog ?? EnableCallLog;
            result.EnableResponseGuard = runtime.EnableResponseGuard ?? EnableResponseGuard;
            result.ResponseWarnBytes = runtime.ResponseWarnBytes ?? ResponseWarnBytes;
            result.ResponseStrongWarnBytes = runtime.ResponseStrongWarnBytes ?? ResponseStrongWarnBytes;
            result.ResponseBudgetBytes = runtime.ResponseBudgetBytes ?? ResponseBudgetBytes;
            result.MaxResponseBytes = runtime.MaxResponseBytes ?? MaxResponseBytes;
            result.SpillRetentionHours = runtime.SpillRetentionHours ?? SpillRetentionHours;
            result.ValidateResponseLimits();
            return result;
        }
        public bool AllowLanBindOrDefault          => AllowLanBind       ?? DefaultAllowLanBind;
        public bool CacheSendCodeBodiesOrDefault  => CacheSendCodeBodies ?? DefaultCacheSendCodeBodies;
        public bool EnableToastOrDefault          => EnableToast       ?? DefaultEnableToast;
        public int ToastIdleSecondsOrDefault      => NormalizeToastIdleSeconds(ToastIdleSeconds);

        public static int NormalizeToastIdleSeconds(int? seconds)
        {
            switch (seconds)
            {
                case 10:
                case 20:
                case 30:
                case 60:
                    return seconds.Value;
                default:
                    return DefaultToastIdleSeconds;
            }
        }

        public static string DefaultConfigFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "rvt-mcp",
                "rvtmcp.config.json");

        /// <summary>Test hook: redirects every Load that does not pass an explicit path.</summary>
        internal static string ConfigFilePathOverride { get; set; }

        /// <summary>
        /// Load config from JSON → overlay env vars → overlay CLI args. Pass <c>null</c>
        /// for args to skip the CLI layer (plugin-process callers do this since Revit
        /// does not propagate Server args).
        /// </summary>
        public static RvtMcpConfig Load(string[] args = null, string configFilePath = null)
        {
            return Load(args, configFilePath, envLookup: null);
        }

        /// <summary>
        /// Read the settings snapshot without expiry cleanup, CLI processing, or any
        /// write-back. Environment variables are an in-memory overlay only. Settings
        /// uses this method while a dialog is open so merely opening the dialog cannot
        /// mutate the user's config or journal TTL.
        /// </summary>
        public static RvtMcpConfig LoadReadOnly(string configFilePath = null)
        {
            return LoadReadOnly(configFilePath, envLookup: null);
        }

        internal static RvtMcpConfig LoadReadOnly(string configFilePath, Func<string, string> envLookup)
        {
            var path = configFilePath ?? ConfigFilePathOverride ?? DefaultConfigFilePath;
            var config = LoadFromJsonFile(path) ?? new RvtMcpConfig();
            ApplyEnvVars(config, envLookup, configFilePath: null, allowPersistenceWriteBack: false);
            return config;
        }

        internal static RvtMcpConfig Load(string[] args, string configFilePath, Func<string, string> envLookup)
        {
            var path = configFilePath ?? ConfigFilePathOverride ?? DefaultConfigFilePath;
            var config = LoadFromJsonFile(path)
                         ?? new RvtMcpConfig();

            ApplyEnvVars(config, envLookup, path);
            if (args != null) ApplyCliArgs(config, args, path);
            return config;
        }

        internal static RvtMcpConfig LoadFromJsonFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JsonConvert.DeserializeObject<RvtMcpConfig>(text);
            }
            catch
            {
                // Malformed config = ignore silently, fall back to env/CLI + code defaults.
                // Don't punish the user for a typo in a file that's optional.
                return null;
            }
        }

        internal static void ApplyEnvVars(RvtMcpConfig config, Func<string, string> lookup = null)
        {
            ApplyEnvVars(config, lookup, null, allowPersistenceWriteBack: true);
        }

        internal static void ApplyEnvVars(RvtMcpConfig config, Func<string, string> lookup, string configFilePath)
        {
            ApplyEnvVars(config, lookup, configFilePath, allowPersistenceWriteBack: true);
        }

        internal static void ApplyEnvVars(
            RvtMcpConfig config,
            Func<string, string> lookup,
            string configFilePath,
            bool allowPersistenceWriteBack)
        {
            lookup = lookup ?? Environment.GetEnvironmentVariable;

            var target = lookup(EnvTarget);
            if (!string.IsNullOrWhiteSpace(target)) config.Target = target.Trim();

            var toolsets = lookup(EnvToolsets);
            if (!string.IsNullOrWhiteSpace(toolsets)) config.Toolsets = ParseCsv(toolsets);

            var tools = lookup(EnvTools);
            if (!string.IsNullOrWhiteSpace(tools)) config.Tools = ParseCsv(tools);

            var readOnly = ParseBool(lookup(EnvReadOnly));
            if (readOnly.HasValue) config.ReadOnly = readOnly;

            config.EnableSendCode = ParseBool(lookup(EnvEnableSendCode)) ?? config.EnableSendCode;
            config.EnableCallLog = ParseBool(lookup(EnvEnableCallLog)) ?? config.EnableCallLog;
            config.EnableResponseGuard = ParseBool(lookup(EnvEnableResponseGuard)) ?? config.EnableResponseGuard;
            config.ResponseWarnBytes = ParseBytes(lookup(EnvResponseWarnBytes), EnvResponseWarnBytes) ?? config.ResponseWarnBytes;
            config.ResponseStrongWarnBytes = ParseBytes(lookup(EnvResponseStrongWarnBytes), EnvResponseStrongWarnBytes) ?? config.ResponseStrongWarnBytes;
            config.ResponseBudgetBytes = ParseBytes(lookup(EnvResponseBudgetBytes), EnvResponseBudgetBytes) ?? config.ResponseBudgetBytes;
            config.MaxResponseBytes = ParseBytes(lookup(EnvMaxResponseBytes), EnvMaxResponseBytes) ?? config.MaxResponseBytes;
            ApplySpillRetention(config, lookup(EnvSpillRetentionHours), EnvSpillRetentionHours);

            var allowLan = ParseBool(lookup(EnvAllowLanBind));
            if (allowLan.HasValue) config.AllowLanBind = allowLan;

            var cacheBodies = ParseBool(lookup(EnvCacheSendCodeBodies));
            if (cacheBodies.HasValue) config.CacheSendCodeBodies = cacheBodies;

            var enableToast = ParseBool(lookup(EnvEnableToast));
            if (enableToast.HasValue) config.EnableToast = enableToast;

            // BIMWRIGHT_UI_LANGUAGE is a Revit.exe process env var (Windows user/machine);
            // the env block in MCP client configs reaches the server, not the plugin.
            var uiLanguage = lookup(EnvUiLanguage);
            if (!string.IsNullOrWhiteSpace(uiLanguage)) config.UiLanguage = uiLanguage.Trim();

        }

        internal static void ApplyCliArgs(RvtMcpConfig config, string[] args)
        {
            ApplyCliArgs(config, args, null);
        }

        internal static void ApplyCliArgs(RvtMcpConfig config, string[] args, string configFilePath)
        {
            if (args == null) return;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--target":
                        if (i + 1 < args.Length) config.Target = args[++i];
                        break;
                    case "--toolsets":
                        if (i + 1 < args.Length) config.Toolsets = ParseCsv(args[++i]);
                        break;
                    case "--tools":
                        if (i + 1 < args.Length) config.Tools = ParseCsv(args[++i]);
                        break;
                    case "--read-only":
                        config.ReadOnly = true;
                        break;
                    case "--enable-send-code": config.EnableSendCode = true; break;
                    case "--disable-send-code": config.EnableSendCode = false; break;
                    case "--enable-call-log": config.EnableCallLog = true; break;
                    case "--disable-call-log": config.EnableCallLog = false; break;
                    case "--enable-response-guard": config.EnableResponseGuard = true; break;
                    case "--disable-response-guard": config.EnableResponseGuard = false; break;
                    case "--response-warn-bytes": config.ResponseWarnBytes = ReadBytes(args, ref i); break;
                    case "--response-strong-warn-bytes": config.ResponseStrongWarnBytes = ReadBytes(args, ref i); break;
                    case "--response-budget-bytes": config.ResponseBudgetBytes = ReadBytes(args, ref i); break;
                    case "--max-response-bytes": config.MaxResponseBytes = ReadBytes(args, ref i); break;
                    case "--spill-retention-hours":
                        ApplySpillRetention(config, i + 1 < args.Length ? args[++i] : null, arg);
                        break;
                    case "--allow-lan-bind":
                        config.AllowLanBind = true;
                        break;
                    case "--cache-send-code-bodies":
                        config.CacheSendCodeBodies = true;
                        break;
                    case "--no-cache-send-code-bodies":
                        config.CacheSendCodeBodies = false;
                        break;
                }
            }
        }

        // An unusable value must not throw: it leaves the default in place (36 h) and says so on stderr,
        // so a typo can never shorten how long an agent's only copy of an output survives.
        private static void ApplySpillRetention(RvtMcpConfig config, string text, string source)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var hours = ParseSpillRetentionHours(text);
            if (hours.HasValue)
            {
                config.SpillRetentionHours = new JValue(hours.Value);
                return;
            }
            config.SpillRetentionHours = null;
            Console.Error.WriteLine($"[rvt-mcp] {source}: '{text}' is not 1-{MaxSpillRetentionHours} hours; using {DefaultSpillRetentionHours}.");
        }

        private static int ReadBytes(string[] args, ref int index)
        {
            var flag = args[index];
            if (index + 1 >= args.Length)
                throw new ArgumentException(flag + " requires a byte count.");
            return ParseBytes(args[++index], flag) ?? throw new ArgumentException(flag + " requires a byte count.");
        }

        private static int? ParseBytes(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!int.TryParse(value, out var bytes) || bytes < 1024)
                throw new ArgumentException(name + " must be an integer of at least 1024 bytes.");
            return bytes;
        }

        internal static bool? ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                    return true;
                case "0":
                case "false":
                case "no":
                    return false;
                default:
                    return null;
            }
        }

        internal static List<string> ParseCsv(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return result;
            foreach (var part in value.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        /// <summary>Persisted settings that can be staged by the Settings dialog.</summary>
        public sealed class SettingsPatch
        {
            public int? ToastIdleSeconds { get; set; }
            public bool? CacheSendCodeBodies { get; set; }
        }

        public sealed class ApplyFailure
        {
            public ApplyFailure(string key, string error)
            {
                Key = key;
                Error = error;
            }

            public string Key { get; }
            public string Error { get; }
        }

        /// <summary>Per-key result returned by <see cref="TryApplySettings"/>.</summary>
        public sealed class ApplyResult
        {
            public IList<string> Applied { get; } = new List<string>();
            public IList<ApplyFailure> Failed { get; } = new List<ApplyFailure>();
        }

        /// <summary>
        /// Persist only <c>enableToast</c> into the JSON config file, preserving other keys.
        /// The void wrapper remains for old ribbon callers; new callers should inspect the
        /// boolean and error returned by <see cref="TrySaveEnableToast"/>.
        /// </summary>
        public static void SaveEnableToast(bool enabled, string configFilePath = null)
        {
            string ignoredError;
            TrySaveEnableToast(enabled, out ignoredError, configFilePath);
        }

        public static bool TrySaveEnableToast(bool enabled, out string error, string configFilePath = null)
        {
            return TryUpdateConfig(configFilePath, root => root["enableToast"] = enabled, out error);
        }

        public static bool TrySaveToastIdleSeconds(int seconds, out string error, string configFilePath = null)
        {
            var normalized = NormalizeToastIdleSeconds(seconds);
            return TryUpdateConfig(configFilePath, root => root["toastIdleSeconds"] = normalized, out error);
        }

        /// <summary>
        /// Persist only <c>uiLanguage</c>. The legacy void wrapper is retained for the
        /// ribbon's best-effort behavior; Settings can surface the returned error.
        /// </summary>
        public static void SaveUiLanguage(string code, string configFilePath = null)
        {
            string ignoredError;
            TrySaveUiLanguage(code, out ignoredError, configFilePath);
        }

        public static bool TrySaveUiLanguage(string code, out string error, string configFilePath = null)
        {
            return TryUpdateConfig(configFilePath, root => root["uiLanguage"] = code, out error);
        }

        /// <summary>
        /// Apply the Settings dialog's staged values one key at a time. Each operation
        /// rereads the file immediately before writing, so an external edit made while
        /// the dialog was open is merged and the staged key wins. A failed key does not
        /// roll back a key that already succeeded.
        /// </summary>
        public static ApplyResult TryApplySettings(SettingsPatch patch, string configFilePath = null)
        {
            var result = new ApplyResult();
            if (patch == null)
            {
                result.Failed.Add(new ApplyFailure("settings", "Settings patch is required."));
                return result;
            }

            var toastIdleApplied = false;
            if (patch.ToastIdleSeconds.HasValue)
            {
                string error;
                if (TrySaveToastIdleSeconds(patch.ToastIdleSeconds.Value, out error, configFilePath))
                {
                    result.Applied.Add("toastIdleSeconds");
                    toastIdleApplied = true;
                }
                else
                {
                    result.Failed.Add(new ApplyFailure("toastIdleSeconds", error));
                }
            }

            if (patch.CacheSendCodeBodies.HasValue)
            {
                string error;
                var ok = TryUpdateConfig(configFilePath, root =>
                {
                    // If another process edits the file between the per-key writes,
                    // restage the first key so every staged value still wins.
                    if (toastIdleApplied)
                        root["toastIdleSeconds"] = NormalizeToastIdleSeconds(patch.ToastIdleSeconds);

                    root["cacheSendCodeBodies"] = patch.CacheSendCodeBodies.Value;
                }, out error);

                if (ok) result.Applied.Add("cacheSendCodeBodies");
                else result.Failed.Add(new ApplyFailure("cacheSendCodeBodies", error));
            }

            return result;
        }

        /// <summary>
        /// Read the existing config root for a single-key update. Returns false when the
        /// file exists but cannot be read or parsed — a torn read (e.g. another Revit
        /// instance mid-write) must never be treated as an empty config, or the write
        /// would silently drop readOnly and every other setting.
        /// </summary>
        private static bool TryReadConfigRoot(string path, out JObject root)
        {
            root = new JObject();
            if (!File.Exists(path)) return true;
            try
            {
                using (var textReader = new StringReader(File.ReadAllText(path)))
                using (var jsonReader = new JsonTextReader(textReader)
                {
                    // Keep date-like strings as written instead of re-serialising them with a local offset.
                    DateParseHandling = DateParseHandling.None,
                })
                {
                    var token = JToken.ReadFrom(jsonReader);
                    root = token as JObject;
                    if (root == null)
                    {
                        root = null;
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                root = null;
                return false;
            }
        }

        private static readonly object ConfigWriteGate = new object();

        // Settings and the toast thread (drag release) can both save: serialize the
        // read-modify-write so one cannot overwrite the other's key.
        private static bool TryUpdateConfig(string configFilePath, Action<JObject> update, out string error)
        {
            lock (ConfigWriteGate)
                return TryUpdateConfigCore(configFilePath, update, out error);
        }

        private static bool TryUpdateConfigCore(string configFilePath, Action<JObject> update, out string error)
        {
            error = null;
            string tempPath = null;
            try
            {
                var path = ResolveConfigPath(configFilePath, out var explicitPath);
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) || !IsAllowedConfigDirectory(directory, explicitPath))
                {
                    error = "The configuration path is outside the current profile.";
                    return false;
                }

                JObject root;
                if (!TryReadConfigRoot(path, out root))
                {
                    error = "The configuration file is not valid JSON.";
                    return false;
                }

                update(root);
                Directory.CreateDirectory(directory);
                var json = root.ToString(Formatting.Indented) + Environment.NewLine;
                tempPath = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                var bytes = Encoding.UTF8.GetBytes(json);
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                else
                    File.Move(tempPath, path);
                tempPath = null;
                return true;
            }
            catch (Exception ex)
            {
                // The caller logs the diagnostic; UI surfaces a localized generic error.
                error = ex.Message;
                return false;
            }
            finally
            {
                if (tempPath != null)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { }
                }
            }
        }

        private static string ResolveConfigPath(string configFilePath, out bool explicitPath)
        {
            explicitPath = !string.IsNullOrWhiteSpace(configFilePath) || !string.IsNullOrWhiteSpace(ConfigFilePathOverride);
            var selected = string.IsNullOrWhiteSpace(configFilePath)
                ? (ConfigFilePathOverride ?? DefaultConfigFilePath)
                : configFilePath;
            return Path.GetFullPath(selected);
        }

        private static bool IsAllowedConfigDirectory(string directory, bool explicitPath)
        {
            var profileRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "rvt-mcp");
            if (IsPathWithin(directory, profileRoot))
                return true;

            // Explicit temp paths are the supported test seam. Production calls use the
            // default profile path; no arbitrary current-working-directory writes.
            return explicitPath && IsPathWithin(directory, Path.GetTempPath());
        }

        private static bool IsPathWithin(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
                return false;
            var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
                return true;
            return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullCandidate.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

    }
}

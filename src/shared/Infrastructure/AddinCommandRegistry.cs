using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    /// <summary>Result of an add-in registry call: Data on success, an "&lt;code&gt;: &lt;message&gt;" Error otherwise.</summary>
    public sealed class AddinCommandOutcome
    {
        public bool Success { get; private set; }
        public JObject Data { get; private set; }
        public string Error { get; private set; }

        public static AddinCommandOutcome Ok(JObject data) => new AddinCommandOutcome { Success = true, Data = data };

        public static AddinCommandOutcome Fail(string error) => new AddinCommandOutcome { Success = false, Error = error };
    }

    /// <summary>
    /// Commands of other add-ins in the same Revit process (list_addin_commands / call_addin_command).
    /// An add-in registers without referencing this plugin: under the AppDomain data key <see cref="Key"/> it puts a
    /// <c>ConcurrentDictionary&lt;string, Func&lt;string, string&gt;&gt;</c> - add-in name → function taking a command
    /// envelope JSON and returning the response envelope JSON (protocol 1; the reserved command "_catalog" lists its
    /// commands). BCL types only, so it works across Revit 2026+ add-in load contexts. The key is read on every call,
    /// never cached, so the order the two add-ins load in does not matter. Runs on the Revit thread (ExternalEvent).
    /// </summary>
    public static class AddinCommandRegistry
    {
        public const string Key = "Bimwright.RvtMcp.AddinCommands.v1";
        public const string Protocol = "1";
        public const string CatalogCommand = "_catalog";

        public static AddinCommandOutcome List()
        {
            var addins = new JArray();
            var unavailable = new JArray();
            foreach (var pair in Registered().OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var catalog = ReadCatalog(pair.Key, pair.Value, out var error);
                if (catalog != null)
                    addins.Add(catalog);
                else
                    unavailable.Add(new JObject { ["addin"] = pair.Key, ["error"] = error });
            }

            var data = new JObject { ["addins"] = addins };
            if (unavailable.Count > 0) data["unavailable"] = unavailable;
            return AddinCommandOutcome.Ok(data);
        }

        /// <param name="paramsJson">{ addin, command, args?, entry? ("mcp" | "cli"), read_only? }</param>
        public static AddinCommandOutcome Call(string paramsJson)
        {
            JObject parameters;
            try { parameters = JObject.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson); }
            catch (JsonException ex) { return AddinCommandOutcome.Fail("invalid_params: " + ex.Message); }

            var addin = parameters.Value<string>("addin");
            var command = parameters.Value<string>("command");
            if (string.IsNullOrWhiteSpace(addin) || string.IsNullOrWhiteSpace(command))
                return AddinCommandOutcome.Fail("invalid_params: 'addin' and 'command' are required.");
            var args = parameters["args"] as JObject ?? new JObject();
            var entry = parameters.Value<string>("entry") == "cli" ? "cli" : "mcp";
            var readOnly = parameters.Value<bool?>("read_only") ?? false;

            var registered = Registered();
            if (!registered.TryGetValue(addin, out var function))
            {
                var names = registered.Count == 0 ? "(none)" : string.Join(", ", registered.Keys.OrderBy(n => n, StringComparer.Ordinal));
                return AddinCommandOutcome.Fail(
                    $"addin_not_loaded: Add-in '{addin}' has not registered commands in this Revit session. Registered add-ins: {names}. " +
                    "Check the add-in is installed for this Revit year, or call revit_list_addin_commands.");
            }

            var catalog = ReadCatalog(addin, function, out var catalogError);
            if (catalog == null)
                return AddinCommandOutcome.Fail(catalogError);

            if (readOnly && command != CatalogCommand)
            {
                var declared = (catalog["commands"] as JArray)?.OfType<JObject>()
                    .FirstOrDefault(c => string.Equals(c.Value<string>("name"), command, StringComparison.Ordinal));
                if (declared != null && declared.Value<bool?>("readOnly") != true)
                    return AddinCommandOutcome.Fail(
                        $"read_only_blocked: '{addin}' command '{command}' can change the model and this session is read-only.");
            }

            var envelope = new JObject { ["command"] = command, ["args"] = args, ["entry"] = entry };
            return Invoke(addin, function, envelope.ToString(Formatting.None));
        }

        private static IReadOnlyDictionary<string, Func<string, string>> Registered()
        {
            return AppDomain.CurrentDomain.GetData(Key) as ConcurrentDictionary<string, Func<string, string>>
                   ?? new ConcurrentDictionary<string, Func<string, string>>();
        }

        // The add-in's _catalog result, or null with the reason in error.
        private static JObject ReadCatalog(string addin, Func<string, string> function, out string error)
        {
            var outcome = Invoke(addin, function, "{\"command\":\"" + CatalogCommand + "\",\"entry\":\"mcp\"}");
            if (!outcome.Success)
            {
                error = outcome.Error;
                return null;
            }

            var catalog = outcome.Data.Value<bool?>("ok") == true ? outcome.Data["result"] as JObject : null;
            if (catalog == null)
            {
                error = $"addin_invalid_response: '{addin}' did not answer _catalog with a catalog.";
                return null;
            }

            var protocol = catalog.Value<string>("protocol");
            if (protocol != Protocol)
            {
                error = $"addin_protocol_mismatch: '{addin}' speaks add-in protocol '{protocol ?? "(none)"}', this rvt-mcp speaks '{Protocol}'. " +
                        "Update the add-in or rvt-mcp so both use the same protocol.";
                return null;
            }

            error = null;
            return catalog;
        }

        private static AddinCommandOutcome Invoke(string addin, Func<string, string> function, string envelope)
        {
            string response;
            try
            {
                response = function(envelope);
            }
            catch (Exception ex)
            {
                var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                return AddinCommandOutcome.Fail(ErrorSanitizer.Sanitize($"addin_failed: '{addin}' threw {inner.GetType().Name}: {inner.Message}"));
            }

            try
            {
                return AddinCommandOutcome.Ok(JObject.Parse(response ?? string.Empty));
            }
            catch (JsonException)
            {
                return AddinCommandOutcome.Fail($"addin_invalid_response: '{addin}' did not answer with a JSON envelope.");
            }
        }
    }
}

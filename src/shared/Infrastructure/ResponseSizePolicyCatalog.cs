#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Agent-facing recovery guidance for commands whose inline response can be large.
    /// Parameter names are MCP-facing names from Program.cs, not generic guesses.
    /// </summary>
    public static class ResponseSizePolicyCatalog
    {
        private const string FallbackHint =
            "Retry with a smaller explicit ID list or a more selective filter supported by this tool.";

        private static readonly Dictionary<string, string> NarrowingHints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["export_pdf"] = "Retry with fewer `viewIds`.",
            ["list_sheets"] = "Retry with `numberFilter`, `namePattern`, fewer include flags, and a smaller `limit`.",
        };

        public static string GetNarrowingHint(string? commandName)
        {
            if (commandName == null || commandName.Trim().Length == 0)
                return FallbackHint;

            return NarrowingHints.TryGetValue(commandName, out var hint)
                ? hint
                : FallbackHint;
        }

        public static bool IsMutationOutcomeIndeterminate(string? commandName)
        {
            return string.Equals(commandName, "send_code_to_revit", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldPreserveSuccessfulMutation(
            string? commandName,
            string? paramsJson,
            bool classifiedAsWrite)
        {
            return classifiedAsWrite || HasUiSideEffect(commandName, paramsJson);
        }

        public static bool HasUiSideEffect(string? commandName, string? paramsJson)
        {
            return string.Equals(commandName, "activate_view", StringComparison.OrdinalIgnoreCase);
        }
    }
}

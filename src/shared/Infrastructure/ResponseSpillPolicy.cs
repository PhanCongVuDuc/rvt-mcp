#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public sealed class ResponseSpillDecision
    {
        public bool ShouldSpill { get; set; }
        public bool Automatic { get; set; }
        public ResponseSpillFormat Format { get; set; }
    }

    /// <summary>
    /// Spill policy: only oversized arbitrary-code (send_code) output goes to a local file.
    /// All other commands retain the inline response-size policy.
    /// </summary>
    public static class ResponseSpillPolicy
    {
        public static ResponseSpillDecision Evaluate(string? commandName, string? paramsJson, int compactResponseBytes,
            int budgetBytes = ResponseSizeGuard.EnforcementBudgetBytes, bool automaticSpill = true)
        {
            var command = commandName ?? string.Empty;
            if (string.Equals(command, "send_code_to_revit", StringComparison.OrdinalIgnoreCase)
                && automaticSpill && compactResponseBytes > budgetBytes)
            {
                return new ResponseSpillDecision
                {
                    ShouldSpill = true,
                    Automatic = true,
                    Format = ResponseSpillFormat.Auto
                };
            }

            return new ResponseSpillDecision
            {
                ShouldSpill = false,
                Automatic = false,
                Format = ResponseSpillFormat.Auto
            };
        }

        public static object? SelectPayload(string? commandName, object? data)
        {
            if (!string.Equals(commandName, "send_code_to_revit", StringComparison.OrdinalIgnoreCase))
            {
                return data;
            }

            var token = data as JToken;
            if (token == null && data != null)
                token = JToken.FromObject(data);
            return token is JObject obj && obj.TryGetValue("result", out var output)
                ? output
                : data;
        }

    }
}

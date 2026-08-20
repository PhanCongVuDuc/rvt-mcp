#nullable enable
using System.Text;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Size policy for MCP wire responses. Warn at 100 KB; reject above 1 MiB
    /// so a clash/export dump cannot blow the client context or the pipe.
    /// </summary>
    public static class ResponseSizeGuard
    {
        public const int DefaultThresholdBytes = 100 * 1024; // 100 KB warn
        public const int MaxResponseBytes = 1024 * 1024; // 1 MiB hard cap

        public sealed class Decision
        {
            public int ByteCount { get; set; }
            public string? Warning { get; set; }
            public bool Reject { get; set; }
            public string? RejectError { get; set; }
        }

        public static string? CheckResponse(
            string commandName,
            string serializedPayload,
            int topLevelKeyCount,
            int thresholdBytes = DefaultThresholdBytes)
        {
            return Evaluate(commandName, serializedPayload, topLevelKeyCount, thresholdBytes, MaxResponseBytes).Warning;
        }

        public static Decision Evaluate(
            string commandName,
            string serializedPayload,
            int topLevelKeyCount,
            int warnBytes = DefaultThresholdBytes,
            int maxBytes = MaxResponseBytes)
        {
            var byteCount = Encoding.UTF8.GetByteCount(serializedPayload ?? string.Empty);
            var decision = new Decision { ByteCount = byteCount };

            if (byteCount > maxBytes)
            {
                decision.Reject = true;
                decision.RejectError =
                    $"Response exceeded {maxBytes} bytes ({byteCount}) for command={commandName} " +
                    $"top_level_keys={topLevelKeyCount}. Narrow the query (filters, ids, max_results, pagination). " +
                    "Do not retry the same unscoped request.";
                return decision;
            }

            if (byteCount > warnBytes)
            {
                decision.Warning =
                    $"[S4] Oversized response: command={commandName} bytes={byteCount} " +
                    $"top_level_keys={topLevelKeyCount} threshold={warnBytes}";
            }

            return decision;
        }
    }
}

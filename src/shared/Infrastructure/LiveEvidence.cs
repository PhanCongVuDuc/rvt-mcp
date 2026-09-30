using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RvtMcp.Plugin
{
    /// <summary>Which build a snippet ran: the <c>live</c> field beside <c>result</c> in send_code's answer.</summary>
    public sealed class LiveEvidence
    {
        [JsonProperty("entry")]
        public string Entry { get; private set; }

        [JsonProperty("source")]
        public string Source { get; private set; }

        [JsonProperty("directory")]
        public string Directory { get; private set; }

        [JsonProperty("builtAt")]
        public string BuiltAt { get; private set; }

        public static LiveEvidence From(EntryResolution resolution, LiveEntry entry)
        {
            var source = "startup";
            if (resolution.Kind == EntryResolutionKind.Stage) source = "stage";

            string builtAt = null;
            if (resolution.BuiltAt.HasValue) builtAt = new DateTimeOffset(resolution.BuiltAt.Value).ToString("yyyy-MM-ddTHH:mm:sszzz");

            return new LiveEvidence
            {
                Entry = entry.Value,
                Source = source,
                Directory = resolution.Directory,
                BuiltAt = builtAt,
            };
        }

        /// <summary>
        /// Evidence of the first entry the snippet used, or null when it used none - the result then keeps the
        /// shape it had before live entries existed.
        /// </summary>
        public static LiveEvidence FromUsedOrNull(IReadOnlyList<EntryResolution> used)
        {
            var ran = used.FirstOrDefault(r => r.Kind != EntryResolutionKind.Failed);
            if (ran == null) return null;
            return From(ran, ran.Entry);
        }
    }
}

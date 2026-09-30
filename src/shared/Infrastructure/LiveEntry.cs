using System;
using System.Collections.Generic;
using System.Linq;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// One value of <c>liveEntries</c> in rvtmcp.config.json: <c>&lt;namespace&gt;.&lt;Class&gt;.&lt;Method&gt;</c>, a static
    /// method of an add-in that takes a DLL file name and returns the assembly of that add-in's freshest build.
    /// </summary>
    public sealed class LiveEntry
    {
        public string Value { get; private set; }
        public string ClassName { get; private set; }
        public string MethodName { get; private set; }
        public string NamespaceRoot { get; private set; }

        /// <summary>
        /// The first two segments of <see cref="NamespaceRoot"/>. The namespace itself ("...Standalone.LiveReload")
        /// matches no assembly name, while the add-in's assemblies ("Revgen.StairDesign.Domain") all start with this.
        /// </summary>
        public string NamespacePrefix { get; private set; }

        public static IReadOnlyList<LiveEntry> ParseAll(IEnumerable<string> values)
        {
            var entries = new List<LiveEntry>();
            if (values == null) return entries;

            foreach (var raw in values)
            {
                var entry = ParseOrNull(raw);
                if (entry == null) continue;
                if (entries.Any(e => e.Value == entry.Value)) continue;
                entries.Add(entry);
            }

            return entries;
        }

        private static LiveEntry ParseOrNull(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var value = raw.Trim();
            var segments = value.Split('.');
            if (segments.Length < 3 || segments.Any(s => s.Length == 0)) return null;

            var namespaceSegments = segments.Take(segments.Length - 2).ToArray();

            return new LiveEntry
            {
                Value = value,
                ClassName = string.Join(".", segments.Take(segments.Length - 1)),
                MethodName = segments[segments.Length - 1],
                NamespaceRoot = string.Join(".", namespaceSegments),
                NamespacePrefix = string.Join(".", namespaceSegments.Take(2)),
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Stops a snippet loading an add-in assembly by hand or picking it out of the AppDomain.
    ///
    /// send_code already binds a snippet's references to the build the add-in's live entry reports. A copy the
    /// snippet fetches itself is the startup build (or a third copy of its types), so it runs old code silently, and
    /// its WPF windows die outright: XAML resolves by assembly name, so code and markup come from different builds.
    ///
    /// Only add-ins with a live entry are guarded (<c>liveEntries</c> in rvtmcp.config.json), and only when the
    /// snippet names one of their assemblies in a string - a snippet that merely lists what is loaded is left alone.
    /// A snippet that means it says so with <c>mcp:allow-stale-assembly</c>.
    /// </summary>
    public static class SendCodeStaleAssemblyGuard
    {
        public const string Override = "mcp:allow-stale-assembly";

        private static readonly Regex Reach = new Regex(
            @"AppDomain\s*\.\s*CurrentDomain\s*\.\s*GetAssemblies|Assembly\s*\.\s*(LoadFrom|LoadFile|Load)\s*\(",
            RegexOptions.Compiled);

        /// <summary>
        /// The refusal to show the caller, or null when the snippet is fine. <paramref name="owners"/> are the
        /// namespace prefixes of the configured live entries.
        /// </summary>
        public static string RefusalOrNull(string code, IEnumerable<string> owners)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (code.IndexOf(Override, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (!Reach.IsMatch(code)) return null;

            var owner = owners
                .Where(prefix => NamesAssemblyOf(code, prefix))
                .OrderByDescending(prefix => prefix.Length)
                .FirstOrDefault();

            if (owner == null) return null;

            return
                "This snippet loads or picks out a '" + owner + "' assembly itself. send_code already binds the snippet to the\n" +
                "build that add-in's live entry reports; a copy fetched by hand is the build Revit opened with, so the snippet\n" +
                "would run old code, and any WPF window it opens would fail on XAML from a different build.\n\n" +
                "Write the add-in's types directly - they resolve to the fresh build. If you need the Assembly object,\n" +
                "'live' is in scope:\n\n" +
                "    var asm = live(\"" + owner + ".dll\");\n\n" +
                "If you really do mean the loaded copy, put " + Override + " in a comment in the snippet.";
        }

        /// <summary>Whether a string in the snippet names an assembly under <paramref name="prefix"/>, bare or inside a path.</summary>
        private static bool NamesAssemblyOf(string code, string prefix)
        {
            var quoted = Regex.Matches(code, "\"([^\"\n]*)\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value);

            return quoted.Any(value => value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}

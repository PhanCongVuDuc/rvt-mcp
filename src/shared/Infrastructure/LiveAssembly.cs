using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// The <c>live("X.dll")</c> helper in a snippet: an add-in assembly by name, from the build the add-in's live
    /// entry reports when one is configured (<c>liveEntries</c> in rvtmcp.config.json).
    ///
    /// A snippet rarely needs it - types it names directly are already bound to that build. It is for code that
    /// needs the Assembly object itself, such as reading an embedded resource.
    /// </summary>
    public static class LiveAssembly
    {
        public static Assembly Of(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException("fileName");

            var name = Path.GetFileNameWithoutExtension(fileName);

            foreach (var entry in LiveEntry.ParseAll(RvtMcpConfig.LiveEntriesFromFile()))
            {
                var resolution = LiveEntryResolver.Resolve(entry, AppDomain.CurrentDomain.GetAssemblies());

                Assembly staged;
                if (resolution.Assemblies.TryGetValue(name, out staged)) return staged;

                var claims = name.StartsWith(entry.NamespacePrefix, StringComparison.OrdinalIgnoreCase);
                if (claims && resolution.Kind == EntryResolutionKind.Failed) throw new InvalidOperationException(resolution.Message);
            }

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => !a.IsDynamic && string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

            if (loaded != null) return loaded;
            throw new InvalidOperationException("No assembly named '" + name + "' is loaded in Revit.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RvtMcp.Plugin
{
    public sealed class LiveCompileResult
    {
        public bool Success { get; internal set; }
        public byte[] Image { get; internal set; }
        public IReadOnlyList<string> Errors { get; internal set; }

        /// <summary>The live entries whose assemblies the compiled snippet references.</summary>
        public IReadOnlyList<EntryResolution> Used { get; internal set; }

        /// <summary>Name → staged instance, for every staged assembly of the entries the snippet uses.</summary>
        public IReadOnlyDictionary<string, Assembly> Stage { get; internal set; }

        public string RefusalOrNull { get; internal set; }
    }

    /// <summary>
    /// Compiles a snippet against the build each live entry reports as current, and works out which entries the
    /// compiled code actually uses.
    /// </summary>
    public static class LiveSnippetCompiler
    {
        public static LiveCompileResult Compile(string source, IEnumerable<Assembly> loaded, IReadOnlyList<EntryResolution> resolutions)
        {
            var staged = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var resolution in resolutions.Where(r => r.Kind == EntryResolutionKind.Stage))
            {
                foreach (var pair in resolution.Assemblies) staged[pair.Key] = pair.Value;
            }

            // Same version under one name: the first loaded wins, which is the startup copy - hence the staged
            // override. File.Exists guards stale add-ins whose dll was deleted after Revit loaded them.
            var chosen = loaded
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && File.Exists(a.Location))
                .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => StagedOrNull(staged, g.Key) ?? g.OrderByDescending(a => a.GetName().Version).First())
                .ToList();

            var nameOf = new Dictionary<MetadataReference, string>();
            foreach (var assembly in chosen)
            {
                nameOf[MetadataReference.CreateFromFile(assembly.Location)] = assembly.GetName().Name;
            }

            var compilation = CSharpCompilation.Create(
                "McpDynamic_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                new[] { CSharpSyntaxTree.ParseText(source) },
                nameOf.Keys,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var usedNames = compilation.GetUsedAssemblyReferences()
                .Where(nameOf.ContainsKey)
                .Select(r => nameOf[r])
                .ToList();

            var used = resolutions.Where(r => Uses(r, usedNames)).ToList();

            using (var image = new MemoryStream())
            {
                var emitted = compilation.Emit(image);

                var errors = emitted.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString())
                    .Take(5)
                    .ToList();

                return new LiveCompileResult
                {
                    Success = emitted.Success,
                    Image = emitted.Success ? image.ToArray() : null,
                    Errors = errors,
                    Used = used,
                    Stage = StageOf(used),
                    RefusalOrNull = RefusalOrNull(used, resolutions, usedNames),
                };
            }
        }

        /// <summary>
        /// A failed entry refuses the snippet only when no other entry of the same prefix was reached - a leftover
        /// entry in the machine-wide config must not break a snippet the right entry serves.
        /// </summary>
        private static string RefusalOrNull(IReadOnlyList<EntryResolution> used, IReadOnlyList<EntryResolution> all, IList<string> usedNames)
        {
            foreach (var failed in used.Where(r => r.Kind == EntryResolutionKind.Failed))
            {
                var prefix = failed.Entry.NamespacePrefix;
                var reached = all.Any(r => r.Kind != EntryResolutionKind.Failed
                                           && string.Equals(r.Entry.NamespacePrefix, prefix, StringComparison.OrdinalIgnoreCase));
                if (reached) continue;

                var assembly = usedNames.First(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return "Snippet uses '" + assembly + "', which live entry '" + failed.Entry.Value + "' serves. " + failed.Message + "\n" +
                       "The snippet was not run, so it could not silently run the build Revit started with.";
            }

            return null;
        }

        private static bool Uses(EntryResolution resolution, IEnumerable<string> usedNames)
        {
            if (resolution.Kind == EntryResolutionKind.Stage)
            {
                return usedNames.Any(name => resolution.Assemblies.ContainsKey(name));
            }

            // Without a stage there is no list of served names; the entry's namespace prefix is the best claim.
            return usedNames.Any(name => name.StartsWith(resolution.Entry.NamespacePrefix, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyDictionary<string, Assembly> StageOf(IEnumerable<EntryResolution> used)
        {
            var stage = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var resolution in used.Where(r => r.Kind == EntryResolutionKind.Stage))
            {
                foreach (var pair in resolution.Assemblies) stage[pair.Key] = pair.Value;
            }
            return stage;
        }

        private static Assembly StagedOrNull(Dictionary<string, Assembly> staged, string name)
        {
            Assembly assembly;
            return staged.TryGetValue(name, out assembly) ? assembly : null;
        }
    }
}

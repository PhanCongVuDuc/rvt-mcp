using System;
using System.Collections.Generic;
using System.Reflection;
#if NETFRAMEWORK
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
#else
using System.IO;
using System.Runtime.Loader;
#endif

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Loads a compiled snippet so the assemblies an add-in staged resolve to the staged instances, not to the
    /// copies Revit loaded at startup. The snippet then shares types with windows the add-in opened from that stage.
    /// </summary>
    public static class SnippetBinding
    {
#if NETFRAMEWORK
        private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, Assembly>> Bindings =
            new ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, Assembly>>();

        private static readonly ResolveEventHandler Handler = OnAssemblyResolve;

        public static Assembly Load(byte[] image, IReadOnlyDictionary<string, Assembly> stage)
        {
            // Not Assembly.Load(byte[]): its references bind through the load context's cache, so once any snippet
            // in the process has resolved a name (measured on Revit 2024: the startup Revgen.StairDesign.Domain),
            // every later snippet gets that instance and AssemblyResolve never fires - a rebuilt stage is ignored.
            // A LoadFile assembly asks AssemblyResolve for each of its own references, as the add-in's staged
            // copies do.
            var snippet = Assembly.LoadFile(WriteImage(image));
            // Kept for the life of the process: a snippet's code can run long after send_code returns (event
            // handlers, callbacks), and each of its first-time type loads comes back here.
            Bindings[snippet] = new Dictionary<string, Assembly>(stage.ToDictionary(p => p.Key, p => p.Value), StringComparer.OrdinalIgnoreCase);
            PutHandlerFirst();
            return snippet;
        }

        private static string WriteImage(byte[] image)
        {
            var directory = Path.Combine(Path.GetTempPath(), "RvtMcp", "snippets", Process.GetCurrentProcess().Id.ToString());
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "McpSnippet_" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(path, image);
            return path;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            IReadOnlyDictionary<string, Assembly> stage;
            if (args.RequestingAssembly == null || !Bindings.TryGetValue(args.RequestingAssembly, out stage)) return null;

            Assembly staged;
            return stage.TryGetValue(new AssemblyName(args.Name).Name, out staged) ? staged : null;
        }

        /// <summary>
        /// AssemblyResolve stops at the first handler that answers, and an add-in that loads from Addins registers
        /// one answering its own names with the startup copy - so appending ours would never run. The event has no
        /// API for order; the backing delegate is rewritten with ours at the head, as the add-in's own loader does.
        /// </summary>
        private static void PutHandlerFirst()
        {
            var domain = AppDomain.CurrentDomain;
            var field = typeof(AppDomain).GetField("_AssemblyResolve", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                domain.AssemblyResolve -= Handler;
                domain.AssemblyResolve += Handler;
                return;
            }

            var current = field.GetValue(domain) as ResolveEventHandler;
            var others = current == null
                ? new Delegate[0]
                : current.GetInvocationList().Where(d => !d.Equals(Handler)).ToArray();
            field.SetValue(domain, Delegate.Combine(new Delegate[] { Handler }.Concat(others).ToArray()));
        }
#else
        public static Assembly Load(byte[] image, IReadOnlyDictionary<string, Assembly> stage)
        {
            var context = new SnippetLoadContext(stage);
            using (var stream = new MemoryStream(image))
            {
                return context.LoadFromStream(stream);
            }
        }

        /// <summary>One per snippet. A name the add-in staged answers with that instance; any other falls through to the default context, as before.</summary>
        private sealed class SnippetLoadContext : AssemblyLoadContext
        {
            private readonly Dictionary<string, Assembly> _stage;

            public SnippetLoadContext(IReadOnlyDictionary<string, Assembly> stage)
                : base("McpSnippet_" + Guid.NewGuid().ToString("N"))
            {
                _stage = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in stage) _stage[pair.Key] = pair.Value;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                Assembly staged;
                return _stage.TryGetValue(assemblyName.Name, out staged) ? staged : null;
            }
        }
#endif
    }
}

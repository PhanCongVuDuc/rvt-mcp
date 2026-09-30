using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RvtMcp.Tests.Helpers
{
    /// <summary>
    /// A tiny add-in with a live loader, compiled on the fly, so the live-entry code can be exercised the way
    /// Revit holds a real one: a startup copy and a staged copy of the same assembly name, each in its own load
    /// context. Every call gets a fresh assembly name, because a test process keeps what it loads.
    /// </summary>
    internal static class FakeAddin
    {
        public const string Prefix = "Fake.Addin";
        public const string LoaderClass = "Fake.Addin.Live.Loader";
        public const string Entry = LoaderClass + ".Entry";

        public const string LoaderSource =
            "namespace Fake.Addin.Live { public static class Loader {" +
            " public static System.Func<string, System.Reflection.Assembly> Answer;" +
            " private static System.Reflection.Assembly Entry(string fileName) { return Answer(fileName); } } }";

        public const string ThingSource = "namespace Fake.Addin { public class Thing { } }";

        public const string ThingWithOnlySource =
            "namespace Fake.Addin { public class Thing { public static int Only { get { return 7; } } } }";

        public const string WindowSource =
            "namespace Fake.Addin { public class Zone { }" +
            " public class Window { public System.Collections.Generic.List<Zone> Zones =" +
            " new System.Collections.Generic.List<Zone> { new Zone() }; } }";

        public static string UniqueName()
        {
            return Prefix + Guid.NewGuid().ToString("N");
        }

        public static string NewDirectory()
        {
            return Directory.CreateTempSubdirectory("rvtmcp-fake-addin-").FullName;
        }

        /// <summary>Compiles <paramref name="sources"/> into &lt;directory&gt;\&lt;assemblyName&gt;.dll and returns the path.</summary>
        public static string Build(string directory, string assemblyName, params string[] sources)
        {
            var compilation = CSharpCompilation.Create(assemblyName,
                sources.Select(s => CSharpSyntaxTree.ParseText(s)), PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var path = Path.Combine(directory, assemblyName + ".dll");
            var result = compilation.Emit(path);
            if (!result.Success)
                throw new InvalidOperationException("fake add-in did not compile: " + string.Join("; ", result.Diagnostics));
            return path;
        }

        public static Assembly LoadIsolated(string path)
        {
            return new AssemblyLoadContext("fake-addin-" + Guid.NewGuid().ToString("N")).LoadFromAssemblyPath(path);
        }

        /// <summary>
        /// The test process's assemblies with every other test's fake add-in left out - they all declare
        /// Fake.Addin.Thing, which a snippet compiled against all of them would find ambiguous. Revit holds one add-in.
        /// </summary>
        public static IEnumerable<Assembly> DomainWith(params Assembly[] mine)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.GetName().Name.StartsWith(Prefix, StringComparison.Ordinal))
                .Concat(mine);
        }

        public static void Answer(Assembly addin, Func<string, Assembly> answer)
        {
            addin.GetType(LoaderClass).GetField("Answer").SetValue(null, answer);
        }

        public static MetadataReference[] PlatformReferences()
        {
            return Platform.Value;
        }

        // The fake add-in needs object, Func, Assembly and List only; binding all ~170 framework files per
        // compile made each test take seconds.
        private static readonly Lazy<MetadataReference[]> Platform = new Lazy<MetadataReference[]>(() =>
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(p => PlatformNames.Contains(Path.GetFileNameWithoutExtension(p)))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToArray());

        private static readonly string[] PlatformNames = { "System.Private.CoreLib", "System.Runtime", "System.Collections" };
    }
}

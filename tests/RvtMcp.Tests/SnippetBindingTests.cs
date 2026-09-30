using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using RvtMcp.Plugin;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class SnippetBindingTests
    {
        private static readonly LiveEntry Entry = LiveEntry.ParseAll(new[] { FakeAddin.Entry }).Single();

        private const string ReturnsAddinAssembly =
            "public static class S { public static object Run(object window) { return typeof(Fake.Addin.Thing).Assembly; } }";

        [Fact]
        public void Load_SnippetCompiledAgainstStage_BindsToTheStageInstance()
        {
            var addin = StartupInDefaultContext.Arrange();

            var ran = RunBound(ReturnsAddinAssembly, addin, null);

            Assert.True(ReferenceEquals(ran, addin.Stage),
                "the default context holds the startup copy, so an unbound snippet would get the build Revit started with");
        }

        private const string CastsWindowZoneAndAddsOne =
            "public static class S { public static object Run(object window) {" +
            " var w = (Fake.Addin.Window)window; var first = (Fake.Addin.Zone)w.Zones[0];" +
            " w.Zones.Add(new Fake.Addin.Zone()); return w.Zones.Count; } }";

        [Fact]
        public void Load_SnippetGetsWindowFromStage_CastsAndAddsWithoutTypeMismatch()
        {
            var addin = StartupInDefaultContext.Arrange();
            var window = Activator.CreateInstance(addin.Stage.GetType("Fake.Addin.Window"));

            var ran = RunBound(CastsWindowZoneAndAddsOne, addin, window);

            Assert.True(Equals(ran, 2),
                "a window opened from the stage and the snippet must share Zone, or the cast throws (FR-002)");
        }

        internal static object RunBound(string snippet, StartupInDefaultContext addin, object window)
        {
            var resolution = EntryResolution.Stage(Entry, Path.GetDirectoryName(addin.Stage.Location),
                new Dictionary<string, Assembly> { { addin.Name, addin.Stage } }, null);
            var compiled = LiveSnippetCompiler.Compile(snippet, FakeAddin.DomainWith(addin.Startup, addin.Stage), new[] { resolution });
            if (!compiled.Success)
                throw new InvalidOperationException("snippet did not compile: " + string.Join("; ", compiled.Errors));

            var snippetAssembly = SnippetBinding.Load(compiled.Image, compiled.Stage);
            return snippetAssembly.GetType("S").GetMethod("Run").Invoke(null, new[] { window });
        }

        /// <summary>
        /// Revit 2025's shape: the add-in Revit loaded at startup in the default context, the staged build in a
        /// context of its own.
        /// </summary>
        internal sealed class StartupInDefaultContext
        {
            public string Name;
            public Assembly Startup;
            public Assembly Stage;

            public static StartupInDefaultContext Arrange()
            {
                var name = FakeAddin.UniqueName();
                var startupPath = FakeAddin.Build(FakeAddin.NewDirectory(), name, FakeAddin.LoaderSource, FakeAddin.ThingSource, FakeAddin.WindowSource);
                var startup = AssemblyLoadContext.Default.LoadFromAssemblyPath(startupPath);
                var stage = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), name, FakeAddin.LoaderSource, FakeAddin.ThingSource, FakeAddin.WindowSource));
                return new StartupInDefaultContext { Name = name, Startup = startup, Stage = stage };
            }
        }
    }
}

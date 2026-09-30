using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RvtMcp.Plugin;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class SnippetCompileTests
    {
        private static readonly LiveEntry Entry = LiveEntry.ParseAll(new[] { FakeAddin.Entry }).Single();

        private const string UsesStageOnlyMember =
            "public static class S { public static int Run() { return Fake.Addin.Thing.Only; } }";

        private const string UsesAddinType =
            "public static class S { public static object Run() { return new Fake.Addin.Thing(); } }";

        private const string NamesAddinInStringOnly =
            "public static class S { public static object Run() { return \"Fake.Addin.Thing\"; } }";

        [Fact]
        public void Compile_StageResolution_SeesMemberOnlyInStage()
        {
            var addin = TwoBuilds.Arrange();

            var result = LiveSnippetCompiler.Compile(UsesStageOnlyMember, addin.Loaded(), new[] { addin.StageResolution() });

            Assert.True(result.Success,
                "a snippet must compile against the build it will run, got: " + string.Join("; ", result.Errors));
        }

        [Fact]
        public void Compile_StartupResolution_ReportsMemberMissing()
        {
            var addin = TwoBuilds.Arrange();

            var result = LiveSnippetCompiler.Compile(UsesStageOnlyMember, addin.Loaded(), new[] { addin.StartupResolution() });

            Assert.True(!result.Success,
                "with no newer build the startup copy is the reference, and it has no 'Only'");
        }

        [Fact]
        public void Compile_SnippetUsesServedType_ListsEntryAsUsed()
        {
            var addin = TwoBuilds.Arrange();

            var result = LiveSnippetCompiler.Compile(UsesAddinType, addin.Loaded(), new[] { addin.StageResolution() });

            Assert.True(result.Used.Count == 1 && result.Used[0].Entry.Value == FakeAddin.Entry,
                "a snippet whose compiled code references a served assembly uses the entry, got " + result.Used.Count);
        }

        [Fact]
        public void Compile_SnippetNamesAddinOnlyInString_ListsNothingUsed()
        {
            var addin = TwoBuilds.Arrange();

            var result = LiveSnippetCompiler.Compile(NamesAddinInStringOnly, addin.Loaded(), new[] { addin.StageResolution() });

            Assert.True(result.Used.Count == 0,
                "a name inside a string is not a reference to the assembly, got " + result.Used.Count + " used");
        }

        /// <summary>A startup copy and a staged copy of one fake add-in; the staged one alone has Thing.Only.</summary>
        internal sealed class TwoBuilds
        {
            public string Name;
            public Assembly Startup;
            public Assembly Stage;

            public static TwoBuilds Arrange()
            {
                var name = FakeAddin.UniqueName();
                var startup = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), name, FakeAddin.LoaderSource, FakeAddin.ThingSource));
                var stage = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), name, FakeAddin.LoaderSource, FakeAddin.ThingWithOnlySource, FakeAddin.WindowSource));
                return new TwoBuilds { Name = name, Startup = startup, Stage = stage };
            }

            /// <summary>What Revit's AppDomain lists: the startup copy loaded first, the staged one after it.</summary>
            public IEnumerable<Assembly> Loaded()
            {
                return FakeAddin.DomainWith(Startup, Stage);
            }

            public EntryResolution StageResolution()
            {
                return EntryResolution.Stage(Entry, Path.GetDirectoryName(Stage.Location),
                    new Dictionary<string, Assembly> { { Name, Stage } }, null);
            }

            public EntryResolution StartupResolution()
            {
                return EntryResolution.Startup(Entry, Path.GetDirectoryName(Startup.Location), null);
            }
        }
    }
}

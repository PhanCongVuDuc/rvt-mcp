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
    public class LiveEntryResolverTests
    {
        private static readonly LiveEntry Entry = LiveEntry.ParseAll(new[] { FakeAddin.Entry }).Single();
        private static readonly DateTime BuildTime = new DateTime(2026, 9, 30, 11, 4, 26, DateTimeKind.Local);

        [Fact]
        public void Resolve_TwoCopiesLoaded_CallsTheFirstLoaded()
        {
            var name = FakeAddin.UniqueName();
            var first = LoadAnsweringItself(name);
            var second = LoadAnsweringItself(name);

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { first, second });

            Assert.True(resolution.Directory == Path.GetDirectoryName(first.Location),
                "the loader Revit loaded at startup is the one with state; a staged copy would stage again (FR-003)");
        }

        [Fact]
        public void Resolve_LoaderReturnsCopyFromAnotherDirectory_IsStage()
        {
            var arranged = ArrangeStage();

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { arranged.Startup });

            Assert.True(resolution.Kind == EntryResolutionKind.Stage,
                "an assembly outside the startup copy's directory is a staged build, got " + resolution.Kind);
        }

        [Fact]
        public void Resolve_Stage_ServesEveryDllOfTheStageDirectory()
        {
            var arranged = ArrangeStage();

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { arranged.Startup });

            Assert.True(resolution.Assemblies.Keys.OrderBy(k => k).SequenceEqual(new[] { arranged.MainName, arranged.ExtraName }.OrderBy(k => k)),
                "the stage directory is the list the loader staged, so every DLL in it is served, got " + string.Join(", ", resolution.Assemblies.Keys));
        }

        [Fact]
        public void Resolve_Stage_MapsEachNameToTheLoadedStageInstance()
        {
            var arranged = ArrangeStage();

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { arranged.Startup });

            Assert.True(ReferenceEquals(resolution.Assemblies[arranged.ExtraName], arranged.StageExtra),
                "a snippet must bind to the very instance the add-in loaded, or its types are a third copy");
        }

        [Fact]
        public void Resolve_Stage_BuiltAtIsTheMainDllWriteTime()
        {
            var arranged = ArrangeStage();
            File.SetLastWriteTime(arranged.StageMainPath, BuildTime);

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { arranged.Startup });

            Assert.True(resolution.BuiltAt == BuildTime,
                "builtAt is when the main DLL of the running build was written, got " + resolution.BuiltAt);
        }

        [Fact]
        public void Resolve_LoaderReturnsStartupCopy_IsStartup()
        {
            var startup = LoadAnsweringItself(FakeAddin.UniqueName());

            var resolution = LiveEntryResolver.Resolve(Entry, new[] { startup });

            Assert.True(resolution.Kind == EntryResolutionKind.Startup,
                "no newer build than the startup one means nothing to swap, got " + resolution.Kind);
        }

        [Theory]
        [InlineData("class-not-loaded")]
        [InlineData("method-missing")]
        [InlineData("threw")]
        [InlineData("returned-null")]
        public void Resolve_EntryCannotBeReached_FailsWithReason(string reason)
        {
            var arranged = ArrangeFailure(reason);

            var resolution = LiveEntryResolver.Resolve(arranged.Entry, arranged.Loaded);

            Assert.True(resolution.Kind == EntryResolutionKind.Failed && resolution.Reason == reason,
                "expected Failed/" + reason + ", got " + resolution.Kind + "/" + resolution.Reason);
        }

        [Theory]
        [InlineData("class-not-loaded")]
        [InlineData("method-missing")]
        [InlineData("threw")]
        [InlineData("returned-null")]
        public void Resolve_EntryCannotBeReached_MessageNamesTheEntry(string reason)
        {
            var arranged = ArrangeFailure(reason);

            var resolution = LiveEntryResolver.Resolve(arranged.Entry, arranged.Loaded);

            Assert.True(resolution.Message != null && resolution.Message.Contains(arranged.Entry.Value),
                "the user must see which live entry failed, got: " + resolution.Message);
        }

        private static Assembly LoadAnsweringItself(string name)
        {
            var addin = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), name, FakeAddin.LoaderSource, FakeAddin.ThingSource));
            FakeAddin.Answer(addin, _ => addin);
            return addin;
        }

        private sealed class StageArrangement
        {
            public Assembly Startup;
            public Assembly StageExtra;
            public string StageMainPath;
            public string MainName;
            public string ExtraName;
        }

        private static StageArrangement ArrangeStage()
        {
            var mainName = FakeAddin.UniqueName();
            var extraName = FakeAddin.UniqueName();
            var startup = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), mainName, FakeAddin.LoaderSource, FakeAddin.ThingSource));

            var stageDirectory = FakeAddin.NewDirectory();
            var stageMainPath = FakeAddin.Build(stageDirectory, mainName, FakeAddin.LoaderSource, FakeAddin.ThingWithOnlySource);
            var stageMain = FakeAddin.LoadIsolated(stageMainPath);
            var stageExtra = FakeAddin.LoadIsolated(FakeAddin.Build(stageDirectory, extraName, FakeAddin.ThingSource));
            var staged = new Dictionary<string, Assembly> { { mainName, stageMain }, { extraName, stageExtra } };
            FakeAddin.Answer(startup, fileName => staged[Path.GetFileNameWithoutExtension(fileName)]);

            return new StageArrangement
            {
                Startup = startup, StageExtra = stageExtra, StageMainPath = stageMainPath, MainName = mainName, ExtraName = extraName,
            };
        }

        private sealed class FailureArrangement
        {
            public LiveEntry Entry;
            public Assembly[] Loaded;
        }

        private static FailureArrangement ArrangeFailure(string reason)
        {
            var addin = FakeAddin.LoadIsolated(FakeAddin.Build(FakeAddin.NewDirectory(), FakeAddin.UniqueName(), FakeAddin.LoaderSource));
            var entry = Entry;
            var loaded = new[] { addin };

            switch (reason)
            {
                case "class-not-loaded":
                    loaded = new Assembly[0];
                    break;
                case "method-missing":
                    entry = LiveEntry.ParseAll(new[] { FakeAddin.LoaderClass + ".Missing" }).Single();
                    break;
                case "threw":
                    FakeAddin.Answer(addin, _ => throw new IOException("stage copy failed"));
                    break;
                case "returned-null":
                    FakeAddin.Answer(addin, _ => null);
                    break;
                default:
                    throw new InvalidOperationException("unknown failure reason " + reason);
            }

            return new FailureArrangement { Entry = entry, Loaded = loaded };
        }
    }
}

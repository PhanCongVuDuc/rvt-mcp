using System.Linq;
using RvtMcp.Plugin;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class LiveEntryFailureTests
    {
        private static readonly LiveEntry Entry = LiveEntry.ParseAll(new[] { FakeAddin.Entry }).Single();
        private static readonly LiveEntry StaleEntry = LiveEntry.ParseAll(new[] { "Fake.Addin.Old.Loader.Entry" }).Single();

        private const string FailureMessage =
            "Live entry '" + FakeAddin.Entry + "' could not be reached: no loaded assembly defines class '" + FakeAddin.LoaderClass + "'.";

        private const string UsesAddinType =
            "public static class S { public static object Run() { return new Fake.Addin.Thing(); } }";

        private const string UsesNothingOfTheAddin =
            "public static class S { public static object Run() { return 1; } }";

        [Fact]
        public void Compile_FailedEntrySnippetUsesItsPrefix_RefusalNamesTheEntry()
        {
            var addin = SnippetCompileTests.TwoBuilds.Arrange();
            var failed = EntryResolution.Failed(Entry, LiveEntryResolver.ClassNotLoaded, FailureMessage);

            var result = LiveSnippetCompiler.Compile(UsesAddinType, addin.Loaded(), new[] { failed });

            Assert.True(result.RefusalOrNull != null && result.RefusalOrNull.Contains(FakeAddin.Entry),
                "a snippet using an add-in whose entry failed must not run the startup build silently (FR-006), got: " + result.RefusalOrNull);
        }

        [Fact]
        public void Compile_FailedEntrySnippetUsesItsPrefix_RefusalGivesTheReason()
        {
            var addin = SnippetCompileTests.TwoBuilds.Arrange();
            var failed = EntryResolution.Failed(Entry, LiveEntryResolver.ClassNotLoaded, FailureMessage);

            var result = LiveSnippetCompiler.Compile(UsesAddinType, addin.Loaded(), new[] { failed });

            Assert.True(result.RefusalOrNull != null && result.RefusalOrNull.Contains(FailureMessage),
                "the refusal carries why the entry could not be reached, got: " + result.RefusalOrNull);
        }

        [Fact]
        public void Compile_FailedEntrySnippetDoesNotUseIt_CompilesWithoutRefusal()
        {
            var addin = SnippetCompileTests.TwoBuilds.Arrange();
            var failed = EntryResolution.Failed(Entry, LiveEntryResolver.ClassNotLoaded, FailureMessage);

            var result = LiveSnippetCompiler.Compile(UsesNothingOfTheAddin, addin.Loaded(), new[] { failed });

            Assert.True(result.Success && result.RefusalOrNull == null,
                "a snippet that never touches the add-in runs as before (FR-007), got: " + result.RefusalOrNull);
        }

        [Fact]
        public void Compile_StaleEntryFailsBesideReachableOne_CompilesWithoutRefusal()
        {
            var addin = SnippetCompileTests.TwoBuilds.Arrange();
            var stale = EntryResolution.Failed(StaleEntry, LiveEntryResolver.ClassNotLoaded, "Live entry 'Fake.Addin.Old.Loader.Entry' could not be reached.");

            var result = LiveSnippetCompiler.Compile(UsesAddinType, addin.Loaded(), new[] { stale, addin.StageResolution() });

            Assert.True(result.RefusalOrNull == null,
                "a leftover entry with the same prefix must not break the snippet once the right one is reached, got: " + result.RefusalOrNull);
        }
    }
}

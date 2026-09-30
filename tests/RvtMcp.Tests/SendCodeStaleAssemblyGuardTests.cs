using System.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class SendCodeStaleAssemblyGuardTests
    {
        private static readonly string[] StairOwners = LiveEntry
            .ParseAll(new[] { "Revgen.StairDesign.Standalone.LiveReload.LiveLoader.CommandAssembly" })
            .Select(e => e.NamespacePrefix)
            .ToArray();

        private const string LoadsStairDll =
            "var asm = System.Reflection.Assembly.LoadFrom(@\"D:\\repo\\bin\\DB2024\\Revgen.StairDesign.Domain.dll\");\nreturn asm.FullName;";

        private const string SearchesLoadedForStair =
            "return AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == \"Revgen.StairDesign.Domain\").Location;";

        [Theory]
        [InlineData(LoadsStairDll)]
        [InlineData(SearchesLoadedForStair)]
        public void RefusalOrNull_SnippetReachesForLiveEntryAssembly_Refuses(string code)
        {
            var refusal = SendCodeStaleAssemblyGuard.RefusalOrNull(code, StairOwners);

            Assert.True(refusal != null, "a hand-loaded Stair DLL is a third copy of its types, beside startup and stage (FR-008)");
        }

        [Fact]
        public void RefusalOrNull_SnippetReachesForLiveEntryAssembly_RefusalNamesOverrideMarker()
        {
            var refusal = SendCodeStaleAssemblyGuard.RefusalOrNull(LoadsStairDll, StairOwners);

            Assert.True(refusal != null && refusal.Contains(SendCodeStaleAssemblyGuard.Override),
                "the refusal must say how to mean it, got: " + refusal);
        }

        [Fact]
        public void RefusalOrNull_SnippetCarriesOverrideMarker_Allows()
        {
            var refusal = SendCodeStaleAssemblyGuard.RefusalOrNull("// " + SendCodeStaleAssemblyGuard.Override + "\n" + LoadsStairDll, StairOwners);

            Assert.True(refusal == null, "a snippet that says it means the loaded copy runs, got: " + refusal);
        }

        [Fact]
        public void RefusalOrNull_NoLiveEntries_Allows()
        {
            var refusal = SendCodeStaleAssemblyGuard.RefusalOrNull(LoadsStairDll, new string[0]);

            Assert.True(refusal == null, "owners come from liveEntries; with none configured nothing is guarded, got: " + refusal);
        }
    }
}

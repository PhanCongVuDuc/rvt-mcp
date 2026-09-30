using System.IO;
using System.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class LiveEntryTests
    {
        private const string StairEntry = "Revgen.StairDesign.Standalone.LiveReload.LiveLoader.CommandAssembly";

        [Fact]
        public void ParseAll_StairEntry_SplitsClassFromMethod()
        {
            var entry = LiveEntry.ParseAll(new[] { StairEntry }).Single();

            Assert.True(entry.ClassName == "Revgen.StairDesign.Standalone.LiveReload.LiveLoader",
                "the class is everything before the last dot, got '" + entry.ClassName + "'");
            Assert.True(entry.MethodName == "CommandAssembly",
                "the method is the part after the last dot, got '" + entry.MethodName + "'");
        }

        [Fact]
        public void ParseAll_StairEntry_NamespaceRootIsTheClassNamespace()
        {
            var entry = LiveEntry.ParseAll(new[] { StairEntry }).Single();

            Assert.True(entry.NamespaceRoot == "Revgen.StairDesign.Standalone.LiveReload",
                "the namespace root is the class name without its last segment, got '" + entry.NamespaceRoot + "'");
        }

        [Fact]
        public void ParseAll_StairEntry_PrefixIsFirstTwoNamespaceSegments()
        {
            var entry = LiveEntry.ParseAll(new[] { StairEntry }).Single();

            Assert.True(entry.NamespacePrefix == "Revgen.StairDesign",
                "the prefix must match assembly names such as Revgen.StairDesign.Domain, got '" + entry.NamespacePrefix + "'");
        }

        [Theory]
        [InlineData("LiveLoader.CommandAssembly")]
        [InlineData("CommandAssembly")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void ParseAll_FewerThanTwoDots_DropsValue(string value)
        {
            var entries = LiveEntry.ParseAll(new[] { value });

            Assert.True(entries.Count == 0, "a value without <namespace>.<Class>.<Method> names no method to call");
        }

        [Fact]
        public void ParseAll_DuplicateValues_KeepsOne()
        {
            var entries = LiveEntry.ParseAll(new[] { StairEntry, StairEntry });

            Assert.True(entries.Count == 1, "the same entry twice would resolve and stage-probe twice, got " + entries.Count);
        }

        [Fact]
        public void LiveEntriesFromFile_ConfigWithLiveEntries_ReturnsThem()
        {
            var path = WriteConfig("{\"enableToast\":true,\"liveEntries\":[\"" + StairEntry + "\"]}");

            var values = RvtMcpConfig.LiveEntriesFromFile(path);

            Assert.True(values.SequenceEqual(new[] { StairEntry }), "liveEntries must be read from the JSON file");
        }

        [Fact]
        public void LiveEntriesFromFile_ConfigWithoutKey_ReturnsEmpty()
        {
            var path = WriteConfig("{\"enableToast\":true}");

            var values = RvtMcpConfig.LiveEntriesFromFile(path);

            Assert.True(values.Count == 0, "no liveEntries key means no entry, so send_code runs as before");
        }

        private static string WriteConfig(string json)
        {
            var dir = Directory.CreateTempSubdirectory("rvtmcp-live-entry-").FullName;
            var path = Path.Combine(dir, "rvtmcp.config.json");
            File.WriteAllText(path, json);
            return path;
        }
    }
}

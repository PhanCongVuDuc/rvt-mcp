using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class RvtMcpConfigTask4Tests
    {
        private static string NewConfigPath()
        {
            return Path.Combine(Path.GetTempPath(), "rvtmcp-task4-" + Path.GetRandomFileName() + ".json");
        }

        private static Func<string, string> EnvLookup(Dictionary<string, string> values)
        {
            return name => values.TryGetValue(name, out var value) ? value : null;
        }

        private static JObject ParseRaw(string path)
        {
            using (var textReader = new StringReader(File.ReadAllText(path)))
            using (var jsonReader = new JsonTextReader(textReader)
            {
                DateParseHandling = DateParseHandling.None,
            })
            {
                return (JObject)JToken.ReadFrom(jsonReader);
            }
        }

        [Fact]
        public void TrySave_uses_atomic_replace_and_preserves_unknown_keys()
        {
            var path = NewConfigPath();
            try
            {
                File.WriteAllText(path, "{\"target\":\"2026\",\"ownerExtension\":{\"keep\":true}}");
                string error;
                Assert.True(RvtMcpConfig.TrySaveToastIdleSeconds(30, out error, path), error);

                var root = JObject.Parse(File.ReadAllText(path));
                Assert.Equal(30, (int)root["toastIdleSeconds"]);
                Assert.Equal("2026", (string)root["target"]);
                Assert.True((bool)root["ownerExtension"]["keep"]);
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".*.tmp"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Theory]
        [InlineData(10, 10)]
        [InlineData(20, 20)]
        [InlineData(30, 30)]
        [InlineData(60, 60)]
        [InlineData(9, 20)]
        [InlineData(61, 20)]
        public void Toast_idle_duration_accepts_only_spec_values(int input, int expected)
        {
            Assert.Equal(expected, RvtMcpConfig.NormalizeToastIdleSeconds(input));
        }

        [Fact]
        public void TrySave_rejects_a_non_object_config_without_replacing_it()
        {
            var path = NewConfigPath();
            try
            {
                const string original = "[]";
                File.WriteAllText(path, original);
                string error;
                Assert.False(RvtMcpConfig.TrySaveEnableToast(true, out error, path));
                Assert.Equal(original, File.ReadAllText(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

    }
}

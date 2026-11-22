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

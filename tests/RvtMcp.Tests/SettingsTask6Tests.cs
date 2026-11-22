using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public sealed class SettingsTask6Tests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "rvtmcp-settings-task6-" + Guid.NewGuid().ToString("N") + ".json");

        [Fact]
        public void Apply_PrivacyCache_IsStagedAndPreservesUnrelatedKeys()
        {
            RvtMcpConfig.SaveEnableToast(true, _path);
            var result = RvtMcpConfig.TryApplySettings(new RvtMcpConfig.SettingsPatch
            {
                CacheSendCodeBodies = true,
            }, _path);

            Assert.Contains("cacheSendCodeBodies", result.Applied);
            Assert.Empty(result.Failed);
            var root = JObject.Parse(File.ReadAllText(_path));
            Assert.True(root.Value<bool>("cacheSendCodeBodies"));
            Assert.True(root.Value<bool>("enableToast"));
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }
    }
}

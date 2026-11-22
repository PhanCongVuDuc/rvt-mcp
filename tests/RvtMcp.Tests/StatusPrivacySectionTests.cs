using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class StatusPrivacySectionTests
    {
        [Fact]
        public void Build_Defaults_ShowsOffFlagsAndToastOnWhenPassed()
        {
            var text = StatusPrivacySection.Build(new RvtMcpConfig(), toastEnabled: true);

            Assert.Contains("Toast notifications: ON", text);
            Assert.Contains("send_code: ON", text);
            Assert.Contains("Call log: OFF", text);
            Assert.Contains("Keep send_code bodies in History: OFF", text);
            Assert.DoesNotContain("bake", text, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Build_ReflectsEnabledFlags()
        {
            var config = new RvtMcpConfig { EnableCallLog = true, CacheSendCodeBodies = true };

            var text = StatusPrivacySection.Build(config, toastEnabled: false);

            Assert.Contains("Toast notifications: OFF", text);
            Assert.Contains("Call log: ON", text);
            Assert.Contains("Keep send_code bodies in History: ON", text);
        }

        [Fact]
        public void Build_NullConfig_DoesNotThrow()
        {
            var text = StatusPrivacySection.Build(null, toastEnabled: false);
            Assert.Contains("Call log: OFF", text);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class RvtMcpConfigLocalizationTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "rvt-l10n-" + Guid.NewGuid().ToString("N"));

        private string Cfg => Path.Combine(_dir, "rvtmcp.config.json");

        public RvtMcpConfigLocalizationTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static Func<string, string> EnvLookup(Dictionary<string, string> map) =>
            name => map != null && map.TryGetValue(name, out var v) ? v : null;

        // A torn read (another Revit instance mid-write) must never be treated as
        // an empty config — the write would silently drop readOnly and friends.

        private const string TornConfig = "{ \"readOnly\": true, \"uiLanguage\": \"en\"";

        [Fact]
        public void SaveEnableToast_MalformedConfig_LeavesFileUntouched()
        {
            File.WriteAllText(Cfg, TornConfig);
            RvtMcpConfig.SaveEnableToast(true, Cfg);
            Assert.Equal(TornConfig, File.ReadAllText(Cfg));
        }

    }
}

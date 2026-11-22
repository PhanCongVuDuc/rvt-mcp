using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Server.Memory;
using Xunit;

namespace RvtMcp.Tests
{
    public class HistoryReviewRegressionTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rvt-history-review-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Server_defaults_cannot_relax_plugin_policy()
        {
            var resolved = new RvtMcpConfig { ReadOnly = true, EnableSendCode = false }
                .WithRuntimeOptions(new RvtMcpConfig().ToRuntimeOptions());
            Assert.True(resolved.ReadOnlyOrDefault);
            Assert.False(resolved.EnableSendCodeOrDefault);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Either_side_can_disable_send_code(bool plugin, bool server)
        {
            var resolved = new RvtMcpConfig { EnableSendCode = plugin }
                .WithRuntimeOptions(new RvtMcpConfig { EnableSendCode = server }.ToRuntimeOptions());
            Assert.False(resolved.EnableSendCodeOrDefault);
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}

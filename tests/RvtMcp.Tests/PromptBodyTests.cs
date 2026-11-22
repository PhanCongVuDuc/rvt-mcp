using System;
using System.Collections.Generic;
using RvtMcp.Plugin;
using RvtMcp.Server;
using RvtMcp.Server.Prompts;
using Xunit;

namespace RvtMcp.Tests
{
    /// <summary>send_code gate → its own notice; otherwise the body with args substituted.</summary>
    [Collection("ServerStateConfig")]
    public class PromptBodyTests : IDisposable
    {
        private const string Body =
            "# Demo\n\nSteps\n1. call revit_get_current_view_info for {scope}.\n\nDo not\n- write.";

        public PromptBodyTests() { ServerState.Config = new RvtMcpConfig(); }
        public void Dispose() { ServerState.Config = null; }

        [Fact]
        public void Load_returns_null_for_missing_resource()
        {
            Assert.Null(PromptBody.Load("no_such_prompt"));
        }

        [Fact]
        public void Render_returns_body_with_args_applied()
        {
            var rendered = PromptBody.Render(Body, requiresSendCode: true,
                args: new Dictionary<string, string> { ["scope"] = "level 2" });

            Assert.Equal(Body.Replace("{scope}", "level 2"), rendered);
        }

        [Fact]
        public void Render_returns_sendcode_notice_when_send_code_is_disabled()
        {
            ServerState.Config = new RvtMcpConfig { EnableSendCode = false };

            var rendered = PromptBody.Render(Body, requiresSendCode: true);

            Assert.Contains("revit_send_code_to_revit", rendered);
            Assert.DoesNotContain("call revit_get_current_view_info", rendered);
        }

        [Fact]
        public void Render_ignores_disabled_send_code_when_the_prompt_does_not_need_it()
        {
            ServerState.Config = new RvtMcpConfig { EnableSendCode = false };

            Assert.Equal(Body, PromptBody.Render(Body, requiresSendCode: false));
        }

        [Fact]
        public void ApplyArgs_leaves_unmatched_placeholders_and_null_is_noop()
        {
            Assert.Equal("a {x} b", PromptBody.ApplyArgs("a {x} b", new Dictionary<string, string> { ["y"] = "1" }));
            Assert.Equal("a {x} b", PromptBody.ApplyArgs("a {x} b", null));
        }
    }
}

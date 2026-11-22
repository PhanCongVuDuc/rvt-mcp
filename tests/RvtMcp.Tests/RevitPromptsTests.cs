using System;
using System.Collections.Generic;
using RvtMcp.Plugin;
using RvtMcp.Server;
using RvtMcp.Server.Prompts;
using Xunit;

namespace RvtMcp.Tests
{
    [Collection("ServerStateConfig")]
    public class RevitPromptsTests : IDisposable
    {
        public RevitPromptsTests() { ServerState.Config = new RvtMcpConfig(); }
        public void Dispose() { ServerState.Config = null; }

        [Theory]
        [InlineData("stairs")]
        public void Embedded_prompt_body_loads(string name)
        {
            Assert.False(string.IsNullOrWhiteSpace(PromptBody.Load(name)), $"missing RvtMcp.Prompts.{name}.md");
        }

        [Fact]
        public void Stairs_renders_body_under_default_config()
        {
            var rendered = RevitPrompts.Stairs();

            Assert.Contains("revit_send_code_to_revit", rendered);
            Assert.Contains("confirm", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("--toolsets", rendered);
        }

        [Fact]
        public void Stairs_includes_failure_and_cleanup_template_without_checkout_docs()
        {
            var rendered = RevitPrompts.Stairs();

            Assert.DoesNotContain("docs/", rendered);
            Assert.Contains("```csharp", rendered);
            Assert.Contains("new RvtMcp.Plugin.SafeFailuresPreprocessor()", rendered);
            Assert.Contains("new StairsEditScope(doc", rendered);
            Assert.Contains("new Transaction(doc", rendered);
            Assert.Contains("SetFailuresPreprocessor(failures).SetClearAfterRollback(true)", rendered);
            Assert.Contains("txStatus != TransactionStatus.Committed", rendered);
            Assert.Contains("scope.Commit(failures)", rendered);
            Assert.Contains("!failures.HadErrors", rendered);
            Assert.Contains("doc.GetElement(stairsId) != null", rendered);
            Assert.Contains("failures.Messages", rendered);
            Assert.Contains("finally", rendered);
            Assert.Contains("if (scope.IsActive) scope.Cancel();", rendered);
            Assert.Contains("not a complete stair generator", rendered);
            Assert.Contains("Get my confirmation before writing anything", rendered);
        }

        [Fact]
        public void Stairs_returns_sendcode_notice_under_read_only()
        {
            ServerState.Config = new RvtMcpConfig { ReadOnly = true };

            var rendered = RevitPrompts.Stairs();

            Assert.Contains("--read-only", rendered);
            Assert.DoesNotContain("resolved design", rendered);
        }

        [Fact]
        public void Stairs_returns_enable_notice_when_meta_off_even_with_toolbaker()
        {
            // send_code stays available via the toolbaker set, but the body's step-0
            // revit_get_current_target lives in meta — the prompt must gate on it.
            ServerState.Config = new RvtMcpConfig
            {
                Toolsets = new List<string> { "toolbaker" }
            };

            var rendered = RevitPrompts.Stairs();

            Assert.Contains("meta", rendered);
            Assert.Contains("--toolsets", rendered);
            Assert.DoesNotContain("resolved design", rendered);
        }
    }
}

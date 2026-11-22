using System;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Plugin.Handlers;
using Xunit;

namespace RvtMcp.Tests
{
    public class CommandOutcomeTests
    {

        [Fact]
        public void Plain_success_passes_through()
        {
            var o = CommandOutcome.Normalize("create_level", true, new { elementId = 1 }, null, null);

            Assert.True(o.Success);
            Assert.Null(o.Error);
        }

        [Fact]
        public void Handler_failure_keeps_its_error()
        {
            var o = CommandOutcome.Normalize("create_level", false, null, "No document is open.", null);

            Assert.False(o.Success);
            Assert.Equal("No document is open.", o.Error);
        }

        [Fact]
        public void Rejected_oversized_response_is_a_failure_with_the_agent_error()
        {
            var o = CommandOutcome.Normalize("get_schedule_data", true, new { rows = 1 }, null,
                "Response too large.");

            Assert.False(o.Success);
            Assert.Equal("Response too large.", o.Error);
        }

        [Fact]
        public void Batch_handler_failure_keeps_its_error()
        {
            var o = CommandOutcome.Normalize("batch_execute", false, null, "No document is open.", null);

            Assert.False(o.Success);
            Assert.Equal("No document is open.", o.Error);
        }

        [Fact]
        public void Batch_shape_is_only_read_for_batch_execute()
        {
            var o = CommandOutcome.Normalize("send_code_to_revit", true,
                new { rolledBack = true, results = new[] { new { ok = false } } }, null, null);

            Assert.True(o.Success);
        }

        [Fact]
        public void McpEventHandler_reports_one_outcome_after_the_size_guard()
        {
            var source = ReadSource("Infrastructure", "McpEventHandler.cs");

            var guard = source.IndexOf("ResponseEnvelopeGuard.Apply(", StringComparison.Ordinal);
            var normalize = source.IndexOf("CommandOutcome.Normalize(", StringComparison.Ordinal);
            var fileLog = source.IndexOf("McpLogger.Log(request.CommandName, request.ParamsJson, outcome.Success", StringComparison.Ordinal);
            var sessionLog = source.IndexOf("Success = outcome.Success", StringComparison.Ordinal);

            Assert.True(guard >= 0 && normalize > guard, "outcome must be judged after the size guard");
            Assert.True(fileLog > normalize, "mcp-calls.jsonl (History of past sessions) must log the outcome");
            Assert.True(sessionLog > normalize, "History must record the outcome");
            Assert.DoesNotContain("Success = result.Success", source);
        }

        private static string ReadSource(string folder, string file, [CallerFilePath] string testFile = "")
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", ".."));
            return File.ReadAllText(Path.Combine(root, "src", "shared", folder, file));
        }
    }
}

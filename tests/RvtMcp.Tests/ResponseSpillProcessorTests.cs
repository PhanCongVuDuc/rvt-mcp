using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public sealed class ResponseSpillProcessorTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "rvtmcp-spill-processor-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Send_code_auto_spills_arbitrary_output_above_budget_without_false_failure()
        {
            var output = new string('x', ResponseSizeGuard.EnforcementBudgetBytes + 1000);
            var data = new JObject { ["executed"] = true, ["result"] = output };
            var rawResponse = JsonConvert.SerializeObject(new { success = true, data });
            var outcome = new ResponseSpillProcessor(new ResponseSpillWriter(_directory)).Process(
                "send_code_to_revit",
                "{\"code\":\"return value;\"}",
                true,
                data,
                rawResponse);

            Assert.True(outcome.Spilled);
            Assert.True(outcome.Automatic);
            var envelope = Assert.IsType<JObject>(outcome.Data);
            Assert.True(envelope.Value<bool>("success"));
            Assert.True(envelope.Value<bool>("automatic"));
            Assert.Equal("text", envelope.Value<string>("format"));
            Assert.Equal(output, File.ReadAllText(envelope.Value<string>("path")!, Encoding.UTF8));
            Assert.DoesNotContain("success=false", envelope.ToString(Formatting.None));
        }

        [Fact]
        public void Spill_write_failure_does_not_claim_dry_run_was_applied()
        {
            File.WriteAllText(_directory, "not a directory");
            var data = new JObject { ["dry_run"] = true, ["changed_elements"] = new JArray(1, 2) };
            var rawResponse = JsonConvert.SerializeObject(new { success = true, data });

            var outcome = new ResponseSpillProcessor(new ResponseSpillWriter(_directory)).Process(
                "workflow_data_roundtrip",
                "{\"output\":\"file\",\"dry_run\":true}",
                true,
                data,
                rawResponse);

            var envelope = Assert.IsType<JObject>(outcome.Data);
            Assert.False(envelope.Value<bool>("mutation_applied"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
                else if (File.Exists(_directory)) File.Delete(_directory);
            }
            catch { }
        }
    }
}

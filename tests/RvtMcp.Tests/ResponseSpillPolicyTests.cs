using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class ResponseSpillPolicyTests
    {

        [Theory]
        [InlineData("send_code_to_revit")]
        public void Arbitrary_code_commands_auto_spill_only_above_enforcement_budget(string command)
        {
            var large = ResponseSpillPolicy.Evaluate(
                command,
                "{}",
                ResponseSizeGuard.EnforcementBudgetBytes + 1);
            var bounded = ResponseSpillPolicy.Evaluate(
                command,
                "{}",
                ResponseSizeGuard.EnforcementBudgetBytes);

            Assert.True(large.ShouldSpill);
            Assert.True(large.Automatic);
            Assert.Equal(ResponseSpillFormat.Auto, large.Format);
            Assert.False(bounded.ShouldSpill);
        }

        [Fact]
        public void Spill_policy_extracts_only_arbitrary_user_output_for_auto_spill()
        {
            var sendData = new JObject
            {
                ["executed"] = true,
                ["result"] = new JArray(1, 2, 3)
            };

            Assert.Equal(sendData["result"], ResponseSpillPolicy.SelectPayload("send_code_to_revit", sendData));
            Assert.Same(sendData, ResponseSpillPolicy.SelectPayload("list_sheets", sendData));
        }
    }
}

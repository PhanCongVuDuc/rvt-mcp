using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class OversizedResponseScopeContractTests
    {

        [Fact]
        public void Event_boundary_processes_file_and_auto_spill_before_response_guarding()
        {
            var eventHandler = File.ReadAllText(Path.Combine(
                GetRepoRoot(), "src", "shared", "Infrastructure", "McpEventHandler.cs"));
            var guardIndex = eventHandler.IndexOf("ResponseEnvelopeGuard.Apply", StringComparison.Ordinal);
            var logIndex = eventHandler.IndexOf("string resultJson = null", StringComparison.Ordinal);

            Assert.True(guardIndex >= 0 && logIndex > guardIndex, "The shared spill/size boundary must run before result logging.");
            Assert.Contains("result.Data = preGuard[\"data\"]", eventHandler);
        }

        [Fact]
        public void Completed_oversized_mutation_is_compacted_without_false_failure()
        {
            var guarded = ResponseEnvelopeGuard.Apply("create_level", "{}", new JObject
            {
                ["success"] = true,
                ["data"] = new JObject { ["created_id"] = 123, ["detail"] = new string('x', 1100000) }
            }, new RvtMcpConfig());
            Assert.True(guarded.Value<bool>("success"));
            Assert.True(guarded["data"].Value<bool>("mutation_applied"));
            Assert.Equal(123, guarded["data"]["summary"].Value<int>("created_id"));
        }

        private static string ToWireName(string name)
        {
            var chars = new List<char>();
            foreach (var ch in name)
            {
                if (char.IsUpper(ch))
                {
                    chars.Add('_');
                    chars.Add(char.ToLowerInvariant(ch));
                }
                else
                {
                    chars.Add(ch);
                }
            }
            return new string(chars.ToArray());
        }

        private static string GetRepoRoot([CallerFilePath] string testFile = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", ".."));
        }
    }
}

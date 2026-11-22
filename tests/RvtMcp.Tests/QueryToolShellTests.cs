using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RvtMcp.Server;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class QueryToolShellTests
    {
        private static readonly string[] QueryToolsInClass =
        {
            "revit_get_current_view_info",
        };

        [Fact]
        public void Every_query_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(QueryTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(QueryToolsInClass, declared);
        }

        [Fact]
        public async Task Get_current_view_info_sends_no_payload()
        {
            var sent = await Capture.Send(() => QueryTools.GetCurrentViewInfo());

            Assert.Equal("get_current_view_info", sent.Command);
            Assert.Null(sent.Parameters);
            Assert.Null(sent.TimeoutSeconds);
        }

        [Fact]
        public async Task Gateway_failure_returns_an_error_string()
        {
            await Capture.WithSendOverride(
                (command, parameters, timeout) => throw new InvalidOperationException("Revit is running but no document is open."),
                async () =>
                {
                    var text = await QueryTools.GetCurrentViewInfo();
                    Assert.Equal("Error: Revit is running but no document is open.", text);
                });
        }

    }
}

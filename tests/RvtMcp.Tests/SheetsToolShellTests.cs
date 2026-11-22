using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RvtMcp.Server;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class SheetsToolShellTests
    {
        private static readonly string[] SheetsToolsInClass =
        {
            "revit_list_sheets",
        };

        [Fact]
        public void Every_sheets_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(SheetsTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(SheetsToolsInClass, declared);
        }

        [Fact]
        public async Task List_sheets_sends_filters()
        {
            var sent = await Capture.Send(() => SheetsTools.ListSheets("A", "Plan", false, true, false, 25));

            Assert.Equal("list_sheets", sent.Command);
            var json = sent.Json();
            Assert.Equal("A", json.Value<string>("number_filter"));
            Assert.Equal("Plan", json.Value<string>("name_pattern"));
            Assert.False(json.Value<bool>("include_revisions"));
            Assert.True(json.Value<bool>("include_viewports"));
            Assert.False(json.Value<bool>("include_placeholders"));
            Assert.Equal(25, json.Value<int>("limit"));
        }

    }
}

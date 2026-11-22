using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RvtMcp.Server;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class MetaToolShellTests
    {
        // revit_send_code_to_revit lives in SendCodeTools (same "meta" toolset) and is
        // covered separately; this class pins the MetaTools class surface.
        private static readonly string[] MetaToolsInClass =
        {
            "revit_get_current_target",
            "revit_list_available_targets",
            "revit_list_recent_models",
            "revit_open_model",
            "revit_switch_target"
        };

        [Fact]
        public void Every_meta_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(MetaTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(MetaToolsInClass, declared);
        }

        [Fact]
        public async Task List_recent_models_sends_no_payload()
        {
            var sent = await Capture.Send(() => MetaTools.ListRecentModels());

            Assert.Equal("list_recent_models", sent.Command);
            Assert.Null(sent.Parameters);
        }

        [Fact]
        public async Task Open_model_sends_path_and_timeout_separately()
        {
            var sent = await Capture.Send(() => MetaTools.OpenModel(@"C:\models\a.rvt", false, true, "none", 120));

            Assert.Equal("open_model", sent.Command);
            var json = sent.Json();
            Assert.Equal(@"C:\models\a.rvt", json.Value<string>("path"));
            Assert.False(json.Value<bool>("activate"));
            Assert.True(json.Value<bool>("audit"));
            Assert.Equal("none", json.Value<string>("worksets"));
            Assert.Equal(120, sent.TimeoutSeconds);
        }

        [Fact]
        public void List_available_targets_returns_discovery_payload()
        {
            var json = JObject.Parse(MetaTools.ListAvailableTargets());

            Assert.NotNull(json["discovery_dir"]);
            Assert.NotNull(json["targets"]);
            Assert.NotNull(json["count"]);
        }

        [Fact]
        public void Get_current_target_reports_pin_and_connection()
        {
            var json = JObject.Parse(MetaTools.GetCurrentTarget());

            Assert.NotNull(json["pinned_target"]);
            Assert.True(json.ContainsKey("currently_connected_year"));
            Assert.True(json.ContainsKey("currently_connected_pid"));
        }

        [Fact]
        public async Task Switch_target_rejects_r_codes()
        {
            var text = await MetaTools.SwitchTarget("R24");

            var json = JObject.Parse(text);
            Assert.False(json.Value<bool>("ok"));
            Assert.Equal("revit_list_available_targets", json.Value<string>("recommended_next_tool"));
        }

        [Fact]
        public async Task Switch_target_rejects_unknown_year()
        {
            var text = await MetaTools.SwitchTarget("1999");

            var json = JObject.Parse(text);
            Assert.False(json.Value<bool>("ok"));
            Assert.Equal("revit_list_available_targets", json.Value<string>("recommended_next_tool"));
        }

    }
}

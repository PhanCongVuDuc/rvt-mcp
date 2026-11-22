using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using RvtMcp.Server;
using RvtMcp.Tests.Helpers;
using Xunit;

namespace RvtMcp.Tests
{
    public class ViewToolShellTests
    {
        private static readonly string[] ViewToolsInClass =
        {
            "revit_activate_view",
            "revit_capture_view_image",
        };

        [Fact]
        public void Every_view_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(ViewTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ViewToolsInClass, declared);
        }

        [Fact]
        public async Task Capture_view_image_sends_snake_case()
        {
            var sent = await Capture.Send(() => ViewTools.CaptureViewImage(null, 10449048, 800, "jpeg"));

            Assert.Equal("capture_view_image", sent.Command);
            var json = sent.Json();
            Assert.Equal(10449048, json.Value<long>("view_id"));
            Assert.Equal(800, json.Value<int>("pixel_size"));
            Assert.Equal("jpeg", json.Value<string>("image_format"));
        }

        [Fact]
        public async Task Activate_view_sends_selector()
        {
            var sent = await Capture.Send(() => ViewTools.ActivateView(null, "V1"));

            Assert.Equal("activate_view", sent.Command);
            var json = sent.Json();
            Assert.Null(json["view_id"]);
            Assert.Equal("V1", json.Value<string>("view_name"));
        }

    }
}

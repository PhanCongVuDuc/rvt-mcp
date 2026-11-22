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
    public class ExportToolShellTests
    {
        private static readonly string[] ExportToolsInClass =
        {
            "revit_export_image",
            "revit_export_pdf",
        };

        [Fact]
        public void Every_export_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(ExportTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ExportToolsInClass, declared);
        }

        [Fact]
        public async Task Export_pdf_sends_snake_case()
        {
            var sent = await Capture.Send(() => ExportTools.ExportPdf(@"C:\out", new long[] { 7, 8 }, true, "set"));

            Assert.Equal("export_pdf", sent.Command);
            var json = sent.Json();
            Assert.Equal(@"C:\out", json.Value<string>("output_folder"));
            Assert.Equal(new long[] { 7, 8 }, json["view_ids"].ToObject<long[]>());
            Assert.True(json.Value<bool>("combine"));
            Assert.Equal("set", json.Value<string>("file_name"));
        }

        [Fact]
        public async Task Export_image_sends_output_path_and_format()
        {
            var sent = await Capture.Send(() => ExportTools.ExportImage(@"C:\out\v.png", 7, 1024, "png"));

            Assert.Equal("export_image", sent.Command);
            var json = sent.Json();
            Assert.Equal(@"C:\out\v.png", json.Value<string>("output_path"));
            Assert.Equal(7, json.Value<long>("view_id"));
            Assert.Equal(1024, json.Value<int>("pixel_size"));
            Assert.Equal("png", json.Value<string>("image_format"));
        }

    }
}

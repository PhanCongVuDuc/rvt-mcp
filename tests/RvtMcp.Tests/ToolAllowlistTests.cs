using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RvtMcp.Plugin;
using RvtMcp.Server;
using Xunit;

namespace RvtMcp.Tests
{
    public class ToolAllowlistTests
    {
        private static string[] Registered(RvtMcpConfig config) =>
            Program.ResolveRegisteredToolMethods(ToolsetFilter.Resolve(config), config)
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>().Name)
                .OrderBy(n => n)
                .ToArray();

        private static string MissingConfigFile() => Path.Combine(Path.GetTempPath(), "rvtmcp-none-" + System.Guid.NewGuid() + ".json");

        [Fact]
        public void Allowlist_alone_registers_exactly_the_listed_tools_from_any_toolset()
        {
            // capture_view_image (view), list_sheets (sheets), export_pdf (export), send_code (meta):
            // sheets and export are outside the default toolsets, yet the allowlist reaches them.
            var config = new RvtMcpConfig
            {
                Tools = new List<string> { "revit_capture_view_image", "list_sheets", "revit_export_pdf", "send_code_to_revit" }
            };

            Assert.Equal(
                new[] { "revit_capture_view_image", "revit_export_pdf", "revit_list_sheets", "revit_send_code_to_revit" },
                Registered(config));
        }

        [Fact]
        public void Allowlist_intersects_with_explicit_toolsets()
        {
            var config = new RvtMcpConfig
            {
                Toolsets = new List<string> { "view", "meta" },
                Tools = new List<string> { "revit_capture_view_image", "revit_list_sheets" }
            };

            Assert.Equal(new[] { "revit_capture_view_image" }, Registered(config));
        }

        [Fact]
        public void Read_only_still_filters_an_allowlisted_write_tool()
        {
            var config = new RvtMcpConfig
            {
                ReadOnly = true,
                Tools = new List<string> { "revit_list_sheets", "revit_export_pdf", "revit_send_code_to_revit" }
            };

            Assert.Equal(new[] { "revit_list_sheets" }, Registered(config));
        }

        [Fact]
        public void Unknown_allowlisted_names_are_reported()
        {
            var config = new RvtMcpConfig { Tools = new List<string> { "revit_list_sheets", "revit_no_such_tool" } };

            Assert.Equal(new[] { "revit_no_such_tool" }, Program.UnknownAllowlistedTools(config));
            Assert.Empty(Program.UnknownAllowlistedTools(new RvtMcpConfig()));
        }

        [Fact]
        public void No_allowlist_keeps_the_toolset_surface()
        {
            Assert.Equal(48, Registered(new RvtMcpConfig()).Length);
        }

        [Fact]
        public void Tools_load_from_cli_env_and_json_with_cli_winning()
        {
            var path = Path.Combine(Path.GetTempPath(), "rvtmcp-tools-" + System.Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"tools\":[\"revit_list_sheets\"]}");
            try
            {
                var fromJson = RvtMcpConfig.Load(null, path, _ => null);
                Assert.Equal(new[] { "revit_list_sheets" }, fromJson.Tools);

                var fromEnv = RvtMcpConfig.Load(null, path,
                    name => name == RvtMcpConfig.EnvTools ? "revit_open_model, revit_switch_target" : null);
                Assert.Equal(new[] { "revit_open_model", "revit_switch_target" }, fromEnv.Tools);

                var fromCli = RvtMcpConfig.Load(new[] { "--tools", "revit_capture_view_image" }, path,
                    name => name == RvtMcpConfig.EnvTools ? "revit_open_model" : null);
                Assert.Equal(new[] { "revit_capture_view_image" }, fromCli.Tools);
            }
            finally
            {
                File.Delete(path);
            }

            Assert.Null(RvtMcpConfig.Load(null, MissingConfigFile(), _ => null).Tools);
        }
    }
}

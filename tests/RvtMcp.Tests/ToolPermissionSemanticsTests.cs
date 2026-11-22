using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using RvtMcp.Plugin;
using RvtMcp.Server;
using Xunit;

namespace RvtMcp.Tests
{
    public class ToolPermissionSemanticsTests
    {
        [Fact]
        public void Non_destructive_write_tools_are_additive_or_session_navigation()
        {
            var additivePrefixes = new[] { "create_", "duplicate_", "tag_", "place_", "add_", "auto_create_" };
            var otherAdditive = new HashSet<string>(StringComparer.Ordinal)
            {
                "compute_room_finishes", "export_room_data", "export_shared_parameter_file",
                "get_material_takeoff", "import_cad_to_view", "link_revit_model", "list_bake_suggestions",
                "open_model", "workflow_room_documentation",
                "switch_target", // Session navigation: repins the target binding; touches no document data.
            };
            foreach (var method in Tools())
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr.Name == "revit_send_code_to_revit" || attr.ReadOnly || attr.Destructive) continue;
                var command = attr.Name.Substring(6);
                Assert.True(additivePrefixes.Any(prefix => command.StartsWith(prefix, StringComparison.Ordinal))
                    || otherAdditive.Contains(command), attr.Name + " needs a strongest-branch permission review.");
            }
        }

        [Theory]
        [InlineData("revit_open_model")]
        public void Additive_or_navigation_tools_do_not_gain_destructive_hints(string name)
        {
            var method = Assert.Single(Tools(), m => m.GetCustomAttribute<McpServerToolAttribute>().Name == name);
            Assert.False(McpServerTool.Create(method, (object)null).ProtocolTool.Annotations.DestructiveHint);
        }

        [Fact]
        public void Destructive_descriptions_explain_recovery_and_read_only_tools_remain_non_destructive()
        {
            foreach (var method in Tools())
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr.Name == "revit_send_code_to_revit") continue;
                if (attr.ReadOnly) Assert.False(attr.Destructive);
                if (!attr.Destructive) continue;
                var description = method.GetCustomAttribute<DescriptionAttribute>().Description;
                Assert.True(new[] { "Undo", "back up", "backup", "restore", "cannot be undone" }
                    .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase)), attr.Name);
            }
        }

        private static MethodInfo[] Tools()
        {
            var config = new RvtMcpConfig { };
            return Program.ResolveRegisteredToolMethods(config).ToArray();
        }
    }
}

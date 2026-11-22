using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using RvtMcp.Plugin;
using RvtMcp.Tests.Helpers;
using ModelContextProtocol.Server;
using Xunit;

namespace RvtMcp.Tests
{
    public class ToolsListSnapshotTests
    {
        private static readonly string GoldenPath = Path.Combine(
            Path.GetDirectoryName(typeof(ToolsListSnapshotTests).Assembly.Location)!,
            "..", "..", "..", "Golden", "tools-list.json");


        [Fact]
        public void Tools_list_matches_golden_snapshot()
        {
            var captured = CaptureToolsList(new RvtMcpConfig());

            SnapshotSerializer.VerifyGolden(GoldenPath, captured, "ToolsListSnapshot");
        }

        [Fact]
        public void Default_tools_snapshot_does_not_expose_adaptive_bake_suggestions()
        {
            var captured = CaptureToolsList(new RvtMcpConfig());

            Assert.DoesNotContain("\"name\": \"revit_list_bake_suggestions\"", captured);
            Assert.DoesNotContain("\"name\": \"revit_accept_bake_suggestion\"", captured);
            Assert.DoesNotContain("\"name\": \"revit_dismiss_bake_suggestion\"", captured);
        }

        [Fact]
        public void Disabled_send_code_is_hidden()
        {
            var captured = CaptureToolsList(new RvtMcpConfig { EnableSendCode = false });
            Assert.DoesNotContain("\"name\": \"revit_send_code_to_revit\"", captured);
            Assert.Contains("\"name\": \"revit_list_available_targets\"", captured);
        }

        [Fact]
        public void Server_instructions_stay_under_2048_utf8_bytes()
        {
            var programType = typeof(RvtMcp.Server.MetaTools).Assembly.GetType("RvtMcp.Server.Program")!;
            var field = programType.GetField("ServerInstructionsText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);
            var text = (string)(field!.GetRawConstantValue() ?? field.GetValue(null)!);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
            Assert.True(bytes <= 2048, $"ServerInstructionsText is {bytes} UTF-8 bytes (Anthropic Tool Search cap is 2048).");
        }

        [Fact]
        public void Generated_tools_snapshot_does_not_include_removed_bake_tool()
        {
            var captured = CaptureToolsList(new RvtMcpConfig());

            Assert.DoesNotContain("\"name\": \"revit_bake_tool\"", captured);
        }

        private static string CaptureToolsList(RvtMcpConfig config)
        {
            var tools = RvtMcp.Server.Program.ResolveRegisteredToolTypes(config)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .SelectMany(cls => cls.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null))
                .Select(ToToolMetadata)
                .ToArray();

            return SnapshotSerializer.Serialize(tools.Length, tools);
        }

        private static object ToToolMetadata(MethodInfo method)
        {
            var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>()!;
            var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
            var description = descAttr?.Description ?? string.Empty;
            var name = toolAttr.Name ?? ToSnakeCase(method.Name);

            var parameters = method.GetParameters()
                .Select(p => new
                {
                    name = p.Name,
                    type = p.ParameterType.Name,
                    required = !p.HasDefaultValue,
                    description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty
                })
                .ToArray();

            var metadata = Newtonsoft.Json.Linq.JObject.FromObject(new
            {
                name,
                description_hash = SnapshotSerializer.HashDescription(description),
                inputSchema = new
                {
                    type = "object",
                    properties = parameters.ToDictionary(p => p.name!, p => new { type = p.type, description = p.description }),
                    required = parameters.Where(p => p.required).Select(p => p.name).ToArray()
                }
            });
            var annotations = McpServerTool.Create(method, (object)null).ProtocolTool.Annotations;
            if (annotations != null) metadata["annotations"] = Newtonsoft.Json.Linq.JObject.Parse(
                System.Text.Json.JsonSerializer.Serialize(annotations, ModelContextProtocol.McpJsonUtilities.DefaultOptions));
            return metadata;
        }

        private static string ToSnakeCase(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }
    }
}

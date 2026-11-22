using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Server
{
    /// <summary>
    /// Commands of other add-ins running in the bound Revit (contract: Sonny specs/002-sonny-revit-scaffold/contracts/
    /// rvt-mcp-addin-commands.md). An add-in registers them in-process through an AppDomain dictionary of BCL types; a
    /// new command of that add-in shows up here without changing or rebuilding rvt-mcp.
    /// </summary>
    [McpServerToolType]
    public class AddinQueryTools
    {
        [McpServerTool(Name = "revit_list_addin_commands", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
         Description("List the commands other add-ins registered in this Revit session (for example Sonny.Revit). Returns " +
                     "{addins: [{addin, version, protocol, commands: [{name, description, readOnly, argsSchema, resultSchema}]}]}; " +
                     "an empty list when no add-in registered. Call before revit_call_addin_command.")]
        public static async Task<string> ListAddinCommands()
        {
            try
            {
                var result = await ToolGateway.SendToRevit("list_addin_commands");
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }
    }

    [McpServerToolType]
    public class AddinMetaTools
    {
        [McpServerTool(Name = "revit_call_addin_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
         Description("Run a command another add-in registered in this Revit session. addin: add-in name, command: command " +
                     "name, both from revit_list_addin_commands; args: JSON object matching the command's argsSchema. Returns the " +
                     "add-in's envelope {ok: true, result} or {ok: false, error: {code, message, args, hint}}. " +
                     "Example: addin='Sonny.Revit', command='walls.count', args={\"level\":\"Level 1\"}. " +
                     "A command that changes the model runs in its own Revit transaction, so Revit Undo reverts it; back up files it writes.")]
        public static async Task<string> CallAddinCommand(string addin, string command, object args = null, int? timeout_seconds = null)
        {
            try
            {
                var arguments = ArgsObject(args);
                var result = await ToolGateway.SendToRevit("call_addin_command",
                    new { addin, command, args = arguments, entry = "mcp" }, timeout_seconds);
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex) { return $"Error: {ex.Message}"; }
        }

        internal static JObject ArgsObject(object args)
        {
            switch (args)
            {
                case null:
                    return new JObject();
                case JObject obj:
                    return obj;
                case string text when string.IsNullOrWhiteSpace(text):
                    return new JObject();
                case string text:
                    return JObject.Parse(text);
                case JsonElement element when element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null:
                    return new JObject();
                case JsonElement element when element.ValueKind == JsonValueKind.Object:
                    return JObject.Parse(element.GetRawText());
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    return JObject.Parse(element.GetString() ?? "{}");
                default:
                    throw new ArgumentException("args must be a JSON object.");
            }
        }
    }
}

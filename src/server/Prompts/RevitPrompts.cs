using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RvtMcp.Server.Prompts
{
    /// <summary>
    /// MCP prompt surface (spec D1–D4). Always registered — prompts don't consume model
    /// context until the user invokes one. Each method renders its embedded markdown
    /// through <see cref="PromptBody"/>, which swaps in an enable notice when the
    /// prompt's toolsets (or send_code) aren't exposed on this server.
    /// </summary>
    [McpServerPromptType]
    public class RevitPrompts
    {

        [McpServerPrompt(Name = "revit_stairs"),
         Description("Guided stair creation via send_code: resolve the design together, confirm, then write and verify.")]
        public static string Stairs()
        {
            return PromptBody.Render(
                PromptBody.Load("stairs"), requiresSendCode: true);
        }
    }
}

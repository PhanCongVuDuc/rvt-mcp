using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;

namespace RvtMcp.Server.Prompts
{
    /// <summary>
    /// Loads embedded prompt bodies (resource RvtMcp.Prompts.&lt;name&gt;.md) and renders
    /// them against the active toolset configuration. Spec D2: when the prompt's required
    /// toolsets are missing — or it needs send_code that is not exposed — the renderer
    /// returns a short enable notice INSTEAD of the body, so the agent never guesses.
    /// </summary>
    internal static class PromptBody
    {
        public const string ResourcePrefix = "RvtMcp.Prompts.";

        /// <summary>Returns null when the resource is not embedded. Never throws.</summary>
        public static string Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                var asm = typeof(PromptBody).Assembly;
                using (var stream = asm.GetManifestResourceStream(ResourcePrefix + name + ".md"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch { return null; }
        }

        /// <summary>The send_code notice replaces the body entirely; a prompt that cannot run is never half-shown.</summary>
        public static string Render(string body, bool requiresSendCode, IReadOnlyDictionary<string, string> args = null)
        {
            if (body == null)
                return "This prompt's body was not found in the installed server build.";
            if (requiresSendCode && ServerState.Config?.EnableSendCodeOrDefault == false)
                return SendCodeOffNotice();
            return ApplyArgs(body, args);
        }

        public static string ApplyArgs(string body, IReadOnlyDictionary<string, string> args)
        {
            if (string.IsNullOrEmpty(body) || args == null) return body;
            foreach (var kv in args)
                body = body.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty);
            return body;
        }

        private static string SendCodeOffNotice()
        {
            return
                "This prompt works through revit_send_code_to_revit, which is not exposed right now.\n" +
                "send_code is removed when the server runs with --disable-send-code (or enableSendCode=false).\n" +
                "Remove that setting, then restart the MCP connection.";
        }
    }
}

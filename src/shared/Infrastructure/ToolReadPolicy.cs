using System;
using System.Collections.Generic;

namespace RvtMcp.Plugin
{
    /// <summary>File/document read policy shared with the plugin. Kept equal to MCP annotations by tests.</summary>
    public static class ToolReadPolicy
    {
        public static readonly ISet<string> ReadOnlyCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "activate_view",
            "get_current_target",
            "get_current_view_info",
            "list_addin_commands",
            "list_available_targets",
            "list_recent_models",
            "list_sheets",
        };

        public static bool IsReadOnly(string command) => command != null && ReadOnlyCommands.Contains(command);

        public static string Rejection(string command, bool enableSendCode)
        {
            if (string.Equals(command, "send_code_to_revit", StringComparison.OrdinalIgnoreCase)
                && !enableSendCode)
                return "SEND_CODE_DISABLED: send_code is disabled for this session.";
            return null;
        }
    }
}

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

        public static string Rejection(string command, bool readOnly, bool enableSendCode)
        {
            if (string.Equals(command, "send_code_to_revit", StringComparison.OrdinalIgnoreCase)
                && !enableSendCode)
                return "SEND_CODE_DISABLED: send_code is disabled for this session.";
            // call_addin_command stays callable in read-only sessions: the tool reads the add-in's catalog and refuses a
            // command declared readOnly=false, and the plugin checks again with read_only=true (AddinCommandRegistry).
            if (readOnly && !IsReadOnly(command) && !string.Equals(command, "call_addin_command", StringComparison.OrdinalIgnoreCase))
                return "READ_ONLY: this tool can change documents or files and is disabled for this session.";
            return null;
        }
    }
}

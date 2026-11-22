using System.Text;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Formats privacy / bake flag lines for the ribbon Status dialog (Wave A5).
    /// API-free so unit tests can cover copy without Revit.
    /// </summary>
    public static class StatusPrivacySection
    {
        public static string Build(RvtMcpConfig config, bool toastEnabled)
        {
            if (config == null)
                config = new RvtMcpConfig();

            var sb = new StringBuilder();
            sb.AppendLine("Privacy (plugin-visible config)");
            sb.AppendLine($"  Toast notifications: {OnOff(toastEnabled)}  (ribbon toggle)");
            sb.AppendLine($"  send_code: {OnOff(config.EnableSendCodeOrDefault)}");
            sb.AppendLine($"  Call log: {OnOff(config.EnableCallLogOrDefault)}  (authenticated server options override per request)");
            sb.AppendLine($"  Keep send_code bodies in History: {OnOff(config.CacheSendCodeBodiesOrDefault)}");
            sb.AppendLine();
            sb.Append("Note: change these in %LOCALAPPDATA%\\Bimwright\\rvt-mcp\\rvtmcp.config.json; restart the MCP client after server flags change.");
            return sb.ToString();
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
    }
}

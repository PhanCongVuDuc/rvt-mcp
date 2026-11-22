using System;
using Autodesk.Revit.UI;
using RvtMcp.Plugin.Localization;

namespace RvtMcp.Plugin
{
    public class IdlingUpdater
    {
        private readonly RibbonResult _ribbon;
        private DateTime _lastUpdate = DateTime.MinValue;
        private bool _lastRunning;
        private int _lastCount;
        private bool _lastToastEnabled;
        private int _lastLocVersion = -1;

        public IdlingUpdater(RibbonResult ribbon)
        {
            _ribbon = ribbon;
        }

        public void Update(bool isRunning, ITransportServer transport, McpSessionLog sessionLog, bool toastEnabled)
        {
            if (_ribbon == null) return;

            var now = DateTime.Now;
            if ((now - _lastUpdate).TotalMilliseconds < 1000) return;
            _lastUpdate = now;

            var count = sessionLog?.Count ?? 0;

            // Language change (Settings command or override-file hot reload) forces a
            // full text refresh even when running/count/toast flags are unchanged.
            var locVersion = L.Version;
            var locChanged = locVersion != _lastLocVersion;
            if (locChanged) _lastLocVersion = locVersion;

            // Only update UI if state changed
            if (locChanged || isRunning != _lastRunning)
            {
                _lastRunning = isRunning;
                ApplyToggleState(isRunning, transport);
            }
            else if (isRunning)
            {
                // Update tooltip for client/lastCmd changes even if running state didn't change
                _ribbon.ToggleButton.ToolTip = RunningTooltip(transport);
            }

            if (locChanged || count != _lastCount)
            {
                _lastCount = count;
                _ribbon.HistoryButton.ItemText = L.T("ribbon.history.text", ("count", count));
                _ribbon.HistoryButton.ToolTip = L.T("ribbon.history.tooltip");
            }

            if (_ribbon.ToastButton != null && (locChanged || toastEnabled != _lastToastEnabled))
            {
                _lastToastEnabled = toastEnabled;
                _ribbon.ToastButton.ItemText = L.T("ribbon.toast.text");
                if (toastEnabled)
                {
                    _ribbon.ToastButton.LargeImage = IconGenerator.ToastOn32;
                    _ribbon.ToastButton.Image = IconGenerator.ToastOn16;
                    _ribbon.ToastButton.ToolTip = L.T("ribbon.toast.tooltip.enabled");
                }
                else
                {
                    _ribbon.ToastButton.LargeImage = IconGenerator.ToastOff32;
                    _ribbon.ToastButton.Image = IconGenerator.ToastOff16;
                    _ribbon.ToastButton.ToolTip = L.T("ribbon.toast.tooltip.disabled");
                }
            }

        }

        private void ApplyToggleState(bool isRunning, ITransportServer transport)
        {
            var btn = _ribbon.ToggleButton;
            if (isRunning)
            {
                btn.ItemText = L.T("ribbon.toggle.text.running");
                btn.LargeImage = IconGenerator.McpOn32;
                btn.Image = IconGenerator.McpOn16;
                btn.ToolTip = RunningTooltip(transport);
            }
            else
            {
                btn.ItemText = L.T("ribbon.toggle.text.stopped");
                btn.LargeImage = IconGenerator.McpOff32;
                btn.Image = IconGenerator.McpOff16;
                btn.ToolTip = L.T("ribbon.toggle.tooltip.stopped");
            }
        }

        private static string RunningTooltip(ITransportServer transport)
        {
            var client = transport.IsClientConnected
                ? L.T("ribbon.toggle.client.connected")
                : L.T("ribbon.toggle.client.waiting");
            var lastCmd = transport.LastCommandTime?.ToString("HH:mm:ss")
                ?? L.T("ribbon.toggle.lastCmd.none");
            return L.T("ribbon.toggle.tooltip.running",
                ("connectionInfo", transport.ConnectionInfo),
                ("client", client),
                ("lastCmd", lastCmd));
        }

    }
}

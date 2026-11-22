using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace RvtMcp.Server
{
    /// <summary>
    /// Tool-call activity as a Windows notification, raised by the server so Revit's UI thread draws nothing.
    /// One notification per server session: calls update it in place (no new banner); a call after the idle
    /// window starts a new one.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    internal sealed class ServerToast
    {
        internal const string AppId = "Bimwright.RvtMcp";
        private const string Group = "rvt-mcp";

        // {title}/{line}/{counts} are bound so ToastNotifier.Update can refresh the shown notification silently.
        private const string Template =
            "<toast duration=\"short\"><visual><binding template=\"ToastGeneric\">" +
            "<text>{title}</text><text>{line}</text><text>{counts}</text>" +
            "</binding></visual><audio silent=\"true\"/></toast>";

        internal static ServerToast Instance { get; set; }

        private readonly object _lock = new object();
        private readonly string _tag = "s" + Environment.ProcessId;
        private readonly string _configPath;
        private readonly ToastNotifier _notifier;
        private DateTime _configStamp;
        private bool _enabled;
        private int _idleSeconds;
        private DateTime _lastCall = DateTime.MinValue;
        private int _ok, _failed;
        private uint _sequence;

        private ServerToast(RvtMcpConfig config, string configPath)
        {
            _configPath = configPath;
            _enabled = config.EnableToastOrDefault;
            _idleSeconds = config.ToastIdleSecondsOrDefault;
            _configStamp = Stamp(configPath);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        }

        internal static ServerToast TryCreate(RvtMcpConfig config, string configPath = null)
        {
            try
            {
                // An unpackaged exe needs a registered AppUserModelID before Windows shows its notifications.
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
                    key.SetValue("DisplayName", "rvt-mcp");
                return new ServerToast(config, configPath ?? RvtMcpConfig.DefaultConfigFilePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[RvtMcp] Notifications unavailable: " + ex.Message);
                return null;
            }
        }

        /// <summary>Queued so a slow notification platform never delays the tool response.</summary>
        internal void Record(string tool, bool failed, string revitYear)
            => Task.Run(() =>
            {
                try { Publish(tool, failed, revitYear); }
                catch (Exception ex) { Console.Error.WriteLine("[RvtMcp] Notification failed: " + ex.Message); }
            });

        private void Publish(string tool, bool failed, string revitYear)
        {
            lock (_lock)
            {
                RefreshConfig();
                if (!_enabled) return;

                var now = DateTime.UtcNow;
                var fresh = (now - _lastCall).TotalSeconds > _idleSeconds;
                _lastCall = now;
                if (fresh) { _ok = 0; _failed = 0; }
                if (failed) _failed++; else _ok++;

                var values = new Dictionary<string, string>
                {
                    ["title"] = string.IsNullOrEmpty(revitYear) ? "rvt-mcp" : "rvt-mcp " + revitYear,
                    ["line"] = (failed ? "✗ " : "✓ ") + (tool ?? ""),
                    ["counts"] = "Success " + _ok + " · Failed " + _failed,
                };

                if (!fresh && _notifier.Update(Data(values), _tag, Group) != NotificationUpdateResult.NotificationNotFound)
                    return;
                // Dismissed mid-run: stay quiet until the next idle gap rather than re-raising a banner per call.
                if (!fresh) return;

                var xml = new XmlDocument();
                xml.LoadXml(Template);
                _notifier.Show(new ToastNotification(xml)
                {
                    Tag = _tag,
                    Group = Group,
                    Data = Data(values),
                    ExpirationTime = DateTimeOffset.Now.AddHours(1),
                });
            }
        }

        private NotificationData Data(Dictionary<string, string> values)
        {
            var data = new NotificationData { SequenceNumber = ++_sequence };
            foreach (var pair in values) data.Values[pair.Key] = pair.Value;
            return data;
        }

        /// <summary>The ribbon Toast button and Settings write the shared config file; follow it without a restart.</summary>
        private void RefreshConfig()
        {
            var stamp = Stamp(_configPath);
            if (stamp == _configStamp) return;
            _configStamp = stamp;
            try
            {
                var json = File.Exists(_configPath) ? JObject.Parse(File.ReadAllText(_configPath)) : new JObject();
                _enabled = json.Value<bool?>("enableToast") ?? RvtMcpConfig.DefaultEnableToast;
                _idleSeconds = RvtMcpConfig.NormalizeToastIdleSeconds(json.Value<int?>("toastIdleSeconds"));
            }
            catch { /* half-written file: keep the last good values */ }
        }

        private static DateTime Stamp(string path)
            => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }
}

using System;
using System.Reflection;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using RvtMcp.Plugin.Localization;

namespace RvtMcp.Plugin
{
    public class RibbonResult
    {
        public PushButton ToggleButton { get; set; }
        public PushButton HistoryButton { get; set; }
        public PushButton ToastButton { get; set; }
        public PushButton SettingsButton { get; set; }
        public PushButton LanguageButton { get; set; }
    }

    public static class RibbonSetup
    {
        private const string PanelName = "RvtMcp";
        private static readonly HashSet<string> CreatedButtons = new HashSet<string>();
        /// <summary>
        /// Revit routes every item added after <c>AddSlideOut()</c> into the
        /// slide-out — so ordering is contractual: all main-panel items first,
        /// then AddSlideOut, then Settings and Language commands.
        /// </summary>
        public static RibbonResult Create(UIControlledApplication application, RvtMcpConfig config = null)
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var panel = ResolvePanel(application);

            var toggleData = new PushButtonData(
                "ToggleMcp", L.T("ribbon.toggle.text.stopped"),
                assemblyPath,
                "RvtMcp.Plugin.Commands.ToggleMcpCommand")
            {
                LargeImage = IconGenerator.McpOff32,
                Image = IconGenerator.McpOff16,
                ToolTip = L.T("ribbon.toggle.tooltip.stopped")
            };

            var historyData = new PushButtonData(
                "ShowHistory", L.T("ribbon.history.text", ("count", 0)),
                assemblyPath,
                "RvtMcp.Plugin.Commands.ShowHistoryCommand")
            {
                LargeImage = IconGenerator.History32,
                Image = IconGenerator.History16,
                ToolTip = L.T("ribbon.history.tooltip")
            };

            var toastEnabled = config?.EnableToastOrDefault == true;
            var toastData = new PushButtonData(
                "ToggleToast", L.T("ribbon.toast.text"),
                assemblyPath,
                "RvtMcp.Plugin.Commands.ToggleToastCommand")
            {
                LargeImage = toastEnabled ? IconGenerator.ToastOn32 : IconGenerator.ToastOff32,
                Image = toastEnabled ? IconGenerator.ToastOn16 : IconGenerator.ToastOff16,
                ToolTip = toastEnabled
                    ? L.T("ribbon.toast.tooltip.enabled")
                    : L.T("ribbon.toast.tooltip.disabled")
            };

            var stack = panel.AddStackedItems(toggleData, historyData, toastData);

            panel.AddSlideOut();
            var settingsButton = AddSettingsButton(panel, assemblyPath);
            var languageButton = AddLanguageButton(panel, assemblyPath);

            return new RibbonResult
            {
                ToggleButton = stack[0] as PushButton,
                HistoryButton = stack[1] as PushButton,
                ToastButton = stack[2] as PushButton,
                SettingsButton = settingsButton,
                LanguageButton = languageButton
            };
        }

        private static PushButton AddSettingsButton(RibbonPanel panel, string assemblyPath)
        {
            const string buttonName = "ShowSettings";
            if (CreatedButtons.Contains(buttonName)) return null;
            var data = new PushButtonData(
                buttonName,
                L.T("ribbon.settings.text"),
                assemblyPath,
                "RvtMcp.Plugin.Commands.ShowSettingsCommand")
            {
                LargeImage = IconGenerator.Settings32,
                Image = IconGenerator.Settings16,
                ToolTip = L.T("ribbon.settings.tooltip"),
                LongDescription = L.T("ribbon.settings.tooltip")
            };
            return AddPushButton(panel, data, buttonName);
        }

        private static PushButton AddLanguageButton(RibbonPanel panel, string assemblyPath)
        {
            const string buttonName = "ShowSettingsLanguage";
            if (CreatedButtons.Contains(buttonName)) return null;
            var data = new PushButtonData(
                buttonName,
                Views.Settings.InvariantLanguageText.RibbonCaption,
                assemblyPath,
                "RvtMcp.Plugin.Commands.ShowSettingsLanguageCommand")
            {
                LargeImage = IconGenerator.Language32,
                Image = IconGenerator.Language16,
                ToolTip = L.T("ribbon.language.tooltip"),
                LongDescription = L.T("ribbon.language.tooltip")
            };
            return AddPushButton(panel, data, buttonName);
        }

        private static PushButton AddPushButton(RibbonPanel panel, PushButtonData data, string buttonName)
        {
            try
            {
                var button = panel.AddItem(data) as PushButton;
                CreatedButtons.Add(buttonName);
                return button;
            }
            catch
            {
                CreatedButtons.Add(buttonName);
                return null;
            }
        }

        private static RibbonPanel ResolvePanel(UIControlledApplication application)
        {
            foreach (var panel in application.GetRibbonPanels(Tab.AddIns))
            {
                if (panel.Name == PanelName) return panel;
            }

            return application.CreateRibbonPanel(Tab.AddIns, PanelName);
        }
    }
}

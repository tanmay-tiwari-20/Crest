using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Features;

namespace Crest.UI;

public sealed class WindowsIntegrationsPanel : StackPanel
{
    private readonly WindowsIntegrationService _service;
    private readonly WindowsIntegrationPreferences _preferences;
    private readonly StackPanel _live = new();
    private readonly List<(CheckBox Control, Func<bool> Value)> _toggles = new();
    private readonly List<(Button Control, Func<bool> Enabled)> _actions = new();
    private bool _refreshing;
    public WindowsIntegrationsPanel(WindowsIntegrationService service, WindowsIntegrationPreferences preferences, Action save, Action<string> openTool, bool settings = false)
    {
        _service = service; _preferences = preferences;
        Children.Add(Design.Text("Windows integrations", 20, "Text", true));
        Children.Add(Design.Text("Windows decides which notifications and device capabilities are available. Each source is optional and independent.", 12, "Muted"));
        AddToggle("Read Windows notification previews", () => preferences.NotificationsEnabled, value => preferences.NotificationsEnabled = value, save);
        Children.Add(Design.Text("Reads app names and notification text only after Windows grants access. Previews stay in memory for up to 24 hours. Crest does not reply or remove Windows notifications. Some unpackaged Windows installations cannot grant this access.", 11, "Muted"));
        AddToggle("Show notification titles and bodies", () => preferences.ShowNotificationDetails, value => preferences.ShowNotificationDetails = value, save);
        if (settings) AddAction("Request Windows notification access", () => _ = service.RequestNotificationAccessAsync(), () => preferences.NotificationsEnabled);
        else Children.Add(Design.Text("Request permission in Settings → Integrations → Windows.", 11, "Muted"));
        AddAction("Refresh notification previews", () => _ = service.RefreshNotificationsAsync(), () => preferences.NotificationsEnabled);
        Children.Add(Design.Button("Clear Crest notification previews", service.ClearNotifications));
        AddToggle("Read paired accessory batteries", () => preferences.AccessoryBatteryEnabled, value => preferences.AccessoryBatteryEnabled = value, save);
        Children.Add(Design.Text("Reads the standard Bluetooth LE battery service on connected paired devices when you refresh. Devices that expose no value stay unavailable. Pairing stays in Windows.", 11, "Muted"));
        AddAction("Refresh accessory batteries", () => _ = service.RefreshAccessoriesAsync(), () => preferences.AccessoryBatteryEnabled);
        Children.Add(Design.Button("Open Bluetooth settings", () => openTool("ms-settings:bluetooth")));
        AddToggle("Enable Phone Link access", () => preferences.PhoneEnabled, value => preferences.PhoneEnabled = value, save);
        Children.Add(Design.Text("Uses Microsoft's installed Phone Link app. Crest stores no phone account or messages. Supported calls, messages, and pairing depend on your phone and Phone Link.", 11, "Muted"));
        AddAction("Check Phone Link support", () => _ = service.ProbePhoneAsync(), () => preferences.PhoneEnabled);
        AddAction("Open Microsoft Phone Link", () => openTool("ms-phone:"), () => preferences.PhoneEnabled && service.PhoneAvailable);
        Children.Add(Design.Button("Disconnect Windows integrations", () => { preferences.NotificationsEnabled = false; preferences.AccessoryBatteryEnabled = false; preferences.PhoneEnabled = false; preferences.ShowNotificationDetails = false; save(); service.Clear(); }));
        Children.Add(_live);
        Loaded += (_, _) => { service.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => service.Changed -= OnChanged;
        Refresh();
    }
    private void AddToggle(string label, Func<bool> value, Action<bool> update, Action save)
    {
        var toggle = new CheckBox { Content = label, IsChecked = value(), Margin = new Thickness(0, 12, 0, 6) };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => { if (_refreshing) return; update(true); save(); Refresh(); };
        toggle.Unchecked += (_, _) => { if (_refreshing) return; update(false); save(); Refresh(); };
        _toggles.Add((toggle, value));
        Children.Add(toggle);
    }
    private void AddAction(string label, Action action, Func<bool> enabled)
    {
        var button = Design.Button(label, action); _actions.Add((button, enabled)); Children.Add(button);
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Refresh));
    }
    private void Refresh()
    {
        _refreshing = true;
        foreach (var toggle in _toggles) toggle.Control.IsChecked = toggle.Value();
        foreach (var action in _actions) action.Control.IsEnabled = action.Enabled();
        _refreshing = false;
        _live.Children.Clear();
        _live.Children.Add(Design.Text(_service.NotificationStatus, 11, "Muted"));
        foreach (var item in _service.Notifications)
        {
            _live.Children.Add(Design.Text(item.App + " · " + item.CreatedUtc.ToLocalTime().ToString("g"), 12, "Text", true));
            if (_preferences.ShowNotificationDetails) _live.Children.Add(Design.Text(item.Title + "\n" + item.Body, 12));
            _live.Children.Add(Design.Button("Acknowledge Crest preview from " + item.App, () => _service.DismissNotification(item.Id)));
        }
        _live.Children.Add(Design.Text(_service.BatteryStatus, 11, "Muted"));
        foreach (var item in _service.Accessories)
            _live.Children.Add(Design.Text(item.Name + " · " + (item.Percent is int value ? value + "%" : "Unavailable") + "\n" + item.Status + " · " + item.UpdatedUtc.ToLocalTime().ToString("g"), 12));
        _live.Children.Add(Design.Text(_service.PhoneStatus, 11, "Muted"));
    }
}

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
        Children.Add(PanelLayout.Heading("Windows integrations"));
        Children.Add(Design.Text("Windows decides which notifications and device capabilities are available. Each source is optional and independent.", 12, "Muted"));
        var notifications = new StackPanel();
        AddToggle(notifications, "Read Windows notification previews", () => preferences.NotificationsEnabled, value => preferences.NotificationsEnabled = value, save);
        notifications.Children.Add(Design.Text("Reads app names and notification text only after Windows grants access. Previews stay in memory for up to 24 hours. Crest does not reply or remove Windows notifications. Some unpackaged Windows installations cannot grant this access.", 12, "Muted"));
        AddToggle(notifications, "Show notification titles and bodies", () => preferences.ShowNotificationDetails, value => preferences.ShowNotificationDetails = value, save);
        if (settings) AddAction(notifications, "Request Windows notification access", () => _ = service.RequestNotificationAccessAsync(), () => preferences.NotificationsEnabled);
        else notifications.Children.Add(Design.Text("Request permission in Settings → Integrations → Windows.", 12, "Muted"));
        AddAction(notifications, "Refresh notification previews", () => _ = service.RefreshNotificationsAsync(), () => preferences.NotificationsEnabled);
        notifications.Children.Add(Design.Button("Clear Crest notification previews", service.ClearNotifications));
        Children.Add(PanelLayout.Group("Notification previews", notifications));
        var accessories = new StackPanel();
        AddToggle(accessories, "Read paired accessory batteries", () => preferences.AccessoryBatteryEnabled, value => preferences.AccessoryBatteryEnabled = value, save);
        accessories.Children.Add(Design.Text("Reads the standard Bluetooth LE battery service on connected paired devices when you refresh. Devices that expose no value stay unavailable. Pairing stays in Windows.", 12, "Muted"));
        AddAction(accessories, "Refresh accessory batteries", () => _ = service.RefreshAccessoriesAsync(), () => preferences.AccessoryBatteryEnabled);
        accessories.Children.Add(Design.Button("Open Bluetooth settings", () => openTool("ms-settings:bluetooth")));
        Children.Add(PanelLayout.Group("Paired accessories", accessories));
        var phone = new StackPanel();
        AddToggle(phone, "Enable Phone Link access", () => preferences.PhoneEnabled, value => preferences.PhoneEnabled = value, save);
        phone.Children.Add(Design.Text("Uses Microsoft's installed Phone Link app. Crest stores no phone account or messages. Supported calls, messages, and pairing depend on your phone and Phone Link.", 12, "Muted"));
        AddAction(phone, "Check Phone Link support", () => _ = service.ProbePhoneAsync(), () => preferences.PhoneEnabled);
        AddAction(phone, "Open Microsoft Phone Link", () => openTool("ms-phone:"), () => preferences.PhoneEnabled && service.PhoneAvailable);
        Children.Add(PanelLayout.Group("Phone Link", phone));
        Children.Add(Design.Button("Disconnect Windows integrations", () => { preferences.NotificationsEnabled = false; preferences.AccessoryBatteryEnabled = false; preferences.PhoneEnabled = false; preferences.ShowNotificationDetails = false; save(); service.Clear(); }));
        Children.Add(PanelLayout.Group("Source status", _live));
        Loaded += (_, _) => { service.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => service.Changed -= OnChanged;
        PanelLayout.Refine(this);
        PanelLayout.Refine(notifications); PanelLayout.Refine(accessories); PanelLayout.Refine(phone);
        Refresh();
    }
    private void AddToggle(Panel parent, string label, Func<bool> value, Action<bool> update, Action save)
    {
        var toggle = new CheckBox { Content = label, IsChecked = value(), Margin = new Thickness(0, 12, 0, 6) };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => { if (_refreshing) return; update(true); save(); Refresh(); };
        toggle.Unchecked += (_, _) => { if (_refreshing) return; update(false); save(); Refresh(); };
        _toggles.Add((toggle, value));
        parent.Children.Add(toggle);
    }
    private void AddAction(Panel parent, string label, Action action, Func<bool> enabled)
    {
        var button = Design.Button(label, action); _actions.Add((button, enabled)); parent.Children.Add(button);
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
        PanelLayout.Refine(_live);
    }
}

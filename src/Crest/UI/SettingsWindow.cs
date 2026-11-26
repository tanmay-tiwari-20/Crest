using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace Crest.UI;

public sealed partial class SettingsWindow : Window
{
    private const string SaveStatus = "Changes are saved automatically.";
    private readonly App _app;
    private readonly StackPanel _page = new();
    private readonly StackPanel _nav = new();
    private readonly TextBlock _message = Design.Text(SaveStatus, 12, "Muted");
    private readonly ScrollViewer _pageScroll;
    private readonly Button _close;
    private string _section = "Overview";
    private TextBlock? _hardwareStatus;
    private WindowState _restoreState = WindowState.Normal;
    private static readonly string[] Sections = { "Overview", "Appearance", "Behavior", "Modules", "Shortcuts", "Workspaces", "Privacy", "Activities", "Integrations", "About" };
    private TimerToolsPanel? _activities;

    public SettingsWindow(App app)
    {
        _app = app;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Crest;component/UI/SettingsControls.xaml") });
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _restoreState = WindowState;
            if (WindowState == WindowState.Normal) FitToDisplay();
        };
        SourceInitialized += (_, _) => FitToDisplay();
        Title = "Crest · Settings";
        Width = 1000;
        Height = 760;
        MinWidth = 700;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface");
        var layout = new Grid();
        layout.SetResourceReference(Panel.BackgroundProperty, "Surface");
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(204) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Content = layout;
        layout.Children.Add(BuildSidebar());

        var content = new Grid { MaxWidth = 1000 };
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _page.Margin = new Thickness(28, 20, 20, 14);
        _pageScroll = new ScrollViewer { Content = _page, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
        AutomationProperties.SetName(_pageScroll, "Settings page");
        content.Children.Add(_pageScroll);
        _close = Design.Button("Done", () =>
        {
            if (!_app.Preferences.OnboardingComplete)
            {
                _app.Preferences.OnboardingComplete = true;
                _app.SavePreferences();
            }
            Close();
        }, true);
        var footer = BuildFooter();
        Grid.SetRow(footer, 1);
        content.Children.Add(footer);
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        BuildNav();
        BuildPage();
    }

    public void ShowAndActivate()
    {
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Show();
        Activate();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(() => FitToDisplay(newDpi.DpiScaleX)));
    }

    private void FitToDisplay(double? dpiScale = null)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        double scale = dpiScale ?? (HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformToDevice.M11 ?? 1);
        var workArea = Forms.Screen.FromHandle(handle).WorkingArea;
        double availableWidth = Math.Max(1, workArea.Width / scale - 16);
        double availableHeight = Math.Max(1, workArea.Height / scale - 16);
        MinWidth = Math.Min(700, availableWidth);
        MinHeight = Math.Min(560, availableHeight);
        if (WindowState == WindowState.Normal)
        {
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
        }
    }
    private Border BuildSidebar()
    {
        var sidebar = new Grid { Margin = new Thickness(12, 16, 12, 12) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition());
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 0, 12) };
        var mark = CrestBrand.Mark(34, "Accent");
        mark.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(mark);
        var wordmark = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
        wordmark.Children.Add(Design.Text("crest", 24, "Text", true));
        wordmark.Children.Add(Design.Text("Settings", 12, "Muted"));
        brand.Children.Add(wordmark);
        sidebar.Children.Add(brand);

        _nav.Margin = new Thickness(4, 4, 5, 4);
        var scroll = new ScrollViewer { Content = _nav, Focusable = false };
        AutomationProperties.SetName(scroll, "Settings navigation");
        Grid.SetRow(scroll, 1);
        sidebar.Children.Add(scroll);

        var version = Design.Text("Version " + App.AppVersion, 12, "Muted");
        var bottom = new Border { Child = version, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 8, 8, 0), Padding = new Thickness(1, 10, 0, 0) };
        bottom.SetResourceReference(Border.BorderBrushProperty, "Divider");
        Grid.SetRow(bottom, 2);
        sidebar.Children.Add(bottom);

        var surface = new Border { Child = sidebar, BorderThickness = new Thickness(0, 0, 1, 0) };
        surface.SetResourceReference(Border.BackgroundProperty, "Inset");
        surface.SetResourceReference(Border.BorderBrushProperty, "Divider");
        return surface;
    }

    private Border BuildFooter()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.Margin = new Thickness(0, 0, 12, 0);
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        grid.Children.Add(_message);
        _close.MinWidth = 88;
        _close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_close, 1);
        grid.Children.Add(_close);
        var footer = new Border { Child = grid, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 10, 28, 12) };
        footer.SetResourceReference(Border.BorderBrushProperty, "Divider");
        return footer;
    }

    private void BuildNav()
    {
        // Keep navigation controls alive when changing pages so keyboard focus
        // and the user's position in the tab order survive activation.
        if (_nav.Children.Count == 0)
        {
            foreach (var section in Sections)
            {
                var button = Design.Button(section, () => ShowSection(section));
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = Design.Icon(NavGlyph(section), 16);
                icon.Width = 22;
                icon.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(icon);
                var text = Design.Text(section, 13);
                text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
                text.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { Source = button });
                row.Children.Add(text);
                button.Content = row;
                button.Margin = new Thickness(0, section is "Shortcuts" or "About" ? 8 : 0, 0, 2);
                button.Padding = new Thickness(9, 5, 10, 5);
                button.MinHeight = 34;
                _nav.Children.Add(button);
            }
        }
        for (int index = 0; index < Sections.Length; index++)
        {
            var button = (Button)_nav.Children[index];
            bool selected = Sections[index] == _section;
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Inset");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Text" : "Muted");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Inset");
            button.BorderThickness = new Thickness(1);
            var icon = (TextBlock)((StackPanel)button.Content).Children[0];
            icon.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Accent" : "Muted");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        }
    }
    private static string NavGlyph(string section) => section switch
    {
        "Overview" => "\uE80F", "Appearance" => "\uE790", "Behavior" => "\uE7C4",
        "Modules" => "\uECA5", "Shortcuts" => "\uE8A7", "Workspaces" => "\uE8F1",
        "Privacy" => "\uE72E", "Activities" => "\uE823", "Integrations" => "\uE774", _ => "\uE946"
    };
    private void BuildPage(bool restoreView = true)
    {
        var focused = Keyboard.FocusedElement as Control;
        string? focusName = restoreView && focused != null && focused.IsDescendantOf(_page) ? AutomationProperties.GetName(focused) : null;
        double offset = restoreView ? _pageScroll.VerticalOffset : 0;
        _activities = null;
        _integrations = null;
        _hardwareStatus = null;
        _resetDisplay = null;
        _page.Children.Clear();
        string closeLabel = _app.Preferences.OnboardingComplete ? "Done" : "Try Crest";
        _close.Content = closeLabel;
        AutomationProperties.SetName(_close, closeLabel);
        switch (_section)
        {
            case "Overview":
                Overview();
                break;
            case "Appearance":
                Appearance();
                break;
            case "Behavior":
                Behavior();
                break;
            case "Modules":
                Modules();
                break;
            case "Shortcuts":
                Shortcuts();
                break;
            case "About":
                About();
                break;
            case "Workspaces": Workspaces(); break;
            case "Privacy": Privacy(); break;
            case "Integrations": Integrations(); break;
            case "Activities":
                Heading("Activities", "Manage timers, focus sessions, and local reminders. Due notices stay here until you act on them.");
                _activities = new TimerToolsPanel(_app.Local, SetStatus);
                _page.Children.Add(_activities); break;
        }
        _pageScroll.ScrollToVerticalOffset(offset);
        if (!string.IsNullOrEmpty(focusName))
        {
            var heading = _page.Children[0];
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_page.Children.Count == 0 || !ReferenceEquals(_page.Children[0], heading)) return;
                if (Keyboard.FocusedElement is Control current && (current.IsDescendantOf(_nav) || current.IsDescendantOf(_page))) return;
                var destination = FindPageControl(_page, focusName) ?? FindPageControl(_page, "Preference display");
                (destination ?? (Button)_nav.Children[Array.IndexOf(Sections, _section)]).Focus();
            }));
        }
    }

    private static Control? FindPageControl(DependencyObject element, string name)
    {
        if (element is Control control && control.Focusable && control.IsEnabled && AutomationProperties.GetName(control) == name) return control;
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            if (FindPageControl(child, name) is { } match) return match;
        return null;
    }
    private void Heading(string title, string description)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var heading = Design.Text(title, 32, "Text", true);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        header.Children.Add(heading);
        var subtitle = Design.Text(description, 14, "Muted");
        subtitle.Margin = new Thickness(0, 5, 0, 0);
        header.Children.Add(subtitle);
        _page.Children.Add(header);
    }

    private Border Section(string title, string? detail = null, UIElement? control = null)
    {
        var stack = new StackPanel();
        var heading = Design.Text(title, 17, "Text", true);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        stack.Children.Add(heading);
        if (!string.IsNullOrEmpty(detail))
        {
            var description = Design.Text(detail, 13, "Muted");
            description.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(description);
        }
        if (control != null)
        {
            stack.Children.Add(new Border { Height = 12 });
            stack.Children.Add(control);
        }
        var card = Design.Card(stack, new Thickness(16, 14, 16, 16));
        card.Margin = new Thickness(0, 0, 0, 12);
        _page.Children.Add(card);
        return card;
    }
    private void Overview()
    {
        Heading("Overview", "Set up the island and choose the tools you want at the top of your screen.");
        if (_app.Store.RecoveredCorruptSettings)
            Section("Preferences recovered", "An unreadable settings file was replaced with safe defaults.");

        var preview = new Border { MinHeight = 76, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = SurfacePreview() };
        preview.SetResourceReference(Border.BackgroundProperty, "Inset");
        preview.SetResourceReference(Border.BorderBrushProperty, "Divider");
        string edge = _app.EffectivePreferences.FullWidthTrigger ? "top edge" : "top-center edge";
        string firstStep = _app.Preferences.AllowOverApps
            ? "Close Settings and move your pointer to the " + edge + " of your selected display."
            : "Close Settings, press Win+D, then move your pointer to the " + edge + " of your selected display.";
        if (_app.Preferences.AccessibilityShortcut) firstStep += " You can also press Ctrl+Alt+Space.";
        Section("Open the island", firstStep + " Click the island for the current activity, or choose Tools for all enabled modules.", preview);

        var links = new StackPanel();
        string visibility = _app.Preferences.AllowOverApps ? "Over other apps" : _app.Preferences.StrictDesktop ? "Strict desktop" : "Desktop active";
        AddSetting(links, "Island visibility", visibility, LinkTo("Behavior", "Edit behavior"));
        int moduleCount = _app.Preferences.EnabledModules.Count;
        AddSetting(links, "Enabled modules", $"{moduleCount} of {ModuleCatalog.All.Length} tools available on the island", LinkTo("Modules", "Manage modules"));
        AddSetting(links, "Integrations", "Weather, calendars, and optional sources", LinkTo("Integrations", "Manage integrations"));
        Section("Quick settings", control: links);

        _hardwareStatus = Design.Text(HardwareStatus(), 13, "Muted");
        Section("Device controls", control: _hardwareStatus);
    }

    private Button LinkTo(string section, string label)
    {
        var link = Design.Button(label, () =>
        {
            ShowSection(section);
            ((Button)_nav.Children[Array.IndexOf(Sections, section)]).Focus();
        });
        return link;
    }

    private static void AddSetting(Panel group, string title, string? detail, UIElement? control = null)
    {
        var row = new Grid { Margin = new Thickness(0, group.Children.Count == 0 ? 0 : 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
        label.Children.Add(Design.Text(title, 13, "Text", true));
        if (!string.IsNullOrEmpty(detail))
        {
            var description = Design.Text(detail, 12, "Muted");
            description.Margin = new Thickness(0, 2, 0, 0);
            label.Children.Add(description);
        }
        row.Children.Add(label);
        if (control != null)
        {
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
        }
        group.Children.Add(row);
    }

    private static Border SurfacePreview()
    {
        var pill = new Border { Width = 186, Height = 48, CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        pill.SetResourceReference(Border.BackgroundProperty, "IslandShell");
        pill.SetResourceReference(Border.BorderBrushProperty, "IslandOutline");
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var mark = CrestBrand.Mark(20, "IslandAccent");
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(mark);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
        meta.Children.Add(Design.Text("crest", 13, "IslandText", true));
        meta.Children.Add(Design.Text("Idle", 11, "IslandMuted"));
        Grid.SetColumn(meta, 1);
        content.Children.Add(meta);
        var expand = Design.Icon("\uE70D", 17);
        expand.SetResourceReference(TextBlock.ForegroundProperty, "IslandMuted");
        Grid.SetColumn(expand, 2);
        content.Children.Add(expand);
        pill.Child = content;
        AutomationProperties.SetName(pill, "Crest idle island appearance preview");
        return pill;
    }

    private ComboBox Choice(string name, string[] items, string selected, Action<string> changed, bool save = true)
    {
        var c = new ComboBox { ItemsSource = items, SelectedItem = selected, MinWidth = 158, MaxWidth = 196 };
        AutomationProperties.SetName(c, name);
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is not string selection) return;
            changed(selection);
            if (save) Save();
        };
        return c;
    }

    private CheckBox Toggle(string text, bool value, Action<bool> change, bool save = true)
    {
        var c = new CheckBox { IsChecked = value };
        c.SetResourceReference(StyleProperty, "SettingsSwitch");
        AutomationProperties.SetName(c, text);
        c.Click += (_, _) =>
        {
            change(c.IsChecked == true);
            if (save) Save();
        };
        return c;
    }
    private void Save()
    {
        _app.SavePreferences();
        SetStatus(_app.Store.LastError ?? "Changes saved.");
    }

    private void SetStatus(string message)
    {
        if (_message.Text == message) return;
        _message.Text = message;
        UIElementAutomationPeer.FromElement(_message)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private string HardwareStatus() => (_app.Hardware.AudioAvailable ? "Audio ready" : "Audio unavailable") + " · " + (_app.Hardware.BrightnessAvailable ? "Brightness ready" : "Brightness unavailable");
    public void RefreshCapabilities()
    {
        if (_hardwareStatus != null)
            _hardwareStatus.Text = HardwareStatus();
    }
    public void RefreshActivities() => _activities?.Refresh();
    public void ShowSection(string section)
    {
        if (!Sections.Contains(section) || _section == section) return;
        _section = section;
        SetStatus(_app.Store.LastError ?? SaveStatus);
        BuildNav();
        BuildPage(restoreView: false);
        ((Button)_nav.Children[Array.IndexOf(Sections, section)]).BringIntoView();
    }
    private void Appearance()
    {
        Heading("Appearance", "Choose a palette, island size, and motion for each display.");
        Section("Display preferences", control: PreferenceScope(compact: true));
        Section("Color theme", "Applies to Settings and the island accent.", new ThemePicker(Scoped.Theme, value =>
        {
            ChangeScoped(s => s.Theme = value);
            Save();
        }));

        var size = new StackPanel();
        AddSetting(size, "Size preset", null, Choice("Island size preset", new[] { "Compact", "Comfortable", "Large" },
            Scoped.SizeScale < .98 ? "Compact" : Scoped.SizeScale > 1.1 ? "Large" : "Comfortable", value =>
            {
                ChangeScoped(s => s.SizeScale = value == "Compact" ? .9 : value == "Large" ? 1.2 : 1);
                BuildPage();
            }));
        AddSetting(size, "Island size", "Fine-tune the preset.", Range("Island size", 85, 130, Scoped.SizeScale * 100, v => ChangeScoped(s => s.SizeScale = v / 100), "%"));
        Section("Size", control: size);

        var motion = new StackPanel();
        AddSetting(motion, "Motion preference", "System follows the Windows animation setting. Reduced uses a short fade.",
            Choice("Motion preference", new[] { "System", "Normal", "Reduced" }, Scoped.Motion, value => ChangeScoped(s => s.Motion = value)));
        AddSetting(motion, "Motion speed", "Applies to reveal and dismissal.", Range("Motion speed", 50, 200, Scoped.MotionSpeed * 100, v => ChangeScoped(s => s.MotionSpeed = v / 100), "%"));
        Section("Motion", control: motion);
    }

    private Grid Range(string title, double min, double max, double value, Action<double> change, string unit)
    {
        var row = new Grid { Width = 158 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var label = Design.Text(((int)Math.Round(value)) + unit, 12, "Accent", true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(label, 1);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, _) => { label.Text = ((int)slider.Value) + unit; change(slider.Value); };
        slider.PreviewMouseLeftButtonUp += (_, _) => Save();
        slider.PreviewKeyUp += (_, _) => Save();
        row.Children.Add(slider);
        row.Children.Add(label);
        return row;
    }

    private void Behavior()
    {
        Heading("Behavior", "Choose where Crest opens and how your pointer or keyboard reveals it.");
        var visibility = new StackPanel();
        var strictDesktop = Toggle("Use strict desktop mode (recommended)", _app.Preferences.StrictDesktop, v => _app.Preferences.StrictDesktop = v);
        strictDesktop.IsEnabled = !_app.Preferences.AllowOverApps;
        var appAccess = Toggle("Allow Crest over other apps", _app.Preferences.AllowOverApps, enabled =>
        {
            _app.Preferences.AllowOverApps = enabled;
            strictDesktop.IsEnabled = !enabled;
        });
        AddSetting(visibility, "Allow Crest over other apps", "Reveal the island above the active app. Crest stays hidden during Settings, Pause, screen lock, and sleep.", appAccess);
        AddSetting(visibility, "Strict desktop mode", "When app access is off, require the desktop to be active and apps on the selected display to be minimized.", strictDesktop);
        Section("Visibility", control: visibility);

        var displays = Forms.Screen.AllScreens;
        var displayOptions = displays.Select(screen => new
        {
            screen.DeviceName,
            Label = DisplayLabel(screen)
        }).ToArray();
        string? activeDisplay = displays.FirstOrDefault(s => s.DeviceName == _app.Preferences.SelectedDisplay)?.DeviceName ?? Forms.Screen.PrimaryScreen?.DeviceName;
        var displayChoice = new ComboBox { ItemsSource = displayOptions, DisplayMemberPath = "Label", SelectedValuePath = "DeviceName", SelectedValue = activeDisplay, MinWidth = 158, MaxWidth = 196 };
        AutomationProperties.SetName(displayChoice, "Selected display");
        displayChoice.SelectionChanged += (_, _) =>
        {
            if (displayChoice.SelectedValue is not string deviceName) return;
            _app.Preferences.SelectedDisplay = deviceName;
            Save();
        };
        var display = new StackPanel();
        AddSetting(display, "Island display", "If this display disconnects, Crest returns to the primary display on your next hover.", displayChoice);
        Section("Display", control: display);

        var trigger = PreferenceScope();
        var width = Range("Trigger width", 80, 800, Scoped.TriggerWidth, v => ChangeScoped(s => s.TriggerWidth = (int)v), " DIP");
        width.IsEnabled = !Scoped.FullWidthTrigger;
        AddSetting(trigger, "Full-width top edge", "Use the entire edge to reveal Crest.", Toggle("Use the full-width top edge", Scoped.FullWidthTrigger, v =>
        {
            ChangeScoped(s => s.FullWidthTrigger = v);
            width.IsEnabled = !v;
        }));
        AddSetting(trigger, "Hover delay", "How long to stay at the edge before Crest opens.", Range("Hover delay", 80, 500, Scoped.DwellMs, v => ChangeScoped(s => s.DwellMs = (int)v), " ms"));
        AddSetting(trigger, "Pointer grace", "Time to return before the island closes.", Range("Pointer grace", 100, 750, Scoped.GraceMs, v => ChangeScoped(s => s.GraceMs = (int)v), " ms"));
        AddSetting(trigger, "Trigger width", "Width of the centered target.", width);
        AddSetting(trigger, "Trigger depth", "Distance down from the top edge.", Range("Trigger depth", 1, 12, Scoped.TriggerDepth, v => ChangeScoped(s => s.TriggerDepth = (int)v), " DIP"));
        Section("Pointer trigger", control: trigger);

        var gestures = new StackPanel();
        AddSetting(gestures, "Double-click", null, Choice("Double-click gesture", ModuleCatalog.Gestures, _app.Preferences.DoubleClickGesture, value => _app.Preferences.DoubleClickGesture = value));
        AddSetting(gestures, "Middle-click", null, Choice("Middle-click gesture", ModuleCatalog.Gestures, _app.Preferences.MiddleClickGesture, value => _app.Preferences.MiddleClickGesture = value));
        AddSetting(gestures, "Pointer wheel", null, Choice("Pointer wheel gesture", new[] { "None", "Switch modules" }, _app.Preferences.WheelGesture, value => _app.Preferences.WheelGesture = value));
        Section("Gestures", "These actions apply on an empty part of the island.", gestures);

        var startupAndKeyboard = new StackPanel();
        var startup = Toggle("Start Crest with Windows", _app.Preferences.StartWithWindows, enabled =>
        {
            try { _app.SetStartup(enabled); SetStatus(_app.Store.LastError ?? "Startup preference saved."); }
            catch { SetStatus("Windows could not change this startup preference."); }
        }, save: false);
        startup.Click += (_, _) => startup.IsChecked = _app.Preferences.StartWithWindows;
        AddSetting(startupAndKeyboard, "Start with Windows", "Open Crest in the system tray at sign-in.", startup);
        AddSetting(startupAndKeyboard, "Keyboard reveal", "Press Ctrl+Alt+Space to show Crest, Tab to navigate, and Escape to dismiss.", Toggle("Enable the accessibility reveal shortcut", _app.Preferences.AccessibilityShortcut, v => _app.Preferences.AccessibilityShortcut = v));
        Section("Startup and keyboard", control: startupAndKeyboard);
    }

    private void Modules()
    {
        Heading("Modules", "Choose which tools appear on the island and arrange their order.");
        ModuleList();
        var timerSound = new StackPanel();
        AddSetting(timerSound, "Play a completion sound", "The chime can play while the island is hidden.", Toggle("Play a sound for timer completion", _app.Preferences.TimerSound, v => _app.Preferences.TimerSound = v));
        var testSound = Design.Button("Test timer sound", () =>
        {
            _app.PreviewTimerSound();
            SetStatus("Playing Crest's timer chime. If it is silent, check your output and Crest volume in Windows Volume Mixer.");
        });
        testSound.HorizontalAlignment = HorizontalAlignment.Left;
        testSound.Margin = new Thickness(0, 8, 0, 0);
        timerSound.Children.Add(testSound);
        Section("Timer sound", control: timerSound);
    }
    private void Shortcuts()
    {
        Heading("Shortcuts", "Keep your favorite apps, folders, files, and websites in the Launch module.");
        var actions = new WrapPanel();
        actions.Children.Add(Design.Button("Add app or file", AddFile));
        actions.Children.Add(Design.Button("Add folder", AddFolder));
        PanelLayout.SpaceActions(actions);
        Section("Add from this PC", control: actions);
        var form = new StackPanel();
        var name = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(name, "Website shortcut name");
        form.Children.Add(Design.Text("Name", 12, "Muted"));
        form.Children.Add(name);
        var url = new TextBox { Text = "https://", Margin = new Thickness(0, 4, 0, 12) };
        AutomationProperties.SetName(url, "Website URL");
        form.Children.Add(Design.Text("Website address", 12, "Muted"));
        form.Children.Add(url);
        var add = Design.Button("Add website", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || !Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            {
                SetStatus("Enter a name and a valid https:// or http:// address.");
                return;
            }
            Add(name.Text.Trim(), uri.AbsoluteUri, "Website");
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        form.Children.Add(add);
        Section("Add a website", control: form);

        var shortcuts = _app.Preferences.Launchers;
        if (shortcuts.Count == 0)
        {
            Section("No saved shortcuts", "Items you add here appear in the Launch module.");
            return;
        }
        var saved = new StackPanel();
        foreach (var entry in shortcuts)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var remove = Design.Button("Remove", () =>
            {
                shortcuts.Remove(entry);
                Save();
                BuildPage();
            });
            AutomationProperties.SetName(remove, "Remove shortcut " + entry.Name);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            label.Children.Add(Design.Text(entry.Name, 13, "Text", true));
            var path = Design.Text(entry.Target, 12, "Muted");
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.TextWrapping = TextWrapping.NoWrap;
            path.ToolTip = entry.Target;
            label.Children.Add(path);
            row.Children.Add(label);
            saved.Children.Add(row);
        }
        Section("Saved shortcuts", control: saved);
    }
    private void AddFile()
    {
        var dialog = new OpenFileDialog { Title = "Choose an app, file, or shortcut", CheckFileExists = true, Filter = "Apps and shortcuts|*.exe;*.lnk;*.url|All files|*.*" };
        if (dialog.ShowDialog(this) == true)
            Add(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, "App");
    }
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder" };
        if (dialog.ShowDialog(this) == true)
            Add(Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar)), dialog.FolderName, "Folder");
    }
    private void Add(string name, string target, string kind)
    {
        if (_app.Preferences.Launchers.Count >= 100)
        {
            SetStatus("You can keep up to 100 shortcuts.");
            return;
        }
        _app.Preferences.Launchers.Add(new LauncherEntry { Name = name, Target = target, Kind = kind });
        Save();
        BuildPage();
    }
    private void About()
    {
        Heading("About Crest", "Version " + App.AppVersion + " for Windows 11 (x64)");
        var hardware = new StackPanel();
        AddSetting(hardware, "Audio", _app.Hardware.AudioStatus);
        AddSetting(hardware, "Brightness", _app.Hardware.BrightnessStatus);
        AddSetting(hardware, "Battery", _app.Hardware.Battery.Status);
        AddSetting(hardware, "Music", _app.Hardware.Music.Status);
        Section("Device support", control: hardware);
        Section("Using the tray", "Double-click the tray icon to open Settings. Right-click it to pause or resume the island, or quit Crest.");
        Section("Uninstall", "Use Installed apps in Windows Settings. The uninstaller lets you keep or remove your preferences. To remove a portable copy, quit Crest before deleting its folder.");

        var reset = Design.Button("Reset Crest data", () =>
        {
            if (!CrestDialog.Confirm(this, "Reset Crest data", "Reset Crest preferences, integrations, shortcuts, workspaces, activities, clipboard history, saved file references, and Windows startup? Your original files will stay in place.", "Reset Crest data")) return;
            try
            {
                _app.ResetData();
                _preferenceDisplay = "";
                BuildPage();
                SetStatus(_app.Store.LastError ?? "Crest data was reset.");
            }
            catch { SetStatus("Some data could not be reset. Please try again."); }
        });
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.SetResourceReference(ForegroundProperty, "Danger");
        Section("Reset data", "Remove Crest's saved settings and data. Your original files stay in place.", reset);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Crest.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace Crest.UI;

public sealed class SettingsWindow : Window
{
    private readonly App _app; private readonly StackPanel _page = new(); private readonly StackPanel _nav = new(); private string _section = "Overview";
    private readonly TextBlock _message = Design.Text("", 11, "Muted"); private TextBlock? _hardwareStatus;
    private WindowState _restoreState = WindowState.Normal;
    private static readonly string[] Sections = { "Overview", "Appearance", "Behavior", "Modules", "Shortcuts", "About" };
    public SettingsWindow(App app)
    {
        _app = app;
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) _restoreState = WindowState;
            if (WindowState == WindowState.Normal) FitToDisplay();
        };
        SourceInitialized += (_, _) => FitToDisplay();
        Title = "Crest · Settings";
        Width = 880;
        Height = 700;
        MinWidth = 700;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface");
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        Content = layout;
        var sidebar = new Grid { Margin = new Thickness(22, 26, 18, 22) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(84) });
        sidebar.RowDefinitions.Add(new RowDefinition());
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Height = 40, VerticalAlignment = VerticalAlignment.Top };
        var image = new Image { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        try
        {
            image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/crest.png"));
        }
        catch { image.Source = null; }
        logo.Children.Add(image);
        logo.Children.Add(Design.Text("crest", 26, "Text", true));
        sidebar.Children.Add(logo);
        Grid.SetRow(_nav, 1);
        sidebar.Children.Add(_nav);
        var bottom = new StackPanel();
        bottom.Children.Add(Design.Text("MADE FOR YOUR DESKTOP", 9, "Muted", true));
        bottom.Children.Add(Design.Text("Local. Quiet. Yours.", 11, "Muted"));
        Grid.SetRow(bottom, 2);
        sidebar.Children.Add(bottom);
        layout.Children.Add(sidebar);
        var content = new Grid { Margin = new Thickness(28, 30, 32, 22) };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        var eyebrow = Design.Text("PERSONAL DESKTOP UTILITY  /  0.1.0", 10, "Muted", true);
        content.Children.Add(eyebrow);
        var scroll = new ScrollViewer { Content = _page };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_message);
        _message.VerticalAlignment = VerticalAlignment.Center;
        var tryIt = Design.Button("Close and try Crest  ↗", () => { _app.Preferences.OnboardingComplete = true; _app.SavePreferences(); Close(); }, true);
        tryIt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tryIt, 1);
        footer.Children.Add(tryIt);
        Grid.SetRow(footer, 2);
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
    private void BuildNav()
    {
        _nav.Children.Clear();
        foreach (var section in Sections)
        {
            var b = Design.Button(section, () => { _section = section; BuildNav(); BuildPage(); });
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 0, 7);
            b.Padding = new Thickness(14, 11, 14, 11);
            b.SetResourceReference(Control.BackgroundProperty, section == _section ? "Raised" : "Surface");
            if (section == _section)
                b.SetResourceReference(Control.ForegroundProperty, "Accent");
            _nav.Children.Add(b);
        }
    }
    private void BuildPage()
    {
        _page.Children.Clear();
        _message.Text = "Changes are saved automatically.";
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
        }
    }
    private void Heading(string title, string description)
    {
        _page.Children.Add(Design.Text(title, 30, "Text", true));
        var p = Design.Text(description, 13, "Muted");
        p.Margin = new Thickness(0, 8, 0, 22);
        _page.Children.Add(p);
    }
    private Border Section(string title, string detail, UIElement? control = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(Design.Text(title, 14, "Text", true));
        var p = Design.Text(detail, 12, "Muted");
        p.Margin = new Thickness(0, 6, 0, control == null ? 0 : 14);
        stack.Children.Add(p);
        if (control != null)
            stack.Children.Add(control);
        var card = Design.Card(stack, new Thickness(18));
        card.Margin = new Thickness(0, 0, 0, 12);
        _page.Children.Add(card);
        return card;
    }
    private void Overview()
    {
        Heading("A little space.\nA lot within reach.", "Your music, laptop controls, and everyday tools.\nHere when you need them, hidden when you don't.");
        var steps = new StackPanel();
        steps.Children.Add(_app.Preferences.AllowOverApps
            ? Step("01", "Keep your app open", "Close Settings. Crest can open over the app you're using on your selected display.")
            : Step("01", "Return to your desktop", "Close Settings, then press Win+D. In strict mode, minimize any remaining visible apps on Crest's display."));
        steps.Children.Add(Step("02", "Meet Crest at the top", "Move your pointer away from the top edge, then enter the top-center edge and pause briefly."));
        steps.Children.Add(Step("03", "Make a little room", "Expand a module. Move away when you're finished."));
        Section("READY WHEN YOU ARE", "Crest starts quietly in your system tray.", steps);
        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.Children.Add(Status("VISIBILITY", _app.Preferences.AllowOverApps ? "Over other apps" : _app.Preferences.StrictDesktop ? "Strict desktop" : "Desktop active"));
        var hw = Status("LAPTOP CONTROLS", HardwareStatus(), t => _hardwareStatus = t);
        Grid.SetColumn(hw, 1);
        status.Children.Add(hw);
        _page.Children.Add(status);
        var note = Design.Text(_app.Preferences.AllowOverApps
            ? "App access is enabled. Enter the top edge to open Crest while you work. Timer completions still wait for your next hover."
            : "Crest stays hidden while an app or a Windows panel is active. Enable app access in Behavior to open it over other apps. Timer completions wait for your next hover.", 12, "Muted");
        note.Margin = new Thickness(0, 20, 0, 10);
        _page.Children.Add(note);
        if (_app.Store.RecoveredCorruptSettings)
            Section("Preferences recovered", "An unreadable settings file was replaced with safe defaults.");
    }
    private static UIElement Step(string number, string title, string detail)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(Design.Text(number, 12, "Accent", true));
        var s = new StackPanel();
        s.Children.Add(Design.Text(title, 13, "Text", true));
        s.Children.Add(Design.Text(detail, 12, "Muted"));
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        return g;
    }
    private static UIElement Status(string label, string value, Action<TextBlock>? capture = null)
    {
        var s = new StackPanel();
        s.Children.Add(Design.Text(label, 9, "Muted", true));
        var result = Design.Text(value, 12, "Text", true);
        s.Children.Add(result);
        capture?.Invoke(result);
        return s;
    }
    private ComboBox Choice(string name, string[] items, string selected, Action<string> changed)
    {
        var c = new ComboBox { ItemsSource = items, SelectedItem = selected };
        AutomationProperties.SetName(c, name);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s) { changed(s); Save(); } };
        return c;
    }
    private CheckBox Toggle(string text, bool value, Action<bool> change)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        AutomationProperties.SetName(c, text);
        c.Click += (_, _) => { change(c.IsChecked == true); Save(); };
        return c;
    }
    private void Save()
    {
        _app.SavePreferences();
        _message.Text = _app.Store.LastError ?? "Saved. Applies to your next hover.";
    }
    private string HardwareStatus() => (_app.Hardware.AudioAvailable ? "Audio ready" : "Audio unavailable") + " · " + (_app.Hardware.BrightnessAvailable ? "Brightness ready" : "Brightness unavailable");
    public void RefreshCapabilities()
    {
        if (_hardwareStatus != null)
            _hardwareStatus.Text = HardwareStatus();
    }
    private void Appearance()
    {
        Heading("Make yourself at home.", "A quiet surface, in the style that feels right to you.");
        Section("Color theme", "Follow Windows, or choose a light or dark surface.", Choice("Color theme", new[] { "System", "Dark", "Light" }, _app.Preferences.Theme, s => _app.Preferences.Theme = s));
        Section("Motion", "Follow Windows animation preferences. Reduced motion uses a short fade.", Choice("Motion preference", new[] { "System", "Normal", "Reduced" }, _app.Preferences.Motion, s => _app.Preferences.Motion = s));
        Range("Island size", "Keep controls comfortable at your display's scaling.", 90, 115, _app.Preferences.SizeScale * 100, v => _app.Preferences.SizeScale = v / 100, "%");
        Range("Motion speed", "Adjust reveal and dismissal together.", 50, 160, _app.Preferences.MotionSpeed * 100, v => _app.Preferences.MotionSpeed = v / 100, "%");
    }
    private void Range(string title, string detail, double min, double max, double value, Action<double> change, string unit)
    {
        var s = new StackPanel();
        var label = Design.Text(((int)value) + unit, 12, "Accent", true);
        s.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        AutomationProperties.SetName(slider, title);
        slider.ValueChanged += (_, _) => { label.Text = ((int)slider.Value) + unit; change(slider.Value); };
        slider.PreviewMouseLeftButtonUp += (_, _) => Save();
        slider.PreviewKeyUp += (_, _) => Save();
        s.Children.Add(slider);
        Section(title, detail, s);
    }
    private void Behavior()
    {
        Heading("Choose where Crest opens.", "Desktop-only by default. Enable app access when you want Crest within reach while you work.");
        var strictDesktop = Toggle("Use strict desktop mode (recommended)", _app.Preferences.StrictDesktop, v => _app.Preferences.StrictDesktop = v);
        strictDesktop.IsEnabled = !_app.Preferences.AllowOverApps;
        var appAccess = Toggle("Allow Crest over other apps", _app.Preferences.AllowOverApps, enabled =>
        {
            _app.Preferences.AllowOverApps = enabled;
            strictDesktop.IsEnabled = !enabled;
        });
        Section("App access", "Open Crest from the top edge of your selected display even while another app is active. This is off by default. Settings, Pause, screen lock, and sleep still hide Crest.", appAccess);
        Section("Desktop visibility", "When app access is off, strict mode requires the wallpaper desktop to be active and apps on your selected display to be minimized. Desktop-active mode permits visible apps after you click the desktop. Your choice is retained while app access is enabled.", strictDesktop);
        var displays = Forms.Screen.AllScreens;
        var displayChoice = new ComboBox { ItemsSource = displays, DisplayMemberPath = "DeviceName", SelectedItem = displays.FirstOrDefault(s => s.DeviceName == _app.Preferences.SelectedDisplay) ?? Forms.Screen.PrimaryScreen };
        AutomationProperties.SetName(displayChoice, "Selected display");
        displayChoice.SelectionChanged += (_, _) => { if (displayChoice.SelectedItem is Forms.Screen screen) { _app.Preferences.SelectedDisplay = screen.DeviceName; Save(); } };
        Section("Display", "Crest uses one display. If it disconnects, Crest returns to the primary display and waits for a fresh hover.", displayChoice);
        Section("Trigger coverage", "By default, the trigger is a small area at the top-center edge.", Toggle("Use the full-width top edge", _app.Preferences.FullWidthTrigger, v => _app.Preferences.FullWidthTrigger = v));
        Range("Hover delay", "How long your pointer stays at the edge before Crest opens.", 80, 500, _app.Preferences.DwellMs, v => _app.Preferences.DwellMs = (int)v, " ms");
        Range("Pointer grace", "A little time to return before the island closes.", 100, 750, _app.Preferences.GraceMs, v => _app.Preferences.GraceMs = (int)v, " ms");
        Range("Trigger width", "Width of the centered top-edge trigger.", 120, 640, _app.Preferences.TriggerWidth, v => _app.Preferences.TriggerWidth = (int)v, " DIP");
        Range("Trigger depth", "Depth measured down from the top edge.", 2, 12, _app.Preferences.TriggerDepth, v => _app.Preferences.TriggerDepth = (int)v, " DIP");
        var startup = new CheckBox { Content = "Start Crest with Windows", IsChecked = _app.Preferences.StartWithWindows };
        startup.Click += (_, _) => { try { _app.SetStartup(startup.IsChecked == true); _message.Text = "Startup preference saved."; } catch { startup.IsChecked = _app.Preferences.StartWithWindows; _message.Text = "Windows could not change this startup preference."; } };
        Section("Startup", "Start quietly in your system tray. This is off by default.", startup);
        Section("Keyboard access", "Enable Ctrl+Alt+Space to open Crest from the keyboard. It follows your visibility choice: desktop-only by default, or over another app when app access is enabled. Tab moves between controls; Escape dismisses.", Toggle("Enable the accessibility reveal shortcut", _app.Preferences.AccessibilityShortcut, v => _app.Preferences.AccessibilityShortcut = v));
    }
    private void Modules()
    {
        Heading("Your everyday essentials.", "Choose what appears in the island. Everything works locally.");
        var details = new[] { ("Music", "Playback and metadata from available Windows media sessions."), ("Controls", "System volume, mute, OS battery status, and supported laptop brightness."), ("Timer", "Multiple named timers and a stopwatch with pause, resume, and restart recovery."), ("Launch", "Your chosen apps, folders, websites, and shortcuts."), ("Shelf", "Up to 20 file references for this session. Original files are untouched.") };
        foreach (var (name, detail) in details)
            Section(name, detail, Toggle("Show " + name, _app.Preferences.EnabledModules.Contains(name), v => { if (v && !_app.Preferences.EnabledModules.Contains(name)) _app.Preferences.EnabledModules.Add(name); if (!v) _app.Preferences.EnabledModules.Remove(name); }));
        Section("Timer sound", "Completions wait for your next hover. An optional Windows sound can play when a timer finishes.", Toggle("Play a sound for timer completion", _app.Preferences.TimerSound, v => _app.Preferences.TimerSound = v));
        Section("Future modules", "Clipboard capture, system statistics, and account integrations belong to later phases. They are not enabled or collecting data in this build.");
    }
    private void Shortcuts()
    {
        Heading("Your familiar places.", "Add only the apps, folders, and websites you want within reach.");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var file = Design.Button("Add app or file", AddFile);
        actions.Children.Add(file);
        var folder = Design.Button("Add folder", AddFolder);
        folder.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(folder);
        _page.Children.Add(actions);
        var form = new StackPanel();
        var name = new TextBox { Text = "", Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(name, "Website shortcut name");
        form.Children.Add(Design.Text("Name", 11, "Muted"));
        form.Children.Add(name);
        var url = new TextBox { Text = "https://", Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(url, "Website URL");
        form.Children.Add(Design.Text("Website address", 11, "Muted"));
        form.Children.Add(url);
        form.Children.Add(Design.Button("Add website", () => { if (string.IsNullOrWhiteSpace(name.Text) || !Uri.TryCreate(url.Text, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) { _message.Text = "Enter a name and a valid https:// or http:// address."; return; } Add(name.Text.Trim(), uri.AbsoluteUri, "Website"); }));
        Section("A favorite website", "Websites open in your default browser.", form);
        foreach (var entry in _app.Preferences.Launchers.ToArray())
        {
            var s = new StackPanel();
            s.Children.Add(Design.Text(entry.Name, 14, "Text", true));
            var path = Design.Text(entry.Target, 11, "Muted");
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.TextWrapping = TextWrapping.NoWrap;
            s.Children.Add(path);
            var remove = Design.Button("Remove shortcut", () => { _app.Preferences.Launchers.Remove(entry); Save(); BuildPage(); });
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(remove);
            var c = Design.Card(s);
            c.Margin = new Thickness(0, 0, 0, 10);
            _page.Children.Add(c);
        }
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
            _message.Text = "You can keep up to 100 shortcuts.";
            return;
        }
        _app.Preferences.Launchers.Add(new LauncherEntry { Name = name, Target = target, Kind = kind });
        Save();
        BuildPage();
    }
    private void About()
    {
        Heading("Small by design.", "Crest 0.1.0 · Windows 11 · x64\nA local first release candidate.");
        Section("Your data stays here", "No account, analytics, or required internet connection. Preferences and shortcuts live in your Windows user profile. The file shelf clears when you quit. Clipboard capture and online integrations are absent from this build.");
        Section("Clear Crest data", "Reset preferences and shortcuts, cancel all timers, reset the stopwatch, clear shelf references, and turn off Windows startup. Your original files are untouched.", Design.Button("Reset Crest data", () => { if (MessageBox.Show(this, "Reset all Crest preferences, shortcuts, timers, stopwatch, and shelf references? Your original files will stay in place.", "Reset Crest data", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) { try { _app.ResetData(); BuildPage(); _message.Text = "Crest data was reset."; } catch { _message.Text = "Some data could not be reset. Please try again."; } } }));
        Section("Capability status", $"Audio: {_app.Hardware.AudioStatus}\nBrightness: {_app.Hardware.BrightnessStatus}\nBattery: {_app.Hardware.Battery.Status}\nMusic: {_app.Hardware.Music.Status}");
        Section("Using the tray", "Double-click the Crest tray icon to open Settings. Right-click it for Settings, Pause / Resume, and Quit. Pause disables the hover trigger. Quit releases the island and native subscriptions.");
        Section("Release verification", "Automated checks cover desktop interaction transitions, timer recovery, settings recovery, and file reference limits. The source includes a compatibility report identifying hardware checks and remaining acceptance tests.");
        Section("Uninstall", "Use Windows Installed apps after setup. The uninstaller offers a choice to retain or remove Crest preferences. Portable copies can be closed from the tray and removed.");
    }
}

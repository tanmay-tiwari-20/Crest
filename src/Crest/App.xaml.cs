using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
namespace Crest;

public partial class App : Application
{
    public static string AppVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.2.1";
    private static DiagnosticLog? Diagnostics;
    private string _instancePrefix = @"Local\Crest";
    private string? _acceptanceDirectory;
    private Mutex? _single; private EventWaitHandle? _settingsSignal, _quitSignal; private Forms.NotifyIcon? _tray; private bool _quitting, _cleaned;
    public AppSettings Preferences { get; private set; } = new();
    public SettingsStore Store { get; private set; } = null!;
    public DesktopPolicy Policy { get; private set; } = null!;
    public InteractionController Controller { get; private set; } = null!;
    public HardwareHub Hardware { get; private set; } = null!;
    public LocalHub Local { get; private set; } = null!;
    public LocalWorkflowHub Workflows { get; private set; } = null!;
    public ClipboardHistoryHub ClipboardHistory { get; private set; } = null!;
    public ClipboardHistoryListener ClipboardListener { get; private set; } = null!;
    public AppSettings EffectivePreferences => Preferences.ForDisplay(Policy?.DisplayDeviceName ?? Preferences.SelectedDisplay);
    private IslandWindow _island = null!; private SettingsWindow? _settings; private bool _paused;
    private TimerAlertPlayer? _timerAlertPlayer;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int profileIndex = Array.IndexOf(e.Args, "--acceptance-profile");
        if (profileIndex >= 0)
        {
            if (profileIndex + 1 >= e.Args.Length || !Path.IsPathFullyQualified(e.Args[profileIndex + 1]))
            {
                Shutdown(2);
                return;
            }
            _acceptanceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(e.Args[profileIndex + 1]));
            // Acceptance instances never share the user's data, startup entry or IPC signals.
            if (_acceptanceDirectory.Equals(SettingsStore.DataDirectory, StringComparison.OrdinalIgnoreCase))
            {
                Shutdown(2);
                return;
            }
            string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_acceptanceDirectory.ToUpperInvariant())))[..24];
            _instancePrefix = @"Local\Crest.Acceptance." + identity;
        }
        if (e.Args.Contains("--quit"))
        {
            try
            {
                using var quit = EventWaitHandle.OpenExisting(_instancePrefix + ".Quit");
                quit.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        _single = new Mutex(true, _instancePrefix + ".PersonalUtility", out bool first);
        if (!first)
        {
            if (e.Args.Contains("--settings"))
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(_instancePrefix + ".OpenSettings");
                    signal.Set();
                }
                catch { }
            }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => { WriteDiagnostic("ui-fault:" + args.Exception.GetType().Name); args.Handled = true; try { Controller?.Dismiss(); _island?.Suppress(); if (Controller != null) Controller.CompleteClose(Controller.TransitionVersion); } catch { } };
        Store = new SettingsStore(_acceptanceDirectory);
        if (e.Args.Contains("--diagnostics"))
            Diagnostics = new DiagnosticLog(Store.DirectoryPath);
        Preferences = Store.Load();
        bool startupRegistered = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Crest", null) as string)?.Contains("Crest.exe", StringComparison.OrdinalIgnoreCase) == true;
        if (_acceptanceDirectory == null && Preferences.StartWithWindows != startupRegistered)
        {
            Preferences.StartWithWindows = startupRegistered;
            Store.Save(Preferences);
        }
        Design.Apply(Preferences);
        Hardware = new HardwareHub(Preferences.PreferredPlayer);
        Local = new LocalHub(Store, () => Preferences);
        Workflows = new LocalWorkflowHub(Store);
        ClipboardHistory = new ClipboardHistoryHub(Store, () => new ClipboardHistoryOptions(Preferences.ClipboardHistoryEnabled, Preferences.ClipboardRetentionDays));
        ClipboardListener = new ClipboardHistoryListener(ClipboardHistory);
        ClipboardListener.Start();
        _timerAlertPlayer = new TimerAlertPlayer(() => Preferences.TimerSound,
            id => Local.Timers.Any(timer => timer.Id == id && timer.Phase == TimerPhase.Completed));
        Local.TimerCompleted += (_, args) => _timerAlertPlayer.QueueCompletion(args.Timer.Id);
        var activityHub = Local;
        Local.ReminderDue += (_, args) => Dispatcher.BeginInvoke(() =>
        {
            if (!_quitting && ReferenceEquals(activityHub, Local) && Local.Reminders.Any(reminder => reminder.Id == args.Reminder.Id && reminder.DueUtc == args.Reminder.DueUtc && reminder.Phase == ReminderPhase.Due))
                _tray?.ShowBalloonTip(6000, "Crest reminder", args.Reminder.Text, Forms.ToolTipIcon.Info);
        });
        Local.FocusCompleted += (_, args) => Dispatcher.BeginInvoke(() =>
        {
            var focus = Local.Focus;
            if (!_quitting && ReferenceEquals(activityHub, Local) && focus.Phase == TimerPhase.Completed && focus.Id == args.Focus.Id && focus.Stage == args.Focus.Stage && focus.CompletedRounds == args.Focus.CompletedRounds)
                _tray?.ShowBalloonTip(6000, "Crest focus", args.Focus.CompletedRounds >= args.Focus.TargetRounds ? "Focus session complete." : args.Focus.Stage + " complete. Continue when you are ready.", Forms.ToolTipIcon.Info);
        });
        Policy = new DesktopPolicy(() => Preferences);
        _island = new IslandWindow(this);
        new WindowInteropHelper(_island).EnsureHandle();
        Controller = new InteractionController(Policy, () => EffectivePreferences, () => _island.PixelBounds, () => _island.HoldsInteraction, _island.ContainsVisiblePoint);
        Controller.KeyboardFocusRequested += (_, _) => _island.FocusForKeyboard();
        Controller.RevealRequested += (_, _) => _island.Reveal(Controller.TransitionVersion);
        Controller.CloseRequested += (_, _) => _island.CloseAnimated(Controller.TransitionVersion);
        Controller.SuppressRequested += (_, _) => _island.Suppress();
        Controller.StateChanged += (_, _) => WriteDiagnostic("state:" + Controller.State);
        Policy.Changed += (_, _) => { Design.Apply(EffectivePreferences); _island.PositionOnDisplay(); WriteDiagnostic("eligibility:" + Policy.IsEligible); };
        Hardware.Changed += (_, _) => Dispatcher.BeginInvoke(() => { _island.Refresh(); _settings?.RefreshCapabilities(); });
        Local.Changed += (_, _) => Dispatcher.BeginInvoke(() => { _island.Refresh(); _settings?.RefreshActivities(); });
        ClipboardHistory.Changed += (_, _) => Dispatcher.BeginInvoke(() => _island.Refresh());
        int renderIndex = Array.IndexOf(e.Args, "--render-previews");
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            Dispatcher.BeginInvoke(async () => { try { Hardware.Start(); await Hardware.RefreshAsync(); _island.RenderPreviews(Path.GetFullPath(e.Args[renderIndex + 1])); } finally { Quit(); } });
            return;
        }
        SetupTray();
        _settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, _instancePrefix + ".OpenSettings");
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, _instancePrefix + ".Quit");
        _ = Task.Run(() => { while (!_quitting) { try { int signal = WaitHandle.WaitAny(new WaitHandle[] { _settingsSignal, _quitSignal }, 1000); if (!_quitting && signal == 0) Dispatcher.BeginInvoke(OpenSettings); if (!_quitting && signal == 1) Dispatcher.BeginInvoke(Quit); } catch { break; } } });
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        Policy.Start();
        Controller.Start();
        Hardware.Start();
        if (e.Args.Contains("--settings"))
            OpenSettings();
        WriteDiagnostic("started:" + AppVersion);
        if (_acceptanceDirectory != null)
            Dispatcher.BeginInvoke(() => WriteAcceptanceReady());
    }
    private void WriteAcceptanceReady()
    {
        // A dispatcher turn after initialization establishes shell/controller readiness,
        // not completion of asynchronous hardware loading or a certified cold start.
        try
        {
            Directory.CreateDirectory(_acceptanceDirectory!);
            string file = Path.Combine(_acceptanceDirectory!, "acceptance-ready.json");
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId,
                TimestampUtc = DateTimeOffset.UtcNow,
                CandidateDllSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
                Preferences.AllowOverApps, Preferences.StrictDesktop,
                ControllerState = Controller.State.ToString(),
                Policy.NativeHookCount,
                IslandVisible = _island.IsVisible,
                SettingsVisible = _settings?.IsVisible == true,
                InstancePrefix = _instancePrefix,
                Limitations = "Dispatcher-ready isolated app. Hardware initializes asynchronously; cold-cache readiness and complete native acceptance require separate evidence."
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteDiagnostic("acceptance-ready:" + ex.GetType().Name);
        }
    }
    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon { Text = "Crest · desktop controls", Visible = true };
        string icon = Path.Combine(AppContext.BaseDirectory, "crest.ico");
        _tray.Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Crest " + AppVersion).Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("Timers, focus and reminders", null, (_, _) => Dispatcher.Invoke(OpenActivities));
        _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(OpenActivities);
        var pause = new Forms.ToolStripMenuItem("Pause");
        pause.Click += (_, _) => Dispatcher.Invoke(() => { _paused = !_paused; Controller.SetPaused(_paused); pause.Text = _paused ? "Resume" : "Pause"; _tray.Text = _paused ? "Crest · paused" : "Crest · desktop controls"; });
        menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
    }
    public void OpenSettings()
    {
        Controller.Dismiss();
        _island.Suppress();
        if (_settings != null)
        {
            _settings.ShowAndActivate();
            return;
        }
        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) => { _settings = null; Policy.Recheck(); };
        _settings.ShowAndActivate();
    }
    public void SavePreferences()
    {
        Store.Save(Preferences);
        ClipboardHistory?.ApplyPreferences();
        if (!Preferences.TimerSound) _timerAlertPlayer?.CancelCompletionAlerts();
        Design.Apply(EffectivePreferences);
        Policy.Recheck();
        _island.Refresh();
    }
    public void OpenActivities()
    {
        OpenSettings();
        _settings?.ShowSection("Activities");
    }
    private void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e) => RefreshSystemAppearance();
    private void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
            RefreshSystemAppearance();
    }
    private void RefreshSystemAppearance()
    {
        if (_quitting || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => { if (_quitting) return; Design.Apply(EffectivePreferences); _island.Refresh(); });
    }
    public void ResetData()
    {
        _timerAlertPlayer?.CancelCompletionAlerts();
        if (_acceptanceDirectory == null)
            SetStartup(false);
        Local.ResetProductivity();
        Hardware.ClearBatteryNotices();
        Local.ClearShelf();
        ClipboardHistory.Reset();
        Workflows.ClearCaptures();
        Store.Clear();
        Preferences = new AppSettings();
        SavePreferences();
    }
    public void SetStartup(bool enabled)
    {
        if (_acceptanceDirectory != null)
            throw new InvalidOperationException("Acceptance instances do not modify Windows startup.");
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null)
            throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (enabled)
            key.SetValue("Crest", "\"" + Environment.ProcessPath + "\"");
        else
            key.DeleteValue("Crest", false);
        Preferences.StartWithWindows = enabled;
        SavePreferences();
    }
    public async Task OpenTarget(Func<string?> launch)
    {
        var error = await Task.Run(launch);
        if (error != null)
            _island.ShowMessage(error);
    }
    public void OpenWindowsTool(string uri)
    {
        Controller.Dismiss();
        _island.Suppress();
        _ = OpenTarget(() => WindowsTools.Open(uri));
    }
    public void OpenWorkspace(WorkspacePreset workspace)
    {
        var snapshot = new WorkspacePreset { Name = workspace.Name, Targets = workspace.Targets.Select(target => new LauncherEntry { Id = target.Id, Name = target.Name, Target = target.Target, Kind = target.Kind }).ToList() };
        Controller.Dismiss(); _island.Suppress();
        _ = OpenTarget(() => LocalWorkflowHub.LaunchWorkspace(snapshot, Local.Launch));
    }
    public static void WriteDiagnostic(string code)
    {
        Diagnostics?.Enqueue(code);
    }
    public void PreviewTimerSound() => _timerAlertPlayer?.Preview();
    public void Quit()
    {
        Cleanup();
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }
    private void Cleanup()
    {
        if (_cleaned)
            return;
        _cleaned = true;
        _quitting = true;
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        foreach (Action release in new Action[] { () => _timerAlertPlayer?.Dispose(), () => ClipboardListener?.Dispose(), () => ClipboardHistory?.Dispose(), () => _island?.Suppress(), () => Controller?.Dispose(), () => Policy?.Dispose(), () => Hardware?.Dispose(), () => Local?.Dispose(), () => _tray?.Dispose(), () => _settings?.Close(), () => _island?.Close(), () => _settingsSignal?.Dispose(), () => _quitSignal?.Dispose(), () => _single?.ReleaseMutex(), () => _single?.Dispose(), () => Diagnostics?.Dispose() })
        {
            try
            {
                release();
            }
            catch (Exception ex) { WriteDiagnostic("cleanup:" + ex.GetType().Name); }
        }
    }
}

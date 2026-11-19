using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ComponentModel;
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
    private static readonly DiagnosticLog? Diagnostics = Environment.GetCommandLineArgs().Contains("--diagnostics")
        ? new DiagnosticLog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crest")) : null;
    private Mutex? _single; private EventWaitHandle? _settingsSignal, _quitSignal; private Forms.NotifyIcon? _tray; private bool _quitting, _cleaned;
    public AppSettings Preferences { get; private set; } = new();
    public SettingsStore Store { get; private set; } = null!;
    public DesktopPolicy Policy { get; private set; } = null!;
    public InteractionController Controller { get; private set; } = null!;
    public HardwareHub Hardware { get; private set; } = null!;
    public LocalHub Local { get; private set; } = null!;
    private IslandWindow _island = null!; private SettingsWindow? _settings; private bool _paused;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--quit"))
        {
            try
            {
                using var quit = EventWaitHandle.OpenExisting(@"Local\Crest.Quit");
                quit.Set();
            }
            catch { }
            Shutdown();
            return;
        }
        _single = new Mutex(true, @"Local\Crest.PersonalUtility", out bool first);
        if (!first)
        {
            if (e.Args.Contains("--settings"))
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(@"Local\Crest.OpenSettings");
                    signal.Set();
                }
                catch { }
            }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => { WriteDiagnostic("ui-fault:" + args.Exception.GetType().Name); args.Handled = true; try { Controller?.Dismiss(); _island?.Suppress(); if (Controller != null) Controller.CompleteClose(Controller.TransitionVersion); } catch { } };
        Store = new SettingsStore();
        Preferences = Store.Load();
        bool startupRegistered = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Crest", null) as string)?.Contains("Crest.exe", StringComparison.OrdinalIgnoreCase) == true;
        if (Preferences.StartWithWindows != startupRegistered)
        {
            Preferences.StartWithWindows = startupRegistered;
            Store.Save(Preferences);
        }
        Design.Apply(Preferences);
        Hardware = new HardwareHub(Preferences.PreferredPlayer);
        Local = new LocalHub(Store, () => Preferences);
        Policy = new DesktopPolicy(() => Preferences);
        _island = new IslandWindow(this);
        new WindowInteropHelper(_island).EnsureHandle();
        Controller = new InteractionController(Policy, () => Preferences, () => _island.PixelBounds, () => _island.HoldsInteraction, _island.ContainsVisiblePoint);
        Controller.KeyboardFocusRequested += (_, _) => _island.FocusForKeyboard();
        Controller.RevealRequested += (_, _) => _island.Reveal(Controller.TransitionVersion);
        Controller.CloseRequested += (_, _) => _island.CloseAnimated(Controller.TransitionVersion);
        Controller.SuppressRequested += (_, _) => _island.Suppress();
        Controller.StateChanged += (_, _) => WriteDiagnostic("state:" + Controller.State);
        Policy.Changed += (_, _) => { _island.PositionOnDisplay(); WriteDiagnostic("eligibility:" + Policy.IsEligible); };
        Hardware.Changed += (_, _) => Dispatcher.BeginInvoke(() => { _island.Refresh(); _settings?.RefreshCapabilities(); });
        Local.Changed += (_, _) => Dispatcher.BeginInvoke(() => _island.Refresh());
        Local.Completion += (_, _) => { if (Preferences.TimerSound) Dispatcher.BeginInvoke(() => System.Media.SystemSounds.Asterisk.Play()); };
        int renderIndex = Array.IndexOf(e.Args, "--render-previews");
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            Dispatcher.BeginInvoke(async () => { try { Hardware.Start(); await Hardware.RefreshAsync(); _island.RenderPreviews(Path.GetFullPath(e.Args[renderIndex + 1])); } finally { Quit(); } });
            return;
        }
        SetupTray();
        _settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Crest.OpenSettings");
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Crest.Quit");
        _ = Task.Run(() => { while (!_quitting) { try { int signal = WaitHandle.WaitAny(new WaitHandle[] { _settingsSignal, _quitSignal }, 1000); if (!_quitting && signal == 0) Dispatcher.BeginInvoke(OpenSettings); if (!_quitting && signal == 1) Dispatcher.BeginInvoke(Quit); } catch { break; } } });
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        Policy.Start();
        Controller.Start();
        Hardware.Start();
        if (e.Args.Contains("--settings"))
            OpenSettings();
        WriteDiagnostic("started:0.1.0");
    }
    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon { Text = "Crest · desktop controls", Visible = true };
        string icon = Path.Combine(AppContext.BaseDirectory, "crest.ico");
        _tray.Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Crest 0.1.0").Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
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
        Design.Apply(Preferences);
        Policy.Recheck();
        _island.Refresh();
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
        Dispatcher.BeginInvoke(() => { if (_quitting) return; Design.Apply(Preferences); _island.Refresh(); });
    }
    public void ResetData()
    {
        SetStartup(false);
        Local.ResetProductivity();
        foreach (var item in Local.Shelf)
            Local.RemoveShelf(item.Id);
        Store.Clear();
        Preferences = new AppSettings();
        SavePreferences();
    }
    public void SetStartup(bool enabled)
    {
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
    public static void WriteDiagnostic(string code)
    {
        Diagnostics?.Enqueue(code);
    }
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
        foreach (Action release in new Action[] { () => _island?.Suppress(), () => Controller?.Dispose(), () => Policy?.Dispose(), () => Hardware?.Dispose(), () => Local?.Dispose(), () => _tray?.Dispose(), () => _settings?.Close(), () => _island?.Close(), () => _settingsSignal?.Dispose(), () => _quitSignal?.Dispose(), () => _single?.ReleaseMutex(), () => _single?.Dispose(), () => Diagnostics?.Dispose() })
        {
            try
            {
                release();
            }
            catch (Exception ex) { WriteDiagnostic("cleanup:" + ex.GetType().Name); }
        }
    }
}

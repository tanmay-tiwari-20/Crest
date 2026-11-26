using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "artifacts/productivity-qa");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "CrestUIProbe-" + Guid.NewGuid().ToString("N"));
        // Use the real application type without running startup, single-instance
        // registration, tray setup, or the user's persisted settings.
        var app = new PresentationProbeApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var assembly = typeof(Program).Assembly;
        using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("App.xaml", StringComparison.Ordinal)))!;
        var source = XDocument.Load(resource);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), source.Root!.Element(presentation + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var settings = new AppSettings { Theme = "Dark" };
        var clock = new ProbeClock();
        using var hub = new LocalHub(new SettingsStore(temporary), () => settings, clock);
        var messages = new List<string>();
        var panel = new TimerToolsPanel(hub, messages.Add);
        var surface = new Border { Width = 388, Height = 236, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        surface.SetResourceReference(Border.BackgroundProperty, "IslandShell");
        surface.Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new Window { Title = "Crest productivity acceptance probe", Width = 420, Height = 316, Content = surface, ShowInTaskbar = true };
        Design.Apply(settings);
        IslandProbe.ApplyIslandPalette(surface);
        int exitCode = 0;
        if (args.Contains("--show"))
        {
            hub.AddTimer(TimeSpan.FromMinutes(5), "Tea");
            hub.AddTimer(TimeSpan.FromMinutes(25), "Focus");
            hub.StartStopwatch();
            clock.Advance(65.47);
            hub.LapStopwatch();
            hub.PauseStopwatch();
            panel.Refresh();
            window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
        else
        {
            window.Loaded += async (_, _) =>
            {
                var checks = new List<string>();
                try
                {
                    if (args.Contains("--premium-only"))
                    {
                        await PremiumPresentationChecks.Run(app, (passed, label) => Check(passed, label, checks), output);
                        File.WriteAllText(Path.Combine(output, "premium-only-outcomes.json"), JsonSerializer.Serialize(new { coverage = "Premium presentation checks only", passed = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true }));
                        Console.WriteLine($"PASS {checks.Count} premium presentation outcomes in {output}");
                        return;
                    }
                    await Click(panel, "Add a 5 minute timer");
                    await Click(panel, "Add a 15 minute timer");
                    Check(hub.Timers.Count == 2 && hub.Timers[0].TotalSeconds == 300 && hub.Timers[1].TotalSeconds == 900, "Adding presets preserves independent timers", checks);
                    await Click(panel, "Pause Timer 1");
                    clock.Advance(20); hub.Tick(); panel.Refresh();
                    Check(hub.Timers[0].RemainingSeconds == 300 && hub.Timers[1].RemainingSeconds == 880, "Row actions address the intended timer", checks);
                    var name = Descendants<TextBox>(panel).Single(input => AutomationProperties.GetName(input) == "New timer name, optional");
                    var duration = Descendants<TextBox>(panel).Single(input => AutomationProperties.GetName(input) == "New timer duration in minutes");
                    name.Text = "A named tea timer with a long but bounded label";
                    duration.Text = 0.05.ToString(System.Globalization.CultureInfo.CurrentCulture);
                    await Click(panel, "Add custom timer");
                    clock.Advance(4); hub.Tick(); panel.Refresh();
                    string completedName = hub.Timers.Single(timer => timer.Phase == TimerPhase.Completed).Name;
                    await Click(panel, "Acknowledge " + completedName);
                    Check(hub.Timers.Count == 2 && hub.Timers.All(timer => timer.Phase != TimerPhase.Completed), "Acknowledgement removes only the completed timer", checks);
                    duration.Text = "NaN";
                    await Click(panel, "Add custom timer");
                    Check(messages.Count == 1 && hub.Timers.Count == 2, "Invalid custom duration leaves active timers intact", checks);
                    await Click(panel, "Stopwatch");
                    await Click(panel, "Start stopwatch");
                    clock.Advance(65.47); panel.Refresh();
                    await Click(panel, "Record stopwatch lap");
                    await Click(panel, "Pause stopwatch");
                    double paused = hub.Stopwatch.ElapsedSeconds;
                    clock.Advance(10); panel.Refresh();
                    Check(hub.Stopwatch.Phase == StopwatchPhase.Paused && hub.Stopwatch.ElapsedSeconds == paused && hub.Stopwatch.Laps.Count == 1, "Stopwatch controls record a lap and hold paused elapsed time", checks);
                    await Click(panel, "Resume stopwatch");
                    clock.Advance(5); panel.Refresh();
                    await Click(panel, "Pause stopwatch");
                    Check(hub.Stopwatch.ElapsedSeconds == paused + 5, "Stopwatch resumes without including paused time", checks);
                    name.Text = "Tea";
                    duration.Text = "10";
                    foreach (string theme in new[] { "Dark", "Light" })
                    {
                        settings.Theme = theme; Design.Apply(settings);
                        foreach (int dpi in new[] { 96, 120, 144, 192 })
                        {
                            await Click(panel, "Timers");
                            Render(surface, Path.Combine(output, $"{theme.ToLowerInvariant()}-timers-{dpi}.png"), dpi);
                            await Click(panel, "Stopwatch");
                            Render(surface, Path.Combine(output, $"{theme.ToLowerInvariant()}-stopwatch-{dpi}.png"), dpi);
                        }
                    }
                    var peers = Descendants<Button>(panel).Where(button => button.IsVisible).Select(button => new ButtonAutomationPeer(button)).ToArray();
                    Check(peers.All(peer => !string.IsNullOrWhiteSpace(peer.GetName()) && peer.GetAutomationControlType() == AutomationControlType.Button), "Visible productivity buttons expose accessible names and roles", checks);
                    await Click(panel, "Reset stopwatch and clear laps");
                    Check(hub.Stopwatch.Phase == StopwatchPhase.Idle && hub.Stopwatch.Laps.Count == 0, "Reset clears elapsed time and saved laps", checks);
                    await CheckIslandPresentation(app, settings, hub, output, checks);
                    await NativeShelfDropChecks.Run(app, hub, settings, (passed, label) => Check(passed, label, checks));
                    await TimerSoundChecks.Run(app, settings, (passed, label) => Check(passed, label, checks));
                    if (args.Contains("--skip-native-clipboard")) ClipboardSnapshotChecks.Run((passed, label) => Check(passed, label, checks));
                    else await ClipboardHistoryChecks.Run((passed, label) => Check(passed, label, checks));
                    await HardwareP2Checks.Run((passed, label) => Check(passed, label, checks));
                    await LocalP2Checks.Run((passed, label) => Check(passed, label, checks));
                    await LocalPreferencesChecks.Run(app, (passed, label) => Check(passed, label, checks));
                    await LocalWorkflowUiChecks.Run(app, (passed, label) => Check(passed, label, checks), output);
                    await WeatherCalendarChecks.Run(app, (passed, label) => Check(passed, label, checks), output);
                    await WindowsIntegrationChecks.Run((passed, label) => Check(passed, label, checks));
                    await DownloadAiUiChecks.Run(app, (passed, label) => Check(passed, label, checks), output);
                    await IntegrationWiringChecks.Run(app, (passed, label) => Check(passed, label, checks));
                    await PremiumPresentationChecks.Run(app, (passed, label) => Check(passed, label, checks), output);
                    File.WriteAllText(Path.Combine(output, "ui-outcomes.json"), JsonSerializer.Serialize(new { recordedAtUtc = DateTimeOffset.UtcNow, skippedNativeClipboard = args.Contains("--skip-native-clipboard"), assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(TimerToolsPanel).Assembly.Location))).ToLowerInvariant(), passed = checks.Count, checks, limits = "Rendered DPI samples and isolated presentation/drop/audio regressions do not qualify desktop visibility, physical desktop OLE dragging, audible hardware playback, physical mixed-DPI, frame timing, or screen-reader acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"PASS {checks.Count} UI outcomes; 16 productivity and 4 date/time DPI renders in {output}");
                }
                catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
                finally { window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            };
        }
        window.Show();
        Dispatcher.Run();
        hub.ResetProductivity();
        hub.Dispose();
        // Only this probe's unique temporary directory is removed.
        string resolved = Path.GetFullPath(temporary);
        if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("CrestUIProbe-", StringComparison.Ordinal) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        return exitCode;
    }

    private static async Task CheckIslandPresentation(Crest.App app, AppSettings settings, LocalHub hub, string output, List<string> checks)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        string originalMotion = settings.Motion, originalTheme = settings.Theme;
        var originalModules = settings.EnabledModules.ToList();
        using var hardware = new HardwareHub();
        // Retain empty capability snapshots while preventing even read-only
        // background hardware/media requests from this presentation fixture.
        hardware.Dispose();
        using var policy = new DesktopPolicy(() => settings);
        SetAppProperty(app, nameof(Crest.App.Preferences), settings);
        SetAppProperty(app, nameof(Crest.App.Local), hub);
        SetAppProperty(app, nameof(Crest.App.Store), Field<SettingsStore>(hub, "_store"));
        SetAppProperty(app, nameof(Crest.App.Hardware), hardware);
        SetAppProperty(app, nameof(Crest.App.Policy), policy);
        var island = new IslandWindow(app);
        using var controller = new InteractionController(policy, () => settings, () => island.PixelBounds, () => island.HoldsInteraction, island.ContainsVisiblePoint);
        SetAppProperty(app, nameof(Crest.App.Controller), controller);
        FieldInfo islandField = typeof(Crest.App).GetField("_island", BindingFlags.Instance | BindingFlags.NonPublic)!;
        islandField.SetValue(app, island);
        var slide = Field<TranslateTransform>(island, "_slide");
        var detailSlide = Field<TranslateTransform>(island, "_detailSlide");
        var clip = Field<RectangleGeometry>(island, "_clip");
        Rect compactRect = StaticField<Rect>(typeof(IslandWindow), "CompactRect");
        try
        {
            settings.Theme = "Dark";
            settings.Motion = "Reduced";
            island.Reveal(controller.TransitionVersion);
            bool revealStable = slide.Y == 0 && !slide.HasAnimatedProperties;
            await Task.Delay(30);
            revealStable &= slide.Y == 0 && !slide.HasAnimatedProperties;
            await Task.Delay(90);
            island.CloseAnimated(controller.TransitionVersion);
            bool closeStable = slide.Y == 0 && !slide.HasAnimatedProperties;
            await Task.Delay(120);
            Check(revealStable && closeStable && !island.IsVisible && !island.IsHitTestVisible, "Reduced-motion reveal and close fade without translation or a surviving hit surface", checks);

            island.Reveal(controller.TransitionVersion);
            await Task.Delay(120);
            island.Expand("Timer");
            Rect openRect = Field<Rect>(island, "_targetRect");
            bool expandedImmediately = clip.Rect == openRect && !clip.HasAnimatedProperties && detailSlide.Y == 0 && !detailSlide.HasAnimatedProperties;
            Invoke(island, "Collapse");
            bool collapsedImmediately = clip.Rect == compactRect && !clip.HasAnimatedProperties;
            island.Expand("Timer");
            await Task.Delay(120);
            Check(expandedImmediately && collapsedImmediately && clip.Rect == openRect && !clip.HasAnimatedProperties && detailSlide.Y == 0 && !detailSlide.HasAnimatedProperties, "Reduced-motion detail expansion and collapse apply geometry immediately without translation", checks);

            var clockLabel = Field<Dictionary<string, TextBlock>>(island, "_labels")["clock"];
            bool clockCorrect = true;
            foreach (string culture in new[] { "en-GB", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                DateTime before = DateTime.Now;
                island.Refresh();
                DateTime after = DateTime.Now;
                clockCorrect &= clockLabel.Text == before.ToString("g", CultureInfo.CurrentCulture) || clockLabel.Text == after.ToString("g", CultureInfo.CurrentCulture);
            }
            island.UpdateLayout();
            var text = new FormattedText(clockLabel.Text, CultureInfo.CurrentCulture, clockLabel.FlowDirection, new Typeface(clockLabel.FontFamily, clockLabel.FontStyle, clockLabel.FontWeight, clockLabel.FontStretch), clockLabel.FontSize, clockLabel.Foreground, VisualTreeHelper.GetDpi(clockLabel).PixelsPerDip);
            clockCorrect &= AutomationProperties.GetName(clockLabel) == "Local date and time" && text.WidthIncludingTrailingWhitespace <= clockLabel.ActualWidth + 0.5;
            var detail = Field<Border>(island, "_detail");
            // Static layout evidence must not depend on whether the compositor
            // has presented the fixture's fade. Motion checks above and below
            // continue to exercise the actual animation paths.
            island.BeginAnimation(UIElement.OpacityProperty, null);
            island.Opacity = 1;
            detail.BeginAnimation(UIElement.OpacityProperty, null);
            detail.Opacity = 1;
            foreach (int dpi in new[] { 96, 120, 144, 192 })
                Render(detail, Path.Combine(output, $"dark-date-time-{dpi}.png"), dpi, clockLabel);
            Check(clockCorrect, "Detail footer displays localized date and time with an accessible name and unclipped layout", checks);
            CultureInfo.CurrentCulture = originalCulture;

            island.Suppress();
            settings.Motion = "Normal";
            island.Reveal(controller.TransitionVersion);
            island.Expand("Timer");
            if (!slide.HasAnimatedProperties || !detailSlide.HasAnimatedProperties || !clip.HasAnimatedProperties)
                throw new InvalidOperationException("Normal-motion fixture did not create the interrupted animations.");
            settings.Motion = "Reduced";
            Design.Apply(settings);
            island.Refresh();
            bool switchedImmediately = slide.Y == 0 && !slide.HasAnimatedProperties && detailSlide.Y == 0 && !detailSlide.HasAnimatedProperties && clip.Rect == openRect && !clip.HasAnimatedProperties;
            await Task.Delay(280);
            Check(switchedImmediately && slide.Y == 0 && !slide.HasAnimatedProperties && detailSlide.Y == 0 && !detailSlide.HasAnimatedProperties && clip.Rect == openRect && !clip.HasAnimatedProperties, "Enabling reduced motion cancels in-flight movement without stale clocks restoring it", checks);

            // Simulate eligibility only inside this presentation fixture. This does
            // not establish any native desktop-policy or battery-hardware gate.
            var machine = Field<InteractionStateMachine>(controller, "_machine");
            var batteryNotices = Field<BatteryNoticeState>(hardware, "_batteryNotices");
            var batteryProperty = typeof(HardwareHub).GetProperty(nameof(HardwareHub.Battery))!;
            void RevealBatteryFixture()
            {
                machine.SetEligibility(false, false);
                machine.SetEligibility(true, false);
                machine.RevealAccessibility();
                island.Reveal(controller.TransitionVersion);
            }
            island.Suppress();
            batteryNotices.Observe(true, 2, 12, false);
            batteryNotices.Observe(true, 10, 18, true);
            batteryProperty.SetValue(hardware, new BatterySnapshot { Available = true, Percent = 18, Charging = true, Status = "Charging" });
            var historicalLow = hardware.PendingBatteryNotice!;
            RevealBatteryFixture();
            machine.SetEligibility(false, false);
            island.Suppress();
            await Task.Delay(160);
            bool abortedRetained = hardware.PendingBatteryNotice == historicalLow && !island.IsVisible;
            settings.EnabledModules.Remove("Controls");
            RevealBatteryFixture();
            await Task.Delay(180);
            island.Refresh();
            var islandLabels = Field<Dictionary<string, TextBlock>>(island, "_labels");
            Check(abortedRetained && hardware.PendingBatteryNotice == historicalLow && island.IsVisible
                && islandLabels["compactTitle"].Text != "Battery was low",
                "Aborted and Controls-disabled reveals retain an unshown battery warning", checks);

            island.Suppress();
            settings.EnabledModules.Add("Controls");
            RevealBatteryFixture();
            await Task.Delay(180);
            island.Refresh();
            var compact = Field<Grid>(island, "_compact");
            bool compactAcknowledged = hardware.PendingBatteryNotice is null && island.IsVisible && island.Opacity >= 0.99 && compact.IsVisible
                && islandLabels["compactTitle"].Text == "Battery was low" && islandLabels["compactDetail"].Text.Contains("12%", StringComparison.Ordinal);
            island.Expand("Controls");
            await Task.Delay(120);
            island.Refresh();
            Check(compactAcknowledged && islandLabels["bodyBatteryNotice"].IsVisible
                && islandLabels["bodyBatteryNotice"].Text == "Earlier low-battery warning: 12% reported by Windows."
                && islandLabels["bodyBattery"].Text == "18% · Charging" && hardware.Battery.Percent == 18 && hardware.Battery.Charging,
                "A visible completed reveal consumes its warning and distinguishes historical low battery from current charging", checks);

            island.Suppress();
            batteryNotices.Observe(true, 2, 11, false);
            var shownLow = hardware.PendingBatteryNotice!;
            RevealBatteryFixture();
            batteryNotices.Observe(true, 4, 3, false);
            batteryProperty.SetValue(hardware, new BatterySnapshot { Available = true, Percent = 3, Status = "On battery" });
            var newerCritical = hardware.PendingBatteryNotice!;
            await Task.Delay(180);
            island.Refresh();
            bool escalationRetained = newerCritical.Id != shownLow.Id && hardware.PendingBatteryNotice == newerCritical
                && islandLabels["compactTitle"].Text == "Battery was low" && islandLabels["compactDetail"].Text.Contains("11%", StringComparison.Ordinal);
            island.Suppress();
            RevealBatteryFixture();
            await Task.Delay(180);
            island.Refresh();
            Check(escalationRetained && hardware.PendingBatteryNotice is null
                && islandLabels["compactTitle"].Text == "Battery was critically low" && islandLabels["compactDetail"].Text.Contains("3%", StringComparison.Ordinal),
                "A critical update during reveal survives older acknowledgement and waits for the next reveal", checks);

            island.Suppress();
            batteryNotices.Observe(true, 1, 60, false);
            batteryNotices.Observe(true, 2, 13, false);
            var interruptedDetailNotice = hardware.PendingBatteryNotice!;
            RevealBatteryFixture();
            island.Expand("Music");
            await Task.Delay(180);
            island.Refresh();
            bool overviewRetained = hardware.PendingBatteryNotice == interruptedDetailNotice;
            island.Expand("Controls");
            machine.SetEligibility(false, false);
            island.Suppress();
            await Task.Delay(120);
            bool interruptedDetailRetained = hardware.PendingBatteryNotice == interruptedDetailNotice && !island.IsVisible;
            RevealBatteryFixture();
            await Task.Delay(180);
            island.Refresh();
            Check(overviewRetained && interruptedDetailRetained && hardware.PendingBatteryNotice is null
                && islandLabels["compactTitle"].Text == "Battery was low" && islandLabels["compactDetail"].Text.Contains("13%", StringComparison.Ordinal),
                "An interrupted Controls detail fade retains its warning until a later completed compact reveal", checks);
            await CompactActivityChecks.Run(app, island, hardware, controller, settings, (passed, label) => Check(passed, label, checks));
            await CompactLocalP2Checks.Run(app, island, controller, settings, (passed, label) => Check(passed, label, checks));
            await SettingsNavigationChecks.Run(app, (passed, label) => Check(passed, label, checks));
            await CheckShelfDropInteraction(island, policy, controller, hub, settings, checks);
        }
        finally
        {
            island.Suppress();
            island.Close();
            CultureInfo.CurrentCulture = originalCulture;
            settings.Motion = originalMotion;
            settings.Theme = originalTheme;
            settings.EnabledModules = originalModules;
            Design.Apply(settings);
        }
    }

    private static async Task CheckShelfDropInteraction(IslandWindow island, DesktopPolicy policy, InteractionController controller, LocalHub hub, AppSettings settings, List<string> checks)
    {
        // Routed-event regressions exercise the real WPF handlers without a
        // physical drag, changing desktop focus, or touching the user's files.
        var surface = Field<Grid>(island, "_surface");
        var detail = Field<Border>(island, "_detail");
        var detailSlide = Field<TranslateTransform>(island, "_detailSlide");
        var machine = Field<InteractionStateMachine>(controller, "_machine");
        var localGate = Field<object>(hub, "_gate");
        var store = Field<SettingsStore>(hub, "_store");
        string fixtureDirectory = Path.Combine(store.DirectoryPath, "shelf-routed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);
        string CreateFile(string name)
        {
            string path = Path.Combine(fixtureDirectory, name);
            File.WriteAllText(path, "Owned shelf fixture: " + name);
            return path;
        }
        string linked = CreateFile("link.txt"), copied = CreateFile("copy.txt"), pending = CreateFile("pending.txt"), duplicate = CreateFile("duplicate.txt"), rejected = CreateFile("rejected.txt");
        string originalLinked = File.ReadAllText(linked), originalCopied = File.ReadAllText(copied), originalPending = File.ReadAllText(pending);
        Point VisiblePoint() => surface.TranslatePoint(IslandProbe.Interior(island), island);
        Point GapPoint()
        {
            Rect envelope = Field<RectangleGeometry>(island, "_clip").Rect;
            return surface.TranslatePoint(new Point(envelope.X + envelope.Width / 2, envelope.Bottom + 8), island);
        }
        DragEventArgs Raise(RoutedEvent routedEvent, IDataObject data, DragDropEffects allowed, Point point)
        {
            var arguments = NewDragEvent(island, data, allowed, point, routedEvent);
            island.RaiseEvent(arguments);
            return arguments;
        }
        async Task Reveal()
        {
            island.Suppress();
            settings.Motion = "Reduced";
            typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, true);
            machine.SetEligibility(false, false);
            machine.SetEligibility(true, false);
            machine.RevealAccessibility();
            island.Reveal(controller.TransitionVersion);
            await Task.Delay(140);
            island.UpdateLayout();
        }
        async Task Import(IDataObject data, DragDropEffects allowed, Action<DragEventArgs> synchronous)
        {
            var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, EventArgs args) => changed.TrySetResult(true);
            hub.Changed += OnChanged;
            try
            {
                // Preparation must not complete before the effect/handled state
                // is observed at the same synchronous boundary OLE uses.
                lock (localGate)
                {
                    var drop = Raise(DragDrop.PreviewDropEvent, data, allowed, VisiblePoint());
                    synchronous(drop);
                }
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
            finally { hub.Changed -= OnChanged; }
        }

        await Reveal();
        var linkData = new DataObject(DataFormats.FileDrop, new[] { linked });
        var enter = Raise(DragDrop.PreviewDragEnterEvent, linkData, DragDropEffects.Link | DragDropEffects.Copy | DragDropEffects.Move, VisiblePoint());
        bool linkNegotiated = enter.Handled && enter.Effects == DragDropEffects.Link;
        await Task.Delay(100);
        await Import(linkData, DragDropEffects.Link | DragDropEffects.Copy | DragDropEffects.Move, drop => linkNegotiated &= drop.Handled && drop.Effects == DragDropEffects.Link);
        Check(linkNegotiated && hub.Shelf.Any(item => item.Path == linked) && File.ReadAllText(linked) == originalLinked,
            "File drops publish Link and handled state synchronously without moving or modifying the original", checks);

        var copyData = new DataObject(DataFormats.FileDrop, new[] { copied });
        var copyEnter = Raise(DragDrop.PreviewDragEnterEvent, copyData, DragDropEffects.Copy, VisiblePoint());
        bool copyNegotiated = copyEnter.Handled && copyEnter.Effects == DragDropEffects.Copy;
        await Import(copyData, DragDropEffects.Copy, drop => copyNegotiated &= drop.Handled && drop.Effects == DragDropEffects.Copy);
        Check(copyNegotiated && hub.Shelf.Any(item => item.Path == copied) && File.ReadAllText(copied) == originalCopied,
            "Copy-only sources retain a shelf reference with a permitted Copy effect and an unchanged original", checks);

        var rejectionData = new DataObject(DataFormats.FileDrop, new[] { rejected });
        int beforeRejected = hub.Shelf.Count;
        bool rejectedSafely = true;
        void Reject(DragDropEffects allowed, Point point)
        {
            string module = Field<string>(island, "_module");
            foreach (var routedEvent in new[] { DragDrop.PreviewDragEnterEvent, DragDrop.PreviewDragOverEvent, DragDrop.PreviewDropEvent })
            {
                var result = Raise(routedEvent, rejectionData, allowed, point);
                rejectedSafely &= result.Handled && result.Effects == DragDropEffects.None && Field<string>(island, "_module") == module;
            }
        }
        Reject(DragDropEffects.Move, VisiblePoint());
        settings.EnabledModules.Remove("Shelf");
        Reject(DragDropEffects.Link | DragDropEffects.Copy, VisiblePoint());
        settings.EnabledModules.Add("Shelf");
        typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, false);
        Reject(DragDropEffects.Link | DragDropEffects.Copy, VisiblePoint());
        typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, true);
        var gapScreen = island.PointToScreen(GapPoint());
        if (island.ContainsVisiblePoint(new System.Drawing.Point((int)Math.Round(gapScreen.X), (int)Math.Round(gapScreen.Y))))
            throw new InvalidOperationException("Shelf rejection fixture is not inside a transparent gap.");
        Reject(DragDropEffects.Link | DragDropEffects.Copy, GapPoint());
        island.Suppress();
        Reject(DragDropEffects.Link | DragDropEffects.Copy, VisiblePoint());
        await Task.Delay(100);
        Check(rejectedSafely && hub.Shelf.Count == beforeRejected && !hub.Shelf.Any(item => item.Path == rejected),
            "Move-only, disabled, ineligible, hidden, and transparent-gap drags are handled without accepting data or changing modules", checks);

        await Reveal();
        var pendingData = new DataObject(DataFormats.FileDrop, new[] { pending });
        bool duplicateRejected = false;
        Slider? activeVolume = null;
        await Import(pendingData, DragDropEffects.Link, first =>
        {
            if (!first.Handled || first.Effects != DragDropEffects.Link)
                throw new InvalidOperationException("Pending import was not accepted safely.");
            var second = Raise(DragDrop.PreviewDropEvent, new DataObject(DataFormats.FileDrop, new[] { duplicate }), DragDropEffects.Link, VisiblePoint());
            duplicateRejected = second.Handled && second.Effects == DragDropEffects.None;
            island.Expand("Controls");
            activeVolume = Field<Dictionary<string, Slider>>(island, "_sliders")["volume"];
        });
        Check(duplicateRejected && hub.Shelf.Any(item => item.Path == pending) && !hub.Shelf.Any(item => item.Path == duplicate),
            "A second drop is rejected while a shelf import is still in flight", checks);
        Check(Field<string>(island, "_module") == "Controls" && ReferenceEquals(activeVolume, Field<Dictionary<string, Slider>>(island, "_sliders")["volume"]) && File.ReadAllText(pending) == originalPending,
            "A late shelf import preserves the active Controls slider and its source file", checks);

        var missingData = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(fixtureDirectory, "missing.txt") });
        await Import(missingData, DragDropEffects.Link, drop =>
        {
            if (!drop.Handled || drop.Effects != DragDropEffects.Link)
                throw new InvalidOperationException("Missing-file import did not publish its safe effect synchronously.");
        });
        bool controlsStable = Field<string>(island, "_module") == "Controls" && ReferenceEquals(activeVolume, Field<Dictionary<string, Slider>>(island, "_sliders")["volume"]);
        island.Expand("Shelf");
        await Task.Delay(120);
        island.UpdateLayout();
        bool errorVisible = Descendants<TextBlock>(island).Any(text => text.IsVisible && text.Text.Contains("Some items are unavailable", StringComparison.Ordinal));
        Check(controlsStable && errorVisible, "A background shelf import error remains available on the next Shelf view without rebuilding Controls", checks);

        settings.Motion = "Normal";
        island.Expand("Controls");
        island.UpdateLayout();
        detail.BeginAnimation(UIElement.OpacityProperty, null);
        detail.Opacity = 1;
        detailSlide.BeginAnimation(TranslateTransform.YProperty, null);
        detailSlide.Y = -6;
        var envelopeClip = Field<RectangleGeometry>(island, "_clip");
        envelopeClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        envelopeClip.Rect = IslandProbe.Target(island);
        System.Drawing.Point ScreenPoint(Point point)
        {
            var screen = surface.PointToScreen(point);
            return new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        }
        Rect envelopeBounds = envelopeClip.Rect;
        var visibleTop = ScreenPoint(new Point(envelopeBounds.X + envelopeBounds.Width / 2, envelopeBounds.Top + 1));
        var invisibleBottom = ScreenPoint(new Point(envelopeBounds.X + envelopeBounds.Width / 2, envelopeBounds.Bottom + 4));
        Check(island.ContainsVisiblePoint(visibleTop) && policy.IsRegisteredPointerSurface(visibleTop)
            && !island.ContainsVisiblePoint(invisibleBottom) && !policy.IsRegisteredPointerSurface(invisibleBottom),
            "The contiguous envelope hit region follows its current shape and rejects invisible edges", checks);
        var envelopeCenter = ScreenPoint(new Point(envelopeBounds.X + envelopeBounds.Width / 2, envelopeBounds.Top + envelopeBounds.Height / 2));
        island.Opacity = 0;
        bool transparentRejected = !island.ContainsVisiblePoint(envelopeCenter) && !policy.IsRegisteredPointerSurface(envelopeCenter);
        island.Opacity = 1;
        island.IsHitTestVisible = false;
        bool disabledHitRejected = !island.ContainsVisiblePoint(envelopeCenter) && !policy.IsRegisteredPointerSurface(envelopeCenter);
        island.IsHitTestVisible = true;
        detailSlide.Y = 0;
        Check(transparentRejected && disabledHitRejected, "Transparent and input-disabled envelopes expose no registered hit region", checks);

        string reentrantFile = CreateFile("reentrant.txt");
        string reentrantOriginal = File.ReadAllText(reentrantFile);
        await Reveal();
        string initialModule = Field<string>(island, "_module");
        var presentReset = new CallbackDataObject(new DataObject(DataFormats.FileDrop, new[] { reentrantFile }), onPresent: hub.ClearShelf);
        var presentResult = Raise(DragDrop.PreviewDragEnterEvent, presentReset, DragDropEffects.Link, VisiblePoint());
        bool scopeRevalidated = presentReset.CallbackInvoked && presentResult.Handled && presentResult.Effects == DragDropEffects.None
            && hub.Shelf.Count == 0 && Field<string>(island, "_module") == initialModule;

        await Reveal();
        initialModule = Field<string>(island, "_module");
        var dataReset = new CallbackDataObject(new DataObject(DataFormats.FileDrop, new[] { reentrantFile }), onData: hub.ClearShelf);
        var resetResult = Raise(DragDrop.PreviewDropEvent, dataReset, DragDropEffects.Link, VisiblePoint());
        scopeRevalidated &= dataReset.CallbackInvoked && resetResult.Handled && resetResult.Effects == DragDropEffects.None
            && !Field<bool>(island, "_shelfDropPending") && Field<string>(island, "_module") == initialModule;

        await Reveal();
        initialModule = Field<string>(island, "_module");
        var dataSuppress = new CallbackDataObject(new DataObject(DataFormats.FileDrop, new[] { reentrantFile }), onData: island.Suppress);
        var suppressResult = Raise(DragDrop.PreviewDropEvent, dataSuppress, DragDropEffects.Link, VisiblePoint());
        await Task.Delay(100);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        Check(scopeRevalidated && dataSuppress.CallbackInvoked && suppressResult.Handled && suppressResult.Effects == DragDropEffects.None
            && !Field<bool>(island, "_shelfDropPending") && !island.IsVisible && Field<string>(island, "_module") == initialModule
            && hub.Shelf.Count == 0 && File.ReadAllText(reentrantFile) == reentrantOriginal,
            "Reentrant data providers cannot import or switch views after resetting the shelf or suppressing the island", checks);
        island.Suppress();
    }

    private sealed class CallbackDataObject : IDataObject
    {
        private readonly DataObject _inner;
        private Action? _onPresent, _onData;
        public bool CallbackInvoked { get; private set; }
        public CallbackDataObject(DataObject inner, Action? onPresent = null, Action? onData = null)
        {
            _inner = inner;
            _onPresent = onPresent;
            _onData = onData;
        }
        private void BeforePresent()
        {
            var callback = _onPresent;
            _onPresent = null;
            if (callback != null) { CallbackInvoked = true; callback(); }
        }
        private void BeforeData()
        {
            var callback = _onData;
            _onData = null;
            if (callback != null) { CallbackInvoked = true; callback(); }
        }
        public object GetData(string format, bool autoConvert) { BeforeData(); return _inner.GetData(format, autoConvert)!; }
        public object GetData(string format) { BeforeData(); return _inner.GetData(format)!; }
        public object GetData(Type format) { BeforeData(); return _inner.GetData(format)!; }
        public bool GetDataPresent(string format, bool autoConvert) { BeforePresent(); return _inner.GetDataPresent(format, autoConvert); }
        public bool GetDataPresent(string format) { BeforePresent(); return _inner.GetDataPresent(format); }
        public bool GetDataPresent(Type format) { BeforePresent(); return _inner.GetDataPresent(format); }
        public string[] GetFormats(bool autoConvert) => _inner.GetFormats(autoConvert);
        public string[] GetFormats() => _inner.GetFormats();
        public void SetData(object data) => _inner.SetData(data);
        public void SetData(string format, object data, bool autoConvert) => _inner.SetData(format, data, autoConvert);
        public void SetData(string format, object data) => _inner.SetData(format, data);
        public void SetData(Type format, object data) => _inner.SetData(format, data);
    }

    private static DragEventArgs NewDragEvent(DependencyObject target, IDataObject data, DragDropEffects allowed, Point point, RoutedEvent routedEvent)
    {
        var constructor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) }, null)!;
        var arguments = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.LeftMouseButton, allowed, target, point });
        arguments.RoutedEvent = routedEvent;
        return arguments;
    }

    private static void SetAppProperty(Crest.App app, string name, object value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static T StaticField<T>(Type type, string name) => (T)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(instance, null);

    private static async Task Click(DependencyObject root, string name)
    {
        var button = Descendants<Button>(root).Single(button => button.IsVisible && (AutomationProperties.GetName(button) == name || AutomationProperties.GetName(button) == name + ", selected"));
        if (!button.IsEnabled) throw new InvalidOperationException("Disabled action: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            var action = Descendants<Button>(root).Single(control => AutomationProperties.GetName(control) is "Start stopwatch" or "Pause stopwatch" or "Resume stopwatch");
            if (action.IsEnabled) return;
        }
        throw new TimeoutException("Productivity action did not settle: " + name);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Check(bool passed, string label, List<string> checks)
    {
        if (!passed) throw new InvalidOperationException(label);
        checks.Add(label);
        Console.WriteLine("PASS " + label);
    }
    private static void Render(FrameworkElement visual, string path, int dpi, FrameworkElement? expectedText = null)
    {
        visual.UpdateLayout();
        var bounds = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
        // A nested visual carries its parent-relative offset. Draw its local
        // content into a fresh visual so that detail cards are not rendered
        // outside their own bitmap or clipped by an ancestor.
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Design.Brush("IslandShell"), null, bounds);
            context.DrawRectangle(IslandProbe.VisualBrush(visual), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96d), (int)Math.Ceiling(visual.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        if (expectedText != null)
        {
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            int opaquePixels = 0;
            for (int index = 3; index < pixels.Length; index += 4)
                if (pixels[index] >= 200) opaquePixels++;
            Rect textBounds = expectedText.TransformToAncestor(visual).TransformBounds(new Rect(0, 0, expectedText.ActualWidth, expectedText.ActualHeight));
            int left = Math.Clamp((int)Math.Floor(textBounds.Left * dpi / 96d), 0, bitmap.PixelWidth);
            int right = Math.Clamp((int)Math.Ceiling(textBounds.Right * dpi / 96d), 0, bitmap.PixelWidth);
            int top = Math.Clamp((int)Math.Floor(textBounds.Top * dpi / 96d), 0, bitmap.PixelHeight);
            int bottom = Math.Clamp((int)Math.Ceiling(textBounds.Bottom * dpi / 96d), 0, bitmap.PixelHeight);
            int textPixels = 0;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int index = y * stride + x * 4;
                    if (pixels[index + 3] >= 200 && Math.Max(pixels[index], Math.Max(pixels[index + 1], pixels[index + 2])) >= 80)
                        textPixels++;
                }
            if (opaquePixels < bitmap.PixelWidth * bitmap.PixelHeight / 4 || textPixels < 12)
                throw new InvalidOperationException($"Date/time render has no readable content: {path} ({opaquePixels} opaque pixels, {textPixels} date/time text pixels).");
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class PresentationProbeApp : Crest.App
    {
        // WPF schedules startup when Application is constructed, even when the
        // harness owns the dispatcher loop. Deliberately bypass Crest startup.
        protected override void OnStartup(StartupEventArgs e) { }
    }
    private sealed class ProbeClock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}

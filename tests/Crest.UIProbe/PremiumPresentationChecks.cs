using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;

/// <summary>Real WPF spring clocks and isolated static screen fixtures; no network, clipboard writes or target launches.</summary>
internal static class PremiumPresentationChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check, string outputDirectory)
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(parent, "CrestPremiumUI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string output = Path.Combine(outputDirectory, "premium");
        Directory.CreateDirectory(output);
        string[] names = ["Preferences", "Store", "Local", "Hardware", "Policy", "Controller", "Workflows", "ClipboardHistory", "ClipboardListener", "Weather", "Calendar", "Downloads", "Ai", "WindowsIntegrations"];
        var originals = names.ToDictionary(name => name, name => typeof(Crest.App).GetProperty(name)!.GetValue(app));
        var islandField = typeof(Crest.App).GetField("_island", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var integrationFlag = typeof(Crest.App).GetField("_hadIntegrationEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalIsland = islandField.GetValue(app), originalFlag = integrationFlag.GetValue(app);
        Window? originalMainWindow = app.MainWindow;
        var settings = new AppSettings { Theme = "Dark", Motion = "Normal", EnabledModules = ModuleCatalog.All.ToList() };
        var store = new SettingsStore(directory);
        var fixtureClock = new Clock();
        using var local = new LocalHub(store, () => settings, fixtureClock);
        using var hardware = new HardwareHub(); hardware.Dispose();
        using var policy = new DesktopPolicy(() => settings);
        using var clipboard = new ClipboardHistoryHub(store, () => new(false, 7), automaticRetention: false);
        using var listener = new ClipboardHistoryListener(clipboard);
        IslandWindow? island = null;
        InteractionController? controller = null;
        SettingsWindow? window = null;
        bool integrationsInitialized = false;
        var renders = new List<string>();
        var activityFixtures = new List<object>();
        try
        {
            if (originalIsland is IslandWindow prior) prior.Suppress();
            Set(app, "Preferences", settings); Set(app, "Store", store); Set(app, "Local", local);
            Set(app, "Hardware", hardware); Set(app, "Policy", policy); Set(app, "Workflows", new LocalWorkflowHub(store));
            Set(app, "ClipboardHistory", clipboard); Set(app, "ClipboardListener", listener);
            Invoke(app, "InitializeIntegrations"); integrationsInitialized = true;
            island = new IslandWindow(app);
            controller = new InteractionController(policy, () => settings, () => island.PixelBounds, () => island.HoldsInteraction, island.ContainsVisiblePoint);
            Set(app, "Controller", controller); islandField.SetValue(app, island);
            typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, true);
            var machine = Field<InteractionStateMachine>(controller, "_machine");
            var clip = Field<RectangleGeometry>(island, "_clip");
            var slide = Field<TranslateTransform>(island, "_slide");
            var detailSlide = Field<TranslateTransform>(island, "_detailSlide");
            var detail = Field<Border>(island, "_detail");
            var dock = Field<Border>(island, "_dock");
            var expanded = Field<ActivityDeck>(island, "_expanded");
            var compact = Field<Grid>(island, "_compact");
            var shell = Field<Grid>(island, "_shell");
            var surface = Field<Grid>(island, "_surface");
            var secondary = Field<Button[]>(island, "_secondary");
            var labels = Field<Dictionary<string, TextBlock>>(island, "_labels");
            var compactAction = Field<Button>(island, "_compactPlay");
            var compactDetails = Field<Button>(island, "_compactDetails");
            void Reveal()
            {
                machine.SetEligibility(false, false); machine.SetEligibility(true, false); machine.RevealAccessibility();
                island.Reveal(controller.TransitionVersion);
            }
            bool NoClocks() => IslandProbe.NoMotionClocks(island);
            bool Finite() => double.IsFinite(clip.Rect.Width) && double.IsFinite(clip.Rect.Height) && clip.Rect.Width > 0 && clip.Rect.Height > 0
                && double.IsFinite(detailSlide.Y) && island.Opacity is >= 0 and <= 1;
            void RenderActivity(IslandWindow current, string file)
            {
                current.Refresh(); current.UpdateLayout();
                var activity = IslandProbe.Selected(current) ?? throw new InvalidOperationException("An activity preview requires a real selected activity.");
                if (IslandProbe.Presentation(current) != "Activity" || expanded.CurrentActivity?.Id != activity.Id)
                    throw new InvalidOperationException("The activity preview must show the same selected activity in the shared envelope.");
                Render(surface, Path.Combine(output, file), 144); renders.Add(file);
                activityFixtures.Add(new { file, kind = activity.Kind.ToString(), activity.Id, activity.EntityId, activity.Title,
                    activity.Subtitle, activity.Value, activity.Phase, activity.Progress,
                    source = activity.Kind switch {
                        IslandActivityKind.Download => "Real DownloadService request through a test-only HTTP handler; held in flight",
                        IslandActivityKind.Ai => "Real AiAssistantService request through a test-only HTTP handler; held in flight",
                        _ => "Real LocalHub activity using an isolated store and controllable clock"
                    } });
            }

            settings.Motion = "Reduced"; Design.Apply(settings); Reveal(); await Task.Delay(120);
            check(IslandProbe.Selected(island) == null && !expanded.IsVisible && secondary.All(button => !button.IsVisible)
                && Field<IReadOnlyList<IslandActivity>>(island, "_activities").Count == 0 && local.Timers.Count == 0 && local.Focus.Phase == TimerPhase.Idle,
                "Idle shows a quiet compact envelope without fabricated media, timers, battery cards or activity bubbles");
            compactDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            check(IslandProbe.Presentation(island) == "Tools" && !expanded.IsVisible && Field<IReadOnlyList<IslandActivity>>(island, "_activities").Count == 0,
                "An intentional idle expansion reveals the tools catalog without promoting unavailable modules to live activities");
            check(detail.IsDescendantOf(shell) && dock.IsDescendantOf(shell) && compact.IsDescendantOf(shell) && expanded.IsDescendantOf(shell)
                && IsTransparent(detail.Background) && IsTransparent(dock.Background),
                "Activity, tools and detail content belong to one morphing shell without separately painted dock or detail surfaces");

            string timerId = local.AddTimer(TimeSpan.FromMinutes(5), "Tea alongside focus");
            local.StartFocusSession(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), 2, "Write proposal");
            IslandProbe.Collapse(island); island.Refresh();
            var focusActivity = IslandProbe.Selected(island)!;
            compactDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120); island.UpdateLayout();
            check(focusActivity.Kind == IslandActivityKind.Focus && focusActivity.EntityId == local.Focus.Id
                && IslandProbe.Presentation(island) == "Activity" && expanded.CurrentActivity?.Id == focusActivity.Id
                && Field<TextBlock>(expanded, "_largeValue").Text == "25:00" && local.Timers.Single(timer => timer.Id == timerId).RemainingSeconds == 300,
                "Compact focus expands into that exact session and its real time instead of an independent countdown preset");
            Rect focusRect = IslandProbe.Target(island);
            Render(surface, Path.Combine(output, "fixture-focus-context.png"), 144); renders.Add("fixture-focus-context.png");
            Rect renderedFocusBounds = OpaqueBounds(Snapshot(surface, 96));
            check(RectDistance(renderedFocusBounds, focusRect) <= 4,
                "Fixture rasterization preserves the envelope's actual proportions instead of stretching its content bounds");
            var staleFocusAction = expanded.CurrentActivity!;
            var expandedPrimary = Field<Button>(expanded, "_primary");
            bool singleVisibleAction = !compactAction.IsVisible && expandedPrimary.IsVisible;
            IslandProbe.Collapse(island); island.Refresh();
            for (int index = 0; index < 12; index++)
                compactAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(180); island.Refresh();
            bool paused = local.Focus.Phase == TimerPhase.Paused && local.Timers.Single(timer => timer.Id == timerId).Phase == TimerPhase.Running
                && IslandProbe.Selected(island)?.Id == focusActivity.Id;
            await IslandActivityCatalog.InvokePrimaryAsync(app, staleFocusAction);
            check(paused && local.Focus.Phase == TimerPhase.Paused,
                "A stale running-focus action cannot reinterpret itself after the selected session has paused");
            compactDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120); island.Refresh();
            singleVisibleAction &= !compactAction.IsVisible && expandedPrimary.IsVisible;
            for (int index = 0; index < 12; index++) expandedPrimary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(180); island.Refresh();
            check(singleVisibleAction && local.Focus.Phase == TimerPhase.Running && local.Timers.Single(timer => timer.Id == timerId).Phase == TimerPhase.Running
                && expanded.CurrentActivity?.Id == focusActivity.Id && expanded.CurrentActivity.Phase == "Running",
                "Compact and expanded controls act on the same focus with one visible primary action and preserve the independent timer");

            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Music))!.SetValue(hardware, new MusicSnapshot {
                HasSession = true, Title = "Fixture song", Artist = "Fixture artist", PlayerName = "Fixture player",
                IsPlaying = true, CanPlayPause = false, PositionSeconds = 20, DurationSeconds = 200 });
            IslandProbe.Collapse(island); island.Refresh(); await Idle();
            check(secondary.Count(button => button.IsVisible) == 2 && Field<IReadOnlyList<IslandActivity>>(island, "_activities").Count == 3
                && secondary.Where(button => button.IsVisible).All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))),
                "Only concurrent real focus, timer and media activities create named minimal secondary bubbles");
            Render(surface, Path.Combine(output, "fixture-concurrent-compact.png"), 144); renders.Add("fixture-concurrent-compact.png");
            var musicBubble = secondary.Single(button => button.IsVisible && AutomationProperties.GetName(button).Contains("Fixture song", StringComparison.Ordinal));
            musicBubble.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120); island.UpdateLayout();
            check(IslandProbe.Selected(island)?.Kind == IslandActivityKind.Music && expanded.CurrentActivity?.Id == IslandProbe.Selected(island)?.Id
                && labels["compactTitle"].Text == "Fixture song" && IslandProbe.Presentation(island) == "Activity",
                "Selecting a minimal bubble enlarges the same media activity and updates the shared compact header");
            Rect musicRect = IslandProbe.Target(island);
            check(focusRect.Height != musicRect.Height && focusRect.Width == musicRect.Width && focusRect.Height < 500 && musicRect.Height < 500,
                "Expanded envelope height follows the selected activity while retaining its stable horizontal anchor");
            Render(surface, Path.Combine(output, "fixture-music-context.png"), 144); renders.Add("fixture-music-context.png");
            void SetSeekMedia(string title, bool canSeek, double duration)
            {
                typeof(HardwareHub).GetProperty(nameof(HardwareHub.Music))!.SetValue(hardware, new MusicSnapshot {
                    HasSession = true, Title = title, Artist = "Fixture artist", PlayerName = "Fixture player", IsPlaying = true,
                    CanPlayPause = false, CanSeek = canSeek, PositionSeconds = 20, DurationSeconds = duration });
                expanded.Refresh();
            }
            var seek = Field<Slider>(expanded, "_seek");
            var seekEdit = Field<PendingControlEdit>(expanded, "_seekEdit");
            var seekCommit = Field<DispatcherTimer>(expanded, "_seekCommit");
            void RequestSeek()
            {
                typeof(ActivityDeck).GetField("_seekPointerDown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(expanded, true);
                seek.Value = 80;
                if (!seekEdit.NeedsWrite) throw new InvalidOperationException("Pending seek fixture did not capture the requested position.");
            }
            SetSeekMedia("Fixture song", true, 200); RequestSeek(); SetSeekMedia("Fixture song", false, 200);
            check(!seekEdit.NeedsWrite && !seekCommit.IsEnabled && !seek.IsEnabled,
                "Losing media seek capability cancels the pending edit and debounce before any provider write");
            SetSeekMedia("Fixture song", true, 200); RequestSeek(); SetSeekMedia("Fixture song", true, 0);
            check(!seekEdit.NeedsWrite && !seekCommit.IsEnabled && !seek.IsEnabled && Field<Grid>(expanded, "_seekRow").Visibility == Visibility.Collapsed,
                "Unknown media duration hides the seek timeline and clears its pending edit without inventing a length");
            SetSeekMedia("Fixture song", true, 200); RequestSeek(); SetSeekMedia("Replacement track", true, 200);
            check(!seekEdit.NeedsWrite && !seekCommit.IsEnabled && seek.Value == 20 && expanded.CurrentActivity?.Title == "Replacement track",
                "A same-player track replacement discards the previous song's pending seek and restores actual provider position");
            typeof(HardwareHub).GetProperty(nameof(HardwareHub.Music))!.SetValue(hardware, new MusicSnapshot());
            island.Refresh(); await Idle();
            check(IslandProbe.Selected(island)?.Kind == IslandActivityKind.Focus && IslandProbe.Presentation(island) == "Compact" && !expanded.IsVisible
                && secondary.Where(button => button.IsVisible).All(button => button.Tag is IslandActivity source && source.Kind != IslandActivityKind.Music),
                "Ending the selected media source restores a real remaining activity without retaining a placeholder bubble");

            settings.Motion = "Normal"; Design.Apply(settings); island.Suppress(); Reveal(); await Task.Delay(380);
            var directShape = new RectangleGeometry(clip.Rect, 24, 24);
            var directMotion = new SpringMotion();
            var directTarget = new Rect(20, 0, 440, 320);
            Rect directFrom = directShape.Rect;
            directMotion.Rect(directShape, RectangleGeometry.RectProperty, directTarget);
            bool directContinuity = RectDistance(directShape.Rect, directFrom) < .01;
            await Task.Delay(40);
            Rect directBeforeRetarget = directShape.Rect;
            directMotion.Rect(directShape, RectangleGeometry.RectProperty, directFrom);
            directContinuity &= RectDistance(directShape.Rect, directBeforeRetarget) < .01;
            directMotion.StopAll();
            check(directContinuity && !directShape.HasAnimatedProperties,
                "Native spring attachment and direct retarget keep current geometry before the first compositor frame");
            bool continuous = true;
            double largestRetargetJump = 0;
            for (int cycle = 0; cycle < 30; cycle++)
            {
                Rect previous = clip.Rect;
                island.Expand(new[] { "Overview", "Tools", "Timer", "Controls", "Shelf" }[cycle % 5]);
                largestRetargetJump = Math.Max(largestRetargetJump, RectDistance(previous, clip.Rect));
                continuous &= RectDistance(previous, clip.Rect) < .01 && Finite();
                await Task.Delay(12);
                previous = clip.Rect;
                Invoke(island, "Collapse");
                largestRetargetJump = Math.Max(largestRetargetJump, RectDistance(previous, clip.Rect));
                continuous &= RectDistance(previous, clip.Rect) < .01 && Finite();
                await Task.Delay(12);
            }
            island.Expand("Timer");
            await Task.Delay(900);
            if (!(continuous && clip.Rect == IslandProbe.Target(island) && detail.IsVisible && detail.Opacity == 1 && Finite()))
                Console.Error.WriteLine($"Rapid morph diagnostics: continuous={continuous}, max retarget jump={largestRetargetJump:F4}, clip={clip.Rect}, target={IslandProbe.Target(island)}, detail={detail.Visibility}/{detail.Opacity}, clocksStopped={NoClocks()}, finite={Finite()}.");
            check(continuous && clip.Rect == IslandProbe.Target(island) && detail.IsVisible && detail.Opacity == 1 && Finite(),
                "Thirty rapid compact, activity, tools and detail reversals retain geometry continuity and settle to the final module");
            check(NoClocks(), "Settled island springs release every shape, opacity and translation clock");

            Invoke(island, "Collapse"); await Task.Delay(18); island.Expand("Controls"); await Task.Delay(18);
            machine.SetEligibility(false, false); island.Suppress();
            await Task.Delay(900);
            check(!island.IsVisible && !island.IsHitTestVisible && NoClocks(),
                "Suppression during intersecting shell, deck and detail springs cancels all clocks without a stale resurrection");

            Reveal(); island.Expand("Timer"); await Task.Delay(20);
            island.CloseAnimated(controller.TransitionVersion); await Task.Delay(20);
            Reveal(); island.Expand("Music"); await Task.Delay(900);
            check(island.IsVisible && island.IsHitTestVisible && island.Opacity == 1 && clip.Rect == IslandProbe.Target(island)
                && Field<string>(island, "_module") == "Music" && NoClocks(),
                "Revealing during an animated close preserves the new live surface and rejects its obsolete close callback");

            Invoke(island, "Collapse"); await Task.Delay(15); island.Expand("Timer"); await Task.Delay(15);
            settings.Motion = "Reduced"; Design.Apply(settings); island.Refresh();
            bool reduced = clip.Rect == IslandProbe.Target(island) && slide.Y == 0 && detailSlide.Y == 0;
            await Task.Delay(900);
            check(reduced && NoClocks() && clip.Rect == IslandProbe.Target(island) && detailSlide.Y == 0,
                "Changing motion preference during rapid morphing stops every spring and retains the selected reduced-motion layout");
            Invoke(island, "Collapse");
            check(clip.Rect == Field<Rect>(island, "_compactRect") && compact.IsHitTestVisible && !expanded.IsVisible && !detail.IsVisible && NoClocks(),
                "Reduced-motion contraction restores an immediately usable compact surface without surviving hidden clocks");

            typeof(IslandWindow).GetField("_selectedActivityId", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(island, "timer:" + timerId);
            island.Refresh(); island.Expand("Overview"); await Idle();
            if (IslandProbe.Selected(island)?.EntityId != timerId || local.Timers.Single(timer => timer.Id == timerId).RemainingSeconds != 300)
                throw new InvalidOperationException("The timer preview must retain the genuine independent five-minute countdown.");
            RenderActivity(island, "fixture-timer-context.png");
            string reminderId = local.AddReminder("Fixture reminder: review tomorrow's plan", fixtureClock.UtcNow);
            try
            {
                IslandProbe.Collapse(island); island.Refresh(); island.Expand("Overview"); await Idle();
                if (IslandProbe.Selected(island)?.EntityId != reminderId || local.Reminders.Single(reminder => reminder.Id == reminderId).Phase != ReminderPhase.Due)
                    throw new InvalidOperationException("The reminder preview must show the genuine due reminder.");
                RenderActivity(island, "fixture-reminder-context.png");
            }
            finally { local.DismissReminder(reminderId); }

            await OptionalActivityWiringChecks.Run(app, island, settings, check, RenderActivity);
            Reveal(); await Task.Delay(120);

            island.Expand("Tools"); island.FocusForKeyboard(); await Idle();
            var firstModule = Descendants<Button>(dock).First(button => button.IsVisible && button.IsEnabled && AutomationProperties.GetAutomationId(button).StartsWith("module-", StringComparison.Ordinal));
            firstModule.Focus(); Keyboard.Focus(firstModule);
            bool keyboard = ReferenceEquals(Keyboard.FocusedElement, firstModule);
            firstModule.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            keyboard &= IslandProbe.Presentation(island) == "Detail" && island.IsKeyboardFocusWithin;
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(island)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            island.RaiseEvent(escape);
            check(keyboard && escape.Handled && controller.State == IslandState.Closing,
                "Keyboard activation enters an intentional tools detail and Escape reaches the shared visibility controller");
            island.CloseAnimated(controller.TransitionVersion); await Task.Delay(180);

            // Local activities and hardware snapshots above are isolated test fixtures, never application defaults.
            local.ResetProductivity(); hardware.ClearBatteryNotices();
            settings.Motion = "Reduced";
            Reveal(); await Task.Delay(120);
            ThemePresentationChecks.CheckPalettes(settings, Field<System.Windows.Shapes.Path>(island, "_compactBrand"), check);
            foreach (string theme in new[] { "Dark", "Light" })
            {
                settings.Theme = theme; Design.Apply(settings); island.Refresh();
                foreach (string module in new[] { "Overview" }.Concat(ModuleCatalog.All))
                {
                    island.Expand(module); island.UpdateLayout();
                    string file = $"{theme.ToLowerInvariant()}-island-{module.ToLowerInvariant()}.png";
                    Render(surface, Path.Combine(output, file), 144); renders.Add(file);
                }
                Invoke(island, "Collapse"); island.UpdateLayout();
                string compactFile = $"{theme.ToLowerInvariant()}-island-compact.png";
                Render(compact, Path.Combine(output, compactFile), 192); renders.Add(compactFile);
            }
            foreach (string theme in new[] { "System", "Ocean", "Forest", "Rose" })
            {
                settings.Theme = theme; Design.Apply(settings); island.Refresh(); island.Expand("Overview"); island.UpdateLayout();
                string file = $"{theme.ToLowerInvariant()}-island-overview.png";
                Render(surface, Path.Combine(output, file), 144); renders.Add(file);
            }
            island.Suppress();
            window = new SettingsWindow(app); window.Show(); await Idle();
            await ThemePresentationChecks.CheckPicker(window, settings, store, check);
            var navigation = Field<StackPanel>(window, "_nav");
            var settingsLayout = (FrameworkElement)window.Content;
            bool settingsFit = true;
            foreach (string theme in new[] { "Dark", "Light" })
            {
                settings.Theme = theme; Design.Apply(settings);
                foreach (var size in new[] { new Size(960, 760), new Size(700, 560) })
                {
                    window.Width = size.Width; window.Height = size.Height; await Idle();
                    foreach (Button button in navigation.Children.OfType<Button>())
                    {
                        string section = AutomationProperties.GetName(button);
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
                        if (section == "Appearance")
                            Descendants<RadioButton>(window).Single(choice => AutomationProperties.GetName(choice) == theme).IsChecked = true;
                        var page = Field<StackPanel>(window, "_page");
                        settingsFit &= page.ActualWidth > 0 && page.Children.Count > 0;
                        foreach (var control in Descendants<Control>(page).Where(control => control.IsVisible && control is Button or TextBox or ComboBox or CheckBox or RadioButton or Slider))
                        {
                            if (FitsHorizontally(control, page)) continue;
                            settingsFit = false;
                            Rect bounds = control.TransformToAncestor(page).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                            Console.Error.WriteLine($"Horizontal overflow in {theme}/{section}/{size.Width}: {control.GetType().Name} '{AutomationProperties.GetName(control)}', {bounds.Left:F1}..{bounds.Right:F1}, page width {page.ActualWidth:F1}.");
                        }
                        string file = $"{theme.ToLowerInvariant()}-settings-{section.ToLowerInvariant()}-{(int)size.Width}.png";
                        Render(settingsLayout, Path.Combine(output, file), 96); renders.Add(file);
                    }
                }
            }
            foreach (string theme in new[] { "System", "Ocean", "Forest", "Rose" })
            {
                navigation.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Appearance")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();
                Descendants<RadioButton>(window).Single(choice => AutomationProperties.GetName(choice) == theme).IsChecked = true;
                string file = $"{theme.ToLowerInvariant()}-settings-appearance-700.png";
                Render(settingsLayout, Path.Combine(output, file), 96); renders.Add(file);
            }
            check(settingsFit, "Every Settings page keeps its visible controls within the standard and compact window widths in light and dark themes");
            check(!settings.Weather.Enabled && !settings.Calendar.Enabled && !settings.Downloads.Enabled && !settings.Ai.Enabled
                && !settings.WindowsIntegrations.NotificationsEnabled && app.Weather.Current == null && app.Calendar.Events.Count == 0
                && app.Downloads.Items.Count == 0 && app.Ai.Messages.Count == 0 && app.WindowsIntegrations.Notifications.Count == 0,
                "Rendering every island and Settings screen retains disabled providers and introduces no simulated service activity");
            File.WriteAllText(Path.Combine(output, "render-manifest.json"), JsonSerializer.Serialize(new {
                kind = "Static WPF fixtures using isolated local activities, explicitly labeled hardware snapshots, genuinely in-flight test HTTP services and empty optional service state for catalog/settings samples", renders, activityFixtures,
                limits = "First scroll viewport per page; not a native desktop screenshot, frame-rate measurement or hardware integration acceptance."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            window?.Close(); island?.Suppress(); island?.Close(); controller?.Dispose();
            if (integrationsInitialized) Invoke(app, "DisposeIntegrations");
            foreach (var original in originals) Set(app, original.Key, original.Value);
            islandField.SetValue(app, originalIsland); integrationFlag.SetValue(app, originalFlag); app.MainWindow = originalMainWindow;
            Design.Apply(app.EffectivePreferences);
            if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("CrestPremiumUI-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static double RectDistance(Rect left, Rect right) => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Width - right.Width) + Math.Abs(left.Height - right.Height);
    private static bool FitsHorizontally(FrameworkElement control, FrameworkElement page)
    {
        Rect bounds = control.TransformToAncestor(page).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
        return bounds.Left >= -.5 && bounds.Right <= page.ActualWidth + .5;
    }
    private static bool IsTransparent(Brush? brush) => brush == null || brush is SolidColorBrush solid && solid.Color.A == 0;
    private static void Set(Crest.App app, string name, object? value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Invoke(object instance, string name)
        => (instance is Crest.App ? typeof(Crest.App) : instance.GetType()).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!.Invoke(instance, null);
    private static async Task Idle() { await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await Task.Delay(15); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static RenderTargetBitmap Snapshot(FrameworkElement visual, int dpi)
    {
        visual.UpdateLayout();
        int width = (int)Math.Ceiling(visual.ActualWidth * dpi / 96d), height = (int)Math.Ceiling(visual.ActualHeight * dpi / 96d);
        if (width < 1 || height < 1) throw new InvalidOperationException("Empty contextual fixture render.");
        var bounds = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(IslandProbe.VisualBrush(visual), null, bounds);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(drawing);
        return bitmap;
    }
    private static void Render(FrameworkElement visual, string path, int dpi)
    {
        var bitmap = Snapshot(visual, dpi);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static Rect OpaqueBounds(BitmapSource bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        int left = bitmap.PixelWidth, top = bitmap.PixelHeight, right = -1, bottom = -1;
        for (int y = 0; y < bitmap.PixelHeight; y++)
            for (int x = 0; x < bitmap.PixelWidth; x++)
                if (pixels[y * stride + x * 4 + 3] >= 200)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < left ? Rect.Empty : new Rect(left, top, right - left + 1, bottom - top + 1);
    }
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-25T00:00:00Z");
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}

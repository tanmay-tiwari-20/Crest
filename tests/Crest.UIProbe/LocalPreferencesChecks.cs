using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;

internal static class LocalPreferencesChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check)
    {
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(tempParent, "CrestPreferencesUI-" + Guid.NewGuid().ToString("N")));
        const string display = "Crest isolated second display";
        const string defaults = "All displays (default)";
        string[] names = { "Preferences", "Store", "Policy", "Controller", "Hardware", "Local", "Workflows", "ClipboardHistory", "ClipboardListener" };
        var originals = names.ToDictionary(name => name, name => typeof(Crest.App).GetProperty(name)!.GetValue(app));
        var islandField = typeof(Crest.App).GetField("_island", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalIsland = islandField.GetValue(app);
        var settings = new AppSettings { Theme = "Dark" };
        settings.Displays[display] = new DisplayPreferences { Theme = "Dark" };
        var store = new SettingsStore(root);
        using var local = new LocalHub(store, () => settings, new PreferenceClock());
        using var hardware = new HardwareHub();
        hardware.Dispose();
        using var policy = new DesktopPolicy(() => settings);
        using var clipboard = new ClipboardHistoryHub(store, () => new(settings.ClipboardHistoryEnabled, settings.ClipboardRetentionDays), automaticRetention: false);
        using var listener = new ClipboardHistoryListener(clipboard);
        Set("Preferences", settings); Set("Store", store); Set("Local", local); Set("Hardware", hardware); Set("Policy", policy);
        Set("Workflows", new LocalWorkflowHub(store)); Set("ClipboardHistory", clipboard); Set("ClipboardListener", listener);
        var island = new IslandWindow(app);
        islandField.SetValue(app, island);
        new WindowInteropHelper(island).EnsureHandle();
        using var controller = new InteractionController(policy, () => app.EffectivePreferences, () => island.PixelBounds, () => island.HoldsInteraction, island.ContainsVisiblePoint);
        Set("Controller", controller);
        var window = new SettingsWindow(app);
        try
        {
            window.Show();
            await Idle();
            await Navigate("Appearance");
            Find<ComboBox>("Preference display").SelectedItem = display;
            await Idle();
            check(Descendants<RadioButton>(window).Select(AutomationProperties.GetName).SequenceEqual(ModuleCatalog.Themes),
                "Appearance settings expose all six saved theme choices in order");
            Find<RadioButton>("Ocean").IsChecked = true;
            check(settings.Displays[display].Theme == "Ocean" && settings.Theme == "Dark" && store.Load().Displays[display].Theme == "Ocean",
                "A display-specific theme persists independently of the default theme");
            Find<ComboBox>("Island size preset").SelectedItem = "Large";
            await Idle();
            check(settings.Displays[display].SizeScale == 1.2 && settings.SizeScale == 1, "A display-specific size preset changes only the selected display");
            Find<ComboBox>("Preference display").SelectedItem = defaults;
            await Idle();
            Find<RadioButton>("Rose").IsChecked = true;
            check(settings.Theme == "Rose" && settings.Displays[display].Theme == "Ocean", "Editing defaults preserves an existing display appearance override");

            await Navigate("Behavior");
            Find<ComboBox>("Preference display").SelectedItem = display;
            await Idle();
            var hover = Find<Slider>("Hover delay");
            hover.Value = 320;
            SaveRange(hover);
            var fullWidth = Find<CheckBox>("Use the full-width top edge");
            fullWidth.IsChecked = true;
            fullWidth.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(settings.Displays[display].DwellMs == 320 && settings.Displays[display].FullWidthTrigger
                && settings.DwellMs == 120 && !settings.FullWidthTrigger && store.Load().Displays[display].DwellMs == 320,
                "Display-specific trigger delay and coverage persist without changing defaults");
            Find<ComboBox>("Double-click gesture").SelectedItem = "Next module";
            Find<ComboBox>("Middle-click gesture").SelectedItem = "Collapse";
            Find<ComboBox>("Pointer wheel gesture").SelectedItem = "Switch modules";
            var loaded = store.Load();
            check(loaded.DoubleClickGesture == "Next module" && loaded.MiddleClickGesture == "Collapse" && loaded.WheelGesture == "Switch modules",
                "Pointer gesture controls persist the selected double-click, middle-click, and wheel actions");

            await Navigate("Modules");
            int previous = settings.ModuleOrder.IndexOf("Clipboard");
            await Click("Move Clipboard earlier");
            typeof(IslandWindow).GetMethod("BuildTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(island, null);
            var tabs = (StackPanel)typeof(IslandWindow).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
            var tabNames = Descendants<Button>(tabs).Select(AutomationProperties.GetAutomationId).Where(id => id.StartsWith("module-", StringComparison.Ordinal)).ToArray();
            check(settings.ModuleOrder.IndexOf("Clipboard") == previous - 1
                && Array.IndexOf(tabNames, "module-Clipboard") < Array.IndexOf(tabNames, "module-Workspaces")
                && store.Load().ModuleOrder.IndexOf("Clipboard") == previous - 1,
                "Module ordering controls update the persisted order and the intentional tools catalog");

            await Navigate("Workspaces");
            Find<TextBox>("New workspace name").Text = "Writing";
            await Click("Create workspace");
            check(settings.WorkspacePresets.Count == 1 && store.Load().WorkspacePresets.Single().Name == "Writing", "Workspace settings create and persist a named preset");
            Find<TextBox>("Website address for Writing").Text = "javascript:alert(1)";
            await Click("Add website");
            check(settings.WorkspacePresets.Single().Targets.Count == 0, "Workspace website creation rejects unsupported URI protocols");
            Find<TextBox>("Website address for Writing").Text = "https://example.test/work";
            await Click("Add website");
            check(settings.WorkspacePresets.Single().Targets.Single().Target == "https://example.test/work"
                && store.Load().WorkspacePresets.Single().Targets.Count == 1, "Workspace settings retain a valid explicitly selected website target");
            Find<TextBox>("Workspace name Writing").Text = "Research";
            await Click("Save workspace name");
            check(settings.WorkspacePresets.Single().Name == "Research" && settings.WorkspacePresets.Single().Targets.Count == 1, "Renaming a workspace preserves its targets");
            await Click("Remove example.test from Research");
            await Click("Remove workspace");
            check(settings.WorkspacePresets.Count == 0 && store.Load().WorkspacePresets.Count == 0, "Workspace removal clears only the selected preset and its saved references");

            await Navigate("Privacy");
            var optIn = Find<CheckBox>("Enable text clipboard history");
            check(optIn.IsChecked == false && !clipboard.Enabled, "Privacy settings start with clipboard capture off");
            optIn.IsChecked = true;
            optIn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var retention = Find<Slider>("Clipboard retention");
            retention.Value = 3;
            SaveRange(retention);
            check(clipboard.Enabled && clipboard.RetentionDays == 3 && store.Load().ClipboardHistoryEnabled, "Privacy opt-in and retention controls apply to the real clipboard history service");
            optIn.IsChecked = false;
            optIn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(!clipboard.Capturing && !File.Exists(Path.Combine(root, "clipboard.json")), "Turning capture off from Privacy settings erases the retained clipboard state");

            await Navigate("Appearance");
            await Click("Use defaults for this display");
            check(!settings.Displays.ContainsKey(display) && settings.ForDisplay(display).Theme == "Rose", "Removing a display override restores the current default appearance and trigger preferences");

            settings.Motion = "Reduced";
            island.Reveal(controller.TransitionVersion);
            await Task.Delay(120);
            var shell = (Grid)typeof(IslandWindow).GetField("_shell", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
            string CurrentModule() => (string)typeof(IslandWindow).GetField("_module", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            shell.RaiseEvent(wheel);
            check(wheel.Handled && CurrentModule() == settings.ModuleOrder.First(settings.EnabledModules.Contains), "A configured surface wheel gesture opens the next enabled module in saved order");
            var middle = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            shell.RaiseEvent(middle);
            check(middle.Handled && !(bool)typeof(IslandWindow).GetField("_wide", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!, "A configured middle-click surface gesture collapses the visible island");
            island.Expand("Timer");
            var control = Descendants<Button>(island).First(button => button.IsVisible);
            var controlClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            control.RaiseEvent(controlClick);
            check(CurrentModule() == "Timer" && (bool)typeof(IslandWindow).GetField("_wide", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!, "Pointer gestures leave interactive controls available without collapsing their panel");
        }
        finally
        {
            window.Close();
            island.Close();
            listener.Dispose();
            clipboard.Reset();
            clipboard.Dispose();
            local.Dispose();
            controller.Dispose();
            policy.Dispose();
            foreach (var original in originals) Set(original.Key, original.Value);
            islandField.SetValue(app, originalIsland);
            if (app.Preferences != null) Design.Apply(app.EffectivePreferences);
            if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestPreferencesUI-", StringComparison.Ordinal)
                && Directory.Exists(root)) Directory.Delete(root, true);
        }

        void Set(string name, object? value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
        T Find<T>(string name) where T : DependencyObject => Descendants<T>(window).Single(control => AutomationProperties.GetName(control) == name);
        async Task Navigate(string name)
        {
            var navigation = (StackPanel)typeof(SettingsWindow).GetField("_nav", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            navigation.Children.OfType<Button>().Single(button => AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
        }
        async Task Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(); }
    }

    private static void SaveRange(Slider slider)
    {
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider)!, Environment.TickCount, Key.Right)
        { RoutedEvent = Keyboard.PreviewKeyUpEvent });
    }
    private static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class PreferenceClock : ITimerClock
    {
        public long MonotonicMilliseconds => 0;
        public DateTimeOffset UtcNow => new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    }
}

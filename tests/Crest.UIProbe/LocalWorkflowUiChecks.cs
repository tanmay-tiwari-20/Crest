using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;

/// <summary>Isolated reference management with real controls; never launches targets or opens file dialogs.</summary>
internal static class LocalWorkflowUiChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check, string outputDirectory)
    {
        app.Dispatcher.VerifyAccess();
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(Path.Combine(tempParent, "CrestWorkflowUI-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(outputDirectory);
        string[] properties = ["Preferences", "Store", "Local", "Hardware", "Policy", "Controller", "Workflows"];
        var originals = properties.ToDictionary(name => name, name => typeof(Crest.App).GetProperty(name)!.GetValue(app));
        var islandField = typeof(Crest.App).GetField("_island", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalIsland = islandField.GetValue(app);
        if (originalIsland is IslandWindow previousIsland) previousIsland.Suppress();
        Window? originalMainWindow = app.MainWindow;
        var settings = new AppSettings { Theme = "Ocean", Motion = "Reduced", EnabledModules = ["Capture", "Shelf"] };
        var store = new SettingsStore(Path.Combine(directory, "state"));
        using var local = new LocalHub(store, () => settings, new Clock());
        using var hardware = new HardwareHub();
        hardware.Dispose(); // No hardware/media sampling is needed for reference controls.
        using var policy = new DesktopPolicy(() => settings); // No native policy hooks or global hotkeys.
        var workflows = new LocalWorkflowHub(store);
        IslandWindow? island = null;
        InteractionController? controller = null;
        try
        {
            Set(app, "Preferences", settings); Set(app, "Store", store); Set(app, "Local", local);
            Set(app, "Hardware", hardware); Set(app, "Policy", policy); Set(app, "Workflows", workflows);
            island = new IslandWindow(app);
            controller = new InteractionController(policy, () => settings, () => island.PixelBounds, () => island.HoldsInteraction, island.ContainsVisiblePoint);
            Set(app, "Controller", controller); islandField.SetValue(app, island);
            typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, true);
            var machine = Field<InteractionStateMachine>(controller, "_machine");
            machine.SetEligibility(true, false); machine.RevealAccessibility();
            island.Reveal(controller.TransitionVersion);
            await Task.Delay(120);

            async Task Show(string module, string theme)
            {
                settings.Theme = theme; Design.Apply(settings);
                island.Expand(module); Invoke(island, "BuildBody");
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                island.BeginAnimation(UIElement.OpacityProperty, null); island.Opacity = 1;
                var detail = Field<Border>(island, "_detail");
                detail.BeginAnimation(UIElement.OpacityProperty, null); detail.Opacity = 1;
                island.UpdateLayout();
            }
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 220, 130, 20, 255, 60, 190, 130, 255, 190, 60, 180, 255, 220, 210, 40, 255 }, 8);
            image.Freeze();
            string capture = Path.Combine(directory, "Selected capture.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(capture)) encoder.Save(stream);
            byte[] originalCaptureBytes = File.ReadAllBytes(capture);
            workflows.SelectCaptures([capture]);
            await Show("Capture", "Ocean");
            var body = Field<StackPanel>(island, "_body");
            check(FindButton(body, "Take a screenshot").IsVisible && FindButton(body, "Select recent captures").IsVisible
                && FindButton(body, Path.GetFileName(capture)).IsVisible && workflows.Captures.Count == 1,
                "Capture module exposes Windows screenshot launch and only explicitly selected recent references");
            var restoredCaptures = new LocalWorkflowHub(store);
            check(restoredCaptures.Captures.Count == 1 && restoredCaptures.Captures[0].Path == capture,
                "Selected capture references survive isolated storage restart without importing other files");
            Render(Field<Border>(island, "_detail"), Path.Combine(outputDirectory, "p2-captures-ocean.png"));

            string movedCapture = Path.Combine(directory, "Relocated capture.png");
            File.Move(capture, movedCapture);
            await Show("Capture", "Ocean");
            check(FindButton(body, "Selected capture.png · missing").IsVisible && !workflows.Captures[0].Available,
                "Capture module identifies a missing selected source without deleting its reference");
            workflows.SelectCaptures([movedCapture]);
            await Show("Capture", "Ocean");
            FindButton(body, "Remove capture reference Selected capture.png").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(workflows.Captures.Count == 1 && workflows.Captures[0].Path == movedCapture
                && File.ReadAllBytes(movedCapture).SequenceEqual(originalCaptureBytes),
                "Removing an obsolete capture reference preserves the selected replacement image bytes");
            FindButton(body, "Clear capture references").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(workflows.Captures.Count == 0 && new LocalWorkflowHub(store).Captures.Count == 0 && File.Exists(movedCapture),
                "Clear capture references clears durable paths and leaves source images intact");

            string file = Path.Combine(directory, "Pinned work notes.txt"), temporaryFile = Path.Combine(directory, "Session note.txt");
            File.WriteAllText(file, "Pinned source bytes remain unchanged.");
            File.WriteAllText(temporaryFile, "Unpinned source bytes remain unchanged.");
            local.AddShelfFiles([file, temporaryFile]);
            string pinnedId = local.Shelf.Single(x => x.Path == file).Id;
            await Show("Shelf", "Forest");
            FindButton(body, "Pin Pinned work notes.txt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(local.Shelf.Single(x => x.Id == pinnedId).Pinned && FindButton(body, "Unpin Pinned work notes.txt").IsVisible,
                "Shelf pin control updates the intended reference and exposes its unpin action");
            using (var restoredShelf = new LocalHub(store, () => settings, new Clock()))
                check(restoredShelf.Shelf.Count == 1 && restoredShelf.Shelf[0].Id == pinnedId && restoredShelf.Shelf[0].Pinned
                    && restoredShelf.Shelf[0].Path == file,
                    "Pinned shelf paths persist while unpinned references remain limited to their session");
            Render(Field<Border>(island, "_detail"), Path.Combine(outputDirectory, "p2-shelf-forest.png"));

            string movedFile = Path.Combine(directory, "Recovered work notes.txt");
            File.Move(file, movedFile); local.RefreshShelf();
            await Show("Shelf", "Rose");
            check(FindButton(body, "Locate Pinned work notes.txt").IsVisible && FindButton(body, "Open nearby folder").IsVisible
                && !local.Shelf.Single(x => x.Id == pinnedId).Available && local.ShelfRecoveryFolder(pinnedId) == directory,
                "Missing pinned shelf entries expose locate and nearest-existing-folder recovery controls");
            Render(Field<Border>(island, "_detail"), Path.Combine(outputDirectory, "p2-shelf-missing-rose.png"));
            check(local.RelinkShelf(pinnedId, movedFile) is null, "Explicit shelf relinking accepts an existing replacement file");
            await Show("Shelf", "Forest");
            check(local.Shelf.Single(x => x.Id == pinnedId) is { Pinned: true, Available: true }
                && FindButton(body, "Unpin Recovered work notes.txt").IsVisible
                && !Descendants<Button>(body).Any(x => AutomationProperties.GetName(x).StartsWith("Locate ", StringComparison.Ordinal)),
                "Recovered shelf UI retains reference identity and pin and removes obsolete missing-source actions");
            FindButton(body, "Remove reference to Recovered work notes.txt").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(local.Shelf.All(x => x.Id != pinnedId) && File.ReadAllText(movedFile) == "Pinned source bytes remain unchanged."
                && File.ReadAllText(temporaryFile) == "Unpinned source bytes remain unchanged."
                && !File.Exists(Path.Combine(store.DirectoryPath, "shelf.json")),
                "Removing the last pinned shelf reference clears durable state and preserves every source file");
        }
        finally
        {
            island?.Suppress(); island?.Close(); controller?.Dispose(); local.Dispose();
            foreach (var property in originals) Set(app, property.Key, property.Value);
            islandField.SetValue(app, originalIsland); app.MainWindow = originalMainWindow;
            Design.Apply(app.EffectivePreferences);
            if (directory.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("CrestWorkflowUI-", StringComparison.Ordinal) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
    private static Button FindButton(DependencyObject parent, string name)
        => Descendants<Button>(parent).Single(button => AutomationProperties.GetName(button) == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Set(Crest.App app, string name, object? value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(IslandWindow island, string name) => typeof(IslandWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(island, null);
    private static void Render(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) throw new InvalidOperationException("Reference panel has no renderable layout.");
        var target = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(IslandProbe.VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        target.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private sealed class Clock : ITimerClock
    {
        public long MonotonicMilliseconds => 0;
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-11-19T00:00:00Z");
    }
}

using System.IO;
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
using Crest.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--")) ?? "artifacts/productivity-qa");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "CrestUIProbe-" + Guid.NewGuid().ToString("N"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
                    File.WriteAllText(Path.Combine(output, "ui-outcomes.json"), JsonSerializer.Serialize(new { recordedAtUtc = DateTimeOffset.UtcNow, assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(TimerToolsPanel).Assembly.Location))).ToLowerInvariant(), passed = checks.Count, checks, limits = "Rendered DPI samples are layout evidence; physical mixed-DPI and screen-reader acceptance are separate gates." }, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"PASS {checks.Count} productivity UI outcomes; 16 theme/DPI renders in {output}");
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
        if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("CrestUIProbe-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        return exitCode;
    }

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
    private static void Render(FrameworkElement visual, string path, int dpi)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi / 96d), (int)Math.Ceiling(visual.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class ProbeClock : ITimerClock
    {
        public long MonotonicMilliseconds { get; private set; }
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        public void Advance(double seconds) { MonotonicMilliseconds += (long)(seconds * 1000); UtcNow += TimeSpan.FromSeconds(seconds); }
    }
}

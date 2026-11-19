using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Features;
using Crest.UI;

internal static class HardwareP2Checks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var hub = new HardwareHub();
        await hub.RefreshAudioOutputsAsync();
        var before = hub.AudioOutputs.ToArray();
        string? invalid = await hub.SwitchAudioOutputAsync("Crest-probe-absent-output");
        check(invalid is not null && before.SequenceEqual(hub.AudioOutputs),
            "Audio selection rejects an absent endpoint without changing the connected outputs");
        var links = new List<string>();
        var panel = new P2SystemPanel(hub, links.Add, _ => { });
        foreach (string name in new[] { "Windows sound settings", "Windows Wi-Fi settings", "Windows Bluetooth settings" })
            Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(links.SequenceEqual(new[] { "ms-settings:sound", "ms-settings:network-wifi", "ms-settings:bluetooth" }),
            "Audio and connectivity panels route the intended Windows settings links");
        check(Descendants<ListBox>(panel).Single().Items.Count == hub.AudioOutputs.Count
            && Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button) == "Switch audio output").IsEnabled == (hub.AudioOutputs.Count > 0),
            "Audio selection renders connected endpoints and disables switching when none are available");
        hub.SetVisible(true);
        for (int attempt = 0; attempt < 6 && hub.SystemStatistics.CpuPercent is null; attempt++) await Task.Delay(1000);
        panel.Refresh();
        check(hub.SystemStatistics.TotalMemoryBytes > 0 && hub.SystemStatistics.CpuPercent is >= 0 and <= 100,
            "Visible system statistics read real Windows physical memory and bounded CPU counters");
        hub.SetVisible(false);
        var stopped = hub.SystemStatistics;
        await Task.Delay(1200);
        check(ReferenceEquals(stopped, hub.SystemStatistics), "System-statistics sampling stops when Crest is hidden");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}

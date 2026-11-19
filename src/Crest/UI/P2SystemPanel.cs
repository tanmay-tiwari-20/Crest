using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Features;

namespace Crest.UI;

public sealed class P2SystemPanel : StackPanel
{
    private readonly HardwareHub _hub;
    private readonly Action<string> _showMessage;
    private readonly ListBox _outputs = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id", MaxHeight = 100, MinHeight = 36 };
    private readonly TextBlock _audio = Design.Text("", 11, "Muted");
    private readonly TextBlock _cpu = Design.Text("", 12), _memory = Design.Text("", 12), _network = Design.Text("", 12);
    private readonly TextBlock _statisticsStatus = Design.Text("", 10, "Muted");
    private readonly TextBlock _wifi = Design.Text("", 12), _bluetooth = Design.Text("", 12);
    private readonly Button _switch;
    private string _outputSignature = "";
    private bool _switching;

    public P2SystemPanel(HardwareHub hub, Action<string> openSettings, Action<string> showMessage)
    {
        _hub = hub; _showMessage = showMessage;
        Margin = new Thickness(0, 14, 0, 0);
        Children.Add(Design.Text("Audio output", 14, "Text", true));
        AutomationProperties.SetName(_outputs, "Select an audio output");
        Children.Add(_outputs);
        _switch = Design.Button("Switch audio output", async () =>
        {
            if (_switching || _outputs.SelectedValue is not string id) return;
            _switching = true; Refresh();
            try
            {
                string? error = await _hub.SwitchAudioOutputAsync(id);
                _showMessage(error ?? "Windows confirmed the selected audio output.");
            }
            catch { _showMessage("Windows could not switch the output. Open Windows sound settings."); }
            finally { _switching = false; _outputSignature = ""; Refresh(); }
        });
        Children.Add(_switch);
        Children.Add(_audio);
        Children.Add(Design.Button("Windows sound settings", () => openSettings("ms-settings:sound")));
        Children.Add(Design.Button("Refresh audio outputs", async () =>
        {
            await _hub.RefreshAudioOutputsAsync(); _outputSignature = ""; Refresh();
        }));
        var statistics = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        statistics.Children.Add(Design.Text("System activity", 14, "Text", true));
        statistics.Children.Add(_cpu); statistics.Children.Add(_memory); statistics.Children.Add(_network);
        statistics.Children.Add(_statisticsStatus);
        Children.Add(statistics);
        var connectivity = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        connectivity.Children.Add(Design.Text("Connectivity", 14, "Text", true));
        connectivity.Children.Add(_wifi); connectivity.Children.Add(_bluetooth);
        connectivity.Children.Add(Design.Button("Windows Wi-Fi settings", () => openSettings("ms-settings:network-wifi")));
        connectivity.Children.Add(Design.Button("Windows Bluetooth settings", () => openSettings("ms-settings:bluetooth")));
        Children.Add(connectivity);
        Refresh();
    }
    public void Refresh()
    {
        var outputs = _hub.AudioOutputs;
        string signature = string.Join("|", outputs.Select(x => x.Id + ":" + x.Name + ":" + x.IsDefault));
        if (!_switching && signature != _outputSignature)
        {
            string? selected = _outputs.SelectedValue as string;
            _outputs.ItemsSource = outputs;
            _outputs.SelectedValue = outputs.Any(x => x.Id == selected) ? selected : outputs.FirstOrDefault(x => x.IsDefault)?.Id ?? outputs.FirstOrDefault()?.Id;
            _outputSignature = signature;
        }
        _switch.IsEnabled = !_switching && outputs.Count > 0;
        _audio.Text = _switching ? "Switching output…" : !_hub.AudioAvailable ? _hub.AudioStatus : "Current output: " + _hub.OutputName;
        var statistics = _hub.SystemStatistics;
        _cpu.Text = "CPU: " + Percent(statistics.CpuPercent);
        _memory.Text = statistics.TotalMemoryBytes == 0 ? "Memory: unavailable"
            : $"Memory: {Percent(statistics.MemoryPercent)} · {Size(statistics.UsedMemoryBytes)} / {Size(statistics.TotalMemoryBytes)}";
        _network.Text = "Network: ↓ " + Rate(statistics.ReceivedBytesPerSecond) + "  ↑ " + Rate(statistics.SentBytesPerSecond);
        _statisticsStatus.Text = statistics.Status + ". Rates warm up after opening Crest.";
        _wifi.Text = _hub.Connectivity.WifiStatus;
        _bluetooth.Text = _hub.Connectivity.BluetoothStatus;
    }
    private static string Percent(double? percent) => percent.HasValue ? percent.Value.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "waiting for Windows samples";
    private static string Size(double bytes) => bytes >= 1_073_741_824 ? (bytes / 1_073_741_824).ToString("0.0", CultureInfo.CurrentCulture) + " GiB"
        : (bytes / 1_048_576).ToString("0.0", CultureInfo.CurrentCulture) + " MiB";
    private static string Rate(double? bytes) => !bytes.HasValue ? "waiting for samples" : bytes >= 1_048_576
        ? (bytes.Value / 1_048_576).ToString("0.0", CultureInfo.CurrentCulture) + " MiB/s"
        : (bytes.Value / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " KiB/s";
}

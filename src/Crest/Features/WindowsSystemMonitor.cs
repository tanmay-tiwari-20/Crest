using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace Crest.Features;

/// <summary>Real machine counters, sampled only while the island is visible.</summary>
internal sealed class WindowsSystemMonitor : IDisposable
{
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _visible, _disposed;
    private int _sampling;
    private long _generation, _lastConnectivity;
    private Sample? _previous;
    public SystemStatisticsSnapshot Statistics { get; private set; } = new();
    public ConnectivitySnapshot Connectivity { get; private set; } = new();
    public event EventHandler? Changed;

    public void SetVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed || _visible == visible) return;
            _visible = visible;
            _generation++;
            _previous = null;
            _lastConnectivity = 0;
            if (visible)
            {
                Statistics = new(Status: "Waiting for Windows samples");
                _timer ??= new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(0, 1000);
            }
            else _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _sampling, 1) != 0) return;
        try
        {
            long generation;
            lock (_gate) { if (_disposed || !_visible) return; generation = _generation; }
            Sample current = ReadSample();
            lock (_gate)
            {
                if (_disposed || !_visible || _generation != generation) return;
                var previous = _previous;
                double seconds = previous is null ? 0 : (current.Timestamp - previous.Timestamp) / (double)Stopwatch.Frequency;
                Statistics = new(
                    previous is null || !current.CpuAvailable || !previous.CpuAvailable ? null
                        : SystemStatisticsMath.CpuPercent(previous.Idle, previous.Kernel, previous.User, current.Idle, current.Kernel, current.User),
                    current.TotalMemory, current.AvailableMemory,
                    previous is null || !previous.NetworkAvailable || !current.NetworkAvailable || previous.AdapterSignature != current.AdapterSignature ? null : SystemStatisticsMath.BytesPerSecond(previous.Received, current.Received, seconds),
                    previous is null || !previous.NetworkAvailable || !current.NetworkAvailable || previous.AdapterSignature != current.AdapterSignature ? null : SystemStatisticsMath.BytesPerSecond(previous.Sent, current.Sent, seconds),
                    !current.CpuAvailable ? "Windows CPU counters unavailable"
                        : !current.NetworkAvailable ? "Some Windows network counters unavailable"
                        : "Live system totals · network across active adapters");
                _previous = current;
            }
            if (Environment.TickCount64 - Interlocked.Read(ref _lastConnectivity) >= 10_000 || _lastConnectivity == 0)
            {
                Interlocked.Exchange(ref _lastConnectivity, Environment.TickCount64);
                ConnectivitySnapshot connectivity = await ReadConnectivityAsync().ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed || !_visible || _generation != generation) return;
                    Connectivity = connectivity;
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            lock (_gate) { if (_disposed || !_visible) return; Statistics = new(Status: "Windows statistics are unavailable. Refresh or open Task Manager."); }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { Interlocked.Exchange(ref _sampling, 0); }
    }
    private static Sample ReadSample()
    {
        bool cpu = GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool hasMemory = GlobalMemoryStatusEx(ref memory);
        long received = 0, sent = 0;
        bool network = true;
        string adapterSignature = "";
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
            adapterSignature = string.Join("|", adapters.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));
            foreach (var adapter in adapters)
            {
                try
                {
                    var counters = adapter.GetIPStatistics();
                    received = checked(received + counters.BytesReceived); sent = checked(sent + counters.BytesSent);
                }
                catch (NetworkInformationException) { network = false; }
            }
        }
        catch (NetworkInformationException) { network = false; }
        return new(Stopwatch.GetTimestamp(), cpu, idle, kernel, user,
            hasMemory ? memory.TotalPhysical : 0, hasMemory ? memory.AvailablePhysical : 0, network, received, sent, adapterSignature);
    }
    private static async Task<ConnectivitySnapshot> ReadConnectivityAsync()
    {
        string wifi = "Wi-Fi status unavailable", bluetooth = "Bluetooth status unavailable";
        try
        {
            var wireless = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToArray();
            wifi = wireless.Any(x => x.OperationalStatus == OperationalStatus.Up) ? "Wi-Fi connected"
                : wireless.Length > 0 ? "Wi-Fi disconnected" : "No Wi-Fi adapter reported";
        }
        catch (NetworkInformationException) { }
        try
        {
            var radios = await Radio.GetRadiosAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            var bluetoothRadios = radios.Where(r => r.Kind == RadioKind.Bluetooth).ToArray();
            bluetooth = bluetoothRadios.Length == 0 ? "No Bluetooth radio reported"
                : bluetoothRadios.Any(r => r.State == RadioState.On) ? "Bluetooth on"
                : bluetoothRadios.All(r => r.State == RadioState.Off) ? "Bluetooth off"
                : "Bluetooth unavailable or disabled";
            var wifiRadios = radios.Where(r => r.Kind == RadioKind.WiFi).ToArray();
            if (wifiRadios.Length > 0 && wifiRadios.All(r => r.State == RadioState.Off)) wifi = "Wi-Fi off";
            else if (wifiRadios.Length > 0 && wifiRadios.All(r => r.State == RadioState.Disabled)) wifi = "Wi-Fi disabled";
        }
        catch { /* Radio access may be denied on an unpackaged desktop app. Show unavailable, never infer On. */ }
        return new(wifi, bluetooth);
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _visible = false; _generation++; _timer?.Dispose(); _timer = null; _previous = null; }
    }
    private sealed record Sample(long Timestamp, bool CpuAvailable, ulong Idle, ulong Kernel, ulong User,
        ulong TotalMemory, ulong AvailableMemory, bool NetworkAvailable, long Received, long Sent, string AdapterSignature);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}

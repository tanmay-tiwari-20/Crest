using System;

namespace Crest.Features;

public sealed record SystemStatisticsSnapshot(double? CpuPercent = null, ulong TotalMemoryBytes = 0,
    ulong AvailableMemoryBytes = 0, double? ReceivedBytesPerSecond = null, double? SentBytesPerSecond = null,
    string Status = "Statistics update while Crest is visible")
{
    public ulong UsedMemoryBytes => TotalMemoryBytes > AvailableMemoryBytes ? TotalMemoryBytes - AvailableMemoryBytes : 0;
    public double? MemoryPercent => TotalMemoryBytes == 0 ? null : UsedMemoryBytes * 100.0 / TotalMemoryBytes;
}
public sealed record ConnectivitySnapshot(string WifiStatus = "Checking Wi-Fi…", string BluetoothStatus = "Checking Bluetooth…");

/// <summary>Counter deltas are invalid after provider resets, wraparound, or a new sampling period.</summary>
public static class SystemStatisticsMath
{
    public static double? CpuPercent(ulong previousIdle, ulong previousKernel, ulong previousUser,
        ulong idle, ulong kernel, ulong user)
    {
        if (idle < previousIdle || kernel < previousKernel || user < previousUser) return null;
        double elapsed = (double)(kernel - previousKernel) + (user - previousUser);
        return elapsed <= 0 ? null : Math.Clamp((elapsed - (idle - previousIdle)) * 100 / elapsed, 0, 100);
    }
    public static double? BytesPerSecond(long previous, long current, double elapsedSeconds)
        => previous < 0 || current < previous || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0
            ? null : (current - previous) / elapsedSeconds;
}

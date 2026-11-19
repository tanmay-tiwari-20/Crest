using Crest.Features;

internal static class SystemStatisticsTests
{
    public static int Run()
    {
        Check(SystemStatisticsMath.CpuPercent(100, 200, 100, 150, 300, 200) == 75, "CPU counter deltas include idle within kernel time");
        Check(SystemStatisticsMath.CpuPercent(100, 200, 100, 90, 300, 200) is null, "CPU provider resets have no invented percentage");
        Check(SystemStatisticsMath.CpuPercent(100, 200, 100, 100, 200, 100) is null, "CPU warm-up requires elapsed counters");
        Check(SystemStatisticsMath.BytesPerSecond(1024, 4096, 2) == 1536, "Network throughput divides a counter delta by real elapsed time");
        Check(SystemStatisticsMath.BytesPerSecond(4096, 1024, 2) is null && SystemStatisticsMath.BytesPerSecond(0, 10, double.NaN) is null, "Network reset and invalid time produce unavailable rate");
        var memory = new SystemStatisticsSnapshot(TotalMemoryBytes: 8192, AvailableMemoryBytes: 2048);
        Check(memory.UsedMemoryBytes == 6144 && memory.MemoryPercent == 75, "Memory derives used bytes from Windows physical totals");
        Console.WriteLine("PASS: 6 system-statistics outcomes");
        return 6;
    }
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
    }
}

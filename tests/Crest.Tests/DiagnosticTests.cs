using Crest.Core;

internal static class DiagnosticTests
{
    public static int Run()
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.GetFullPath(Path.Combine(parent, "CrestDiagnosticTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var now = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
            string path = Path.Combine(directory, "diagnostic.log");
            File.WriteAllLines(path, new[] { now.AddHours(-73).ToString("O") + " state:Open", now.AddHours(-1).ToString("O") + " state:Armed" });
            using var log = new DiagnosticLog(directory, () => now, 512);
            log.Enqueue("state:Ineligible");
            log.Enqueue(@"error:C:\private\secret.txt");
            log.Enqueue("token:credential value");
            log.FlushAsync().GetAwaiter().GetResult();
            string contents = File.ReadAllText(path);
            Check(!contents.Contains("state:Open") && contents.Contains("state:Armed") && contents.Contains("state:Ineligible") && !contents.Contains("private") && !contents.Contains("credential"));
            Console.WriteLine("PASS Diagnostics expire after 72 hours and reject free-form private data");
            for (int i = 0; i < 300; i++) log.Enqueue("state:Armed:" + i);
            log.FlushAsync().GetAwaiter().GetResult();
            Check(new FileInfo(path).Length <= 512 && File.ReadAllText(path).Contains("state:Armed:299"));
            Console.WriteLine("PASS Diagnostic bursts retain newest codes within the disk budget");
            now = now.AddHours(73);
            log.PruneAsync().GetAwaiter().GetResult();
            Check(new FileInfo(path).Length == 0);
            Console.WriteLine("PASS Idle diagnostic retention clears expired codes without a new log event");
            return 3;
        }
        finally
        {
            if (directory.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(directory).StartsWith("CrestDiagnosticTests-", StringComparison.Ordinal))
                Directory.Delete(directory, true);
        }
    }

    private static void Check(bool value)
    {
        if (!value) throw new Exception("Diagnostic retention outcome failed.");
    }
}

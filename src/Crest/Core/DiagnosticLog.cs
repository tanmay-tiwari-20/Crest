using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Crest.Core;

/// <summary>Opt-in technical diagnostics, with bounded memory, disk use and retention.</summary>
public sealed class DiagnosticLog : IDisposable
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumPending = 128;
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly int _maximumBytes;
    private readonly Timer _retentionSweep;
    private Task _drain = Task.CompletedTask;
    private bool _draining, _prunePending, _disposed;

    public DiagnosticLog(string directory, Func<DateTimeOffset>? utcNow = null, int maximumBytes = MaximumBytes)
    {
        _path = Path.Combine(directory, "diagnostic.log");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _maximumBytes = Math.Clamp(maximumBytes, 256, MaximumBytes);
        _retentionSweep = new Timer(_ => RequestPrune(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }

    public void Enqueue(string code)
    {
        // Reject free-form data: callers provide only bounded technical state/error codes.
        if (string.IsNullOrEmpty(code) || code.Length > 120 || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.')))
            return;
        string line = _utcNow().ToString("O", CultureInfo.InvariantCulture) + " " + code;
        lock (_gate)
        {
            if (_disposed) return;
            if (_pending.Count == MaximumPending) _pending.Dequeue();
            _pending.Enqueue(line);
            if (!_draining)
            {
                _draining = true;
                _drain = Task.Run(Drain);
            }
        }
    }

    public Task FlushAsync()
    {
        lock (_gate) return _drain;
    }

    public Task PruneAsync()
    {
        RequestPrune();
        return FlushAsync();
    }

    private void RequestPrune()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _prunePending = true;
            if (!_draining)
            {
                _draining = true;
                _drain = Task.Run(Drain);
            }
        }
    }

    private void Drain()
    {
        while (true)
        {
            string[] batch;
            lock (_gate)
            {
                if (_pending.Count == 0 && !_prunePending)
                {
                    _draining = false;
                    return;
                }
                batch = _pending.ToArray();
                _pending.Clear();
                _prunePending = false;
            }
            try
            {
                DateTimeOffset now = _utcNow();
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var lines = new List<string>();
                if (File.Exists(_path) && new FileInfo(_path).Length <= _maximumBytes)
                {
                    foreach (string line in File.ReadLines(_path))
                    {
                        int separator = line.IndexOf(' ');
                        if (separator > 0 && DateTimeOffset.TryParse(line.AsSpan(0, separator), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
                            && timestamp >= now.AddHours(-72) && timestamp <= now.AddMinutes(1))
                            lines.Add(line);
                    }
                }
                lines.AddRange(batch);
                int bytes = lines.Sum(line => Encoding.UTF8.GetByteCount(line + Environment.NewLine));
                int first = 0;
                while (bytes > _maximumBytes && first < lines.Count)
                    bytes -= Encoding.UTF8.GetByteCount(lines[first++] + Environment.NewLine);
                File.WriteAllLines(_path, lines.Skip(first), new UTF8Encoding(false));
            }
            catch { } // A diagnostic failure must never interrupt controls or lifecycle recovery.
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _retentionSweep.Dispose();
    }
}

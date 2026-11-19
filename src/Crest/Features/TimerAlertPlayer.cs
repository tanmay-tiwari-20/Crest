using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;

namespace Crest.Features;

public interface ITimerAlertPlayback : IDisposable
{
    Task PlayAsync(CancellationToken cancellationToken);
    void Stop();
}

/// <summary>One finite audible batch, independent of Windows' system-sound assignment.</summary>
public sealed class TimerAlertPlayer : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<bool> _enabled;
    private readonly Func<string, bool> _isCurrentCompletion;
    private readonly ITimerAlertPlayback _playback;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private Task _drain = Task.CompletedTask;
    private CancellationTokenSource? _current;
    private bool _running, _playingCompletion, _previewPending, _disposed;
    private int _playbackDisposed;

    public TimerAlertPlayer(Func<bool> enabled, Func<string, bool> isCurrentCompletion, ITimerAlertPlayback? playback = null)
    {
        _enabled = enabled;
        _isCurrentCompletion = isCurrentCompletion;
        _playback = playback ?? new WavePlayback();
    }

    public Task WhenIdle { get { lock (_gate) return _drain; } }

    public void QueueCompletion(string timerId)
    {
        if (string.IsNullOrWhiteSpace(timerId) || !_enabled()) return;
        lock (_gate)
        {
            if (_disposed || _playingCompletion) return;
            _pending.RemoveWhere(id => !_isCurrentCompletion(id));
            if (!_isCurrentCompletion(timerId)) return;
            if (_pending.Count < LocalHub.MaxTimers) _pending.Add(timerId);
            StartDrain();
        }
    }

    public void Preview()
    {
        lock (_gate)
        {
            if (_disposed || _current != null) return;
            _previewPending = true;
            StartDrain();
        }
    }

    private void StartDrain()
    {
        if (_running) return;
        _running = true;
        _drain = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                CancellationTokenSource cancellation;
                lock (_gate)
                {
                    if (_disposed) { _running = false; return; }
                    bool preview = _previewPending;
                    _previewPending = false;
                    bool completion = _pending.Count > 0 && _enabled() && _pending.Any(_isCurrentCompletion);
                    _pending.Clear();
                    if (!preview && !completion) { _running = false; return; }
                    _playingCompletion = !preview;
                    cancellation = _current = new CancellationTokenSource();
                }
                try { await _playback.PlayAsync(cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) { App.WriteDiagnostic("timer-audio:" + ex.GetType().Name); }
                finally
                {
                    lock (_gate) { _current = null; _playingCompletion = false; }
                    cancellation.Dispose();
                }
            }
        }
        finally { if (_disposed) DisposePlayback(); }
    }

    public void CancelCompletionAlerts()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _pending.Clear();
            cancellation = _playingCompletion ? _current : null;
        }
        Cancel(cancellation);
    }

    private void Cancel(CancellationTokenSource? cancellation)
    {
        if (cancellation == null) return;
        try { cancellation.Cancel(); }
        catch (ObjectDisposedException) { return; }
        _playback.Stop();
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        bool running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _previewPending = false;
            cancellation = _current;
            running = _running;
        }
        Cancel(cancellation);
        _playback.Stop();
        if (!running) DisposePlayback();
    }

    private void DisposePlayback()
    {
        if (Interlocked.Exchange(ref _playbackDisposed, 1) == 0) _playback.Dispose();
    }

    private sealed class WavePlayback : ITimerAlertPlayback
    {
        private readonly object _gate = new();
        private SoundPlayer? _player;
        private MemoryStream? _stream;
        private TimeSpan _duration;
        private bool _disposed;

        public async Task PlayAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(Stop);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (_player == null)
                {
                    using var resource = typeof(TimerAlertPlayer).Assembly.GetManifestResourceStream("Crest.TimerComplete.wav")
                        ?? throw new InvalidDataException("Timer sound resource is missing.");
                    if (resource.Length < 44 || resource.Length > 1024 * 1024) throw new InvalidDataException("Invalid timer sound size.");
                    var data = new byte[(int)resource.Length];
                    resource.ReadExactly(data);
                    int byteRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28, 4));
                    int payload = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(40, 4));
                    if (byteRate <= 0 || payload <= 0 || payload > data.Length - 44 || payload / (double)byteRate > 10)
                        throw new InvalidDataException("Invalid timer sound duration.");
                    _duration = TimeSpan.FromSeconds(payload / (double)byteRate);
                    _stream = new MemoryStream(data, writable: false);
                    _player = new SoundPlayer(_stream);
                    _player.Load();
                }
                cancellationToken.ThrowIfCancellationRequested();
                _player.Play();
            }
            try { await Task.Delay(_duration, cancellationToken).ConfigureAwait(false); }
            finally { Stop(); }
        }

        public void Stop() { lock (_gate) _player?.Stop(); }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _player?.Stop();
                _player?.Dispose();
                _stream?.Dispose();
            }
        }
    }
}

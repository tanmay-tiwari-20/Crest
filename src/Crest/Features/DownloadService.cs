using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Crest.Features;

public sealed class DownloadPreferences
{
    public bool Enabled { get; set; }
}

public enum DownloadPhase { Downloading, Completed, Cancelled, Failed }

public sealed record DownloadItem(string Id, string Url, string Destination, DownloadPhase Phase,
    long ReceivedBytes, long? TotalBytes, string Message, DateTimeOffset UpdatedUtc)
{
    public double? Percentage => TotalBytes is > 0 ? Math.Clamp(100.0 * ReceivedBytes / TotalBytes.Value, 0, 100) : null;
}

/// <summary>Only explicit Crest-owned transfers. References and download history remain in memory.</summary>
public sealed class DownloadService : IDisposable
{
    public const int MaxItems = 20;
    public const int MaxConcurrent = 2;
    public const long MaxBytes = 4L * 1024 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Func<DownloadPreferences> _preferences;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Dictionary<string, Transfer> _transfers = new();
    private readonly HashSet<Transfer> _active = new();
    private bool _disposed;
    public event EventHandler? Changed;

    public DownloadService(Func<DownloadPreferences> preferences, HttpClient? client = null)
    {
        _preferences = preferences;
        _ownsClient = client == null;
        _client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public IReadOnlyList<DownloadItem> Items
    {
        get { lock (_gate) return _transfers.Values.Select(x => x.Item).Reverse().ToArray(); }
    }

    public static void Normalize(DownloadPreferences preferences) { }

    public static bool TryGetUrl(string? value, out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 4096
            && Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && !string.IsNullOrEmpty(uri.Host);
    }

    public string? Start(string url, string destination)
    {
        if (!TryGetUrl(url, out var source)) return "Enter an HTTP or HTTPS download URL without embedded credentials or a fragment.";
        string target;
        try
        {
            if (!Path.IsPathFullyQualified(destination)) return "Choose an absolute destination filename.";
            target = Path.GetFullPath(destination);
            if (string.IsNullOrWhiteSpace(Path.GetFileName(target)) || !Directory.Exists(Path.GetDirectoryName(target)))
                return "Choose a filename in an existing folder.";
            if (File.Exists(target) || Directory.Exists(target)) return "The destination already exists. Choose a new filename; Crest never overwrites it.";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { return "The destination is unavailable. Choose a different filename."; }
        Transfer transfer;
        lock (_gate)
        {
            if (_disposed || !_preferences().Enabled) return "Downloads are off. Enable Crest-managed downloads first.";
            if (_active.Count >= MaxConcurrent)
                return "Two downloads are already running. Wait or cancel one before starting another.";
            if (_transfers.Count >= MaxItems) return "The download list is full. Remove finished entries to make room.";
            if (_transfers.Values.Any(x => x.Item.Phase == DownloadPhase.Downloading && string.Equals(x.Item.Destination, target, StringComparison.OrdinalIgnoreCase)))
                return "Another transfer is already using this destination.";
            string id = Guid.NewGuid().ToString("N");
            transfer = new Transfer(new DownloadItem(id, source!.AbsoluteUri, target, DownloadPhase.Downloading, 0, null, "Connecting…", DateTimeOffset.UtcNow));
            _transfers.Add(id, transfer);
            _active.Add(transfer);
            transfer.Task = Task.Run(() => DownloadAsync(transfer));
            _ = transfer.Task.ContinueWith(_ =>
            {
                lock (_gate) { _active.Remove(transfer); transfer.Cancellation.Dispose(); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        RaiseChanged();
        return null;
    }

    public void Cancel(string id)
    {
        lock (_gate) if (_transfers.TryGetValue(id, out var transfer) && transfer.Item.Phase == DownloadPhase.Downloading) transfer.Cancellation.Cancel();
    }

    public string? Retry(string id)
    {
        DownloadItem? item;
        lock (_gate)
        {
            if (!_transfers.TryGetValue(id, out var transfer) || transfer.Item.Phase == DownloadPhase.Downloading || !transfer.Task.IsCompleted) return "This download is still finishing cleanup. Try again shortly.";
            item = transfer.Item;
            _transfers.Remove(id);
            _active.Remove(transfer);
            transfer.Cancellation.Dispose();
        }
        string? error = Start(item.Url, item.Destination);
        if (error != null)
        {
            lock (_gate) _transfers[id] = new Transfer(item);
            RaiseChanged();
        }
        return error;
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_transfers.TryGetValue(id, out var transfer) && transfer.Item.Phase != DownloadPhase.Downloading && transfer.Task.IsCompleted)
            { _transfers.Remove(id); _active.Remove(transfer); transfer.Cancellation.Dispose(); }
        }
        RaiseChanged();
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var transfer in _active) transfer.Cancellation.Cancel();
            _transfers.Clear();
        }
        RaiseChanged();
    }

    public void ApplyPreferences()
    {
        if (!_preferences().Enabled) Clear();
    }

    public Task WaitForIdleAsync()
    {
        lock (_gate) return Task.WhenAll(_active.Select(x => x.Task));
    }

    private async Task DownloadAsync(Transfer transfer)
    {
        string temporary = transfer.Item.Destination + ".crest-" + transfer.Item.Id + ".part";
        long received = 0;
        long? total = null;
        bool ownsTemporary = false;
        try
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(transfer.Cancellation.Token);
            lifetime.CancelAfter(TimeSpan.FromMinutes(30));
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, transfer.Item.Url);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connect.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUri && !TryGetUrl(finalUri.AbsoluteUri, out _))
                throw new InvalidDataException("Unsupported redirect.");
            total = response.Content.Headers.ContentLength;
            if (total is < 0 or > MaxBytes) throw new InvalidDataException("The transfer exceeds Crest's four GiB limit.");
            await using var input = await response.Content.ReadAsStreamAsync(lifetime.Token).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporary = true;
                var buffer = new byte[65536];
                var lastUpdate = DateTimeOffset.MinValue;
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                    int count = await input.ReadAsync(buffer.AsMemory(), readTimeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > MaxBytes || total.HasValue && received > total.Value) throw new InvalidDataException("The server returned an invalid download size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), lifetime.Token).ConfigureAwait(false);
                    if ((DateTimeOffset.UtcNow - lastUpdate).TotalMilliseconds >= 200)
                    {
                        Update(transfer, DownloadPhase.Downloading, received, total, "Downloading…");
                        lastUpdate = DateTimeOffset.UtcNow;
                    }
                }
                if (total.HasValue && received != total.Value) throw new InvalidDataException("The download ended before all bytes arrived.");
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
            }
            // Cancellation and disconnect cannot commit a completed file afterwards.
            lock (_gate)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (_disposed || !_preferences().Enabled || !_transfers.ContainsKey(transfer.Item.Id)) throw new OperationCanceledException();
                File.Move(temporary, transfer.Item.Destination, false);
                ownsTemporary = false;
                transfer.Item = transfer.Item with { Phase = DownloadPhase.Completed, ReceivedBytes = received, TotalBytes = total, Message = "Saved. Opening this file is a separate action.", UpdatedUtc = DateTimeOffset.UtcNow };
            }
            RaiseChanged();
        }
        catch (OperationCanceledException)
        {
            Update(transfer, transfer.Cancellation.IsCancellationRequested ? DownloadPhase.Cancelled : DownloadPhase.Failed,
                received, total, transfer.Cancellation.IsCancellationRequested ? "Cancelled. Partial bytes are removed." : "The transfer timed out. Retry when the connection is available.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            string message = ex is InvalidDataException ? "The download size is invalid or exceeds four GiB. Partial bytes are removed."
                : ex is HttpRequestException ? "The server or connection is unavailable. Retry later."
                : "Crest could not save this file. The destination may exist or be unavailable.";
            Update(transfer, DownloadPhase.Failed, received, total, message);
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Update(transfer, DownloadPhase.Failed, received, total, "A partial download could not be removed. Remove the .crest-… .part file from the destination folder."); }
            }
        }
    }

    private void Update(Transfer transfer, DownloadPhase phase, long received, long? total, string message)
    {
        lock (_gate)
        {
            if (_disposed || !_transfers.ContainsKey(transfer.Item.Id)) return;
            transfer.Item = transfer.Item with { Phase = phase, ReceivedBytes = received, TotalBytes = total, Message = message, UpdatedUtc = DateTimeOffset.UtcNow };
        }
        RaiseChanged();
    }
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        Task pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var transfer in _active) transfer.Cancellation.Cancel();
            _transfers.Clear();
            pending = Task.WhenAll(_active.Select(x => x.Task));
        }
        if (_ownsClient) _client.Dispose();
        // Shutdown allows cooperative cancellation and partial-file cleanup to finish.
        // The bound avoids hanging Quit on an unresponsive third-party stream/provider.
        try { pending.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }
    private sealed class Transfer
    {
        public DownloadItem Item;
        public readonly CancellationTokenSource Cancellation = new();
        public Task Task = Task.CompletedTask;
        public Transfer(DownloadItem item) => Item = item;
    }
}

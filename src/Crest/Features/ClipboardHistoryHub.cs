using System;
using System.Collections.Generic;
using System.Linq;
using Crest.Core;

namespace Crest.Features;

public sealed record ClipboardHistoryOptions(bool Enabled = false, int RetentionDays = 7);
public sealed record ClipboardHistoryItem(string Id, string Text, DateTimeOffset CapturedUtc, bool Pinned);

/// <summary>Bounded opt-in text history. Collection belongs to the Windows listener, never this store.</summary>
public sealed class ClipboardHistoryHub : IDisposable
{
    public const int MaxItems = 100;
    public const int MaxTextCharacters = 8192;
    public const int MaxStoredCharacters = 120000;
    private readonly object _gate = new();
    private readonly SettingsStore _store;
    private readonly Func<ClipboardHistoryOptions> _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly List<ClipboardHistoryItem> _items = new();
    private bool _enabled, _paused, _disposed;
    private int _retentionDays;
    private long _captureGeneration;
    private System.Threading.Timer? _retentionTicker;
    private readonly bool _automaticRetention;
    public event EventHandler? Changed;
    public bool Enabled { get { lock (_gate) return _enabled && !_disposed; } }
    public bool Paused { get { lock (_gate) return _paused; } }
    public bool Capturing { get { lock (_gate) return _enabled && !_paused && !_disposed; } }
    public int RetentionDays { get { lock (_gate) return _retentionDays; } }
    public long CaptureGeneration { get { lock (_gate) return _captureGeneration; } }
    public string? LastError => _store.LastError;

    public ClipboardHistoryHub(SettingsStore store, Func<ClipboardHistoryOptions> options, Func<DateTimeOffset>? utcNow = null, bool automaticRetention = true)
    {
        _store = store;
        _options = options;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _automaticRetention = automaticRetention;
        var settings = options();
        _enabled = settings.Enabled;
        _retentionDays = Math.Clamp(settings.RetentionDays, 1, 30);
        if (!_enabled) { Erase(); return; }
        var saved = _store.ReadState<ClipboardHistoryState>("clipboard.json");
        if (saved is { SchemaVersion: 1 })
        {
            _paused = saved.Paused;
            DateTimeOffset now = _utcNow();
            foreach (var item in (saved.Items ?? new()).Take(MaxItems))
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > MaxTextCharacters
                    || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64
                    || item.CapturedUtc > now.AddDays(1) || item.CapturedUtc == default
                    || _items.Any(x => x.Id == item.Id || x.Text == item.Text)) continue;
                if (_items.Sum(x => x.Text.Length) + item.Text.Length > MaxStoredCharacters) break;
                _items.Add(item);
            }
            PruneCore();
        }
        Persist();
        UpdateRetentionTicker();
    }

    public IReadOnlyList<ClipboardHistoryItem> Search(string? query = null)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return Array.Empty<ClipboardHistoryItem>();
            if (PruneCore()) Persist();
            return _items.Where(x => string.IsNullOrEmpty(query) || x.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Pinned).ThenByDescending(x => x.CapturedUtc).ToArray();
        }
    }

    public void ApplyPreferences()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            var options = _options();
            int retention = Math.Clamp(options.RetentionDays, 1, 30);
            changed = _enabled != options.Enabled || _retentionDays != retention;
            if (_enabled != options.Enabled) _captureGeneration++;
            _enabled = options.Enabled;
            _retentionDays = retention;
            if (!_enabled)
            {
                changed |= _items.Count != 0 || _paused;
                _items.Clear();
                _paused = false;
                Erase();
            }
            else
            {
                changed |= PruneCore();
                if (changed) Persist();
            }
            UpdateRetentionTicker();
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Only the enabled and unpaused listener may add a new clipboard change.</summary>
    public bool CaptureText(string? text, long? expectedGeneration = null)
    {
        bool captured = false;
        bool pruned = false;
        lock (_gate)
        {
            if (_disposed || !_enabled || _paused || (expectedGeneration.HasValue && expectedGeneration.Value != _captureGeneration)
                || string.IsNullOrWhiteSpace(text) || text.Length > MaxTextCharacters) return false;
            pruned = PruneCore();
            var duplicate = _items.FirstOrDefault(x => x.Text == text);
            var retained = _items.Where(x => x != duplicate).ToList();
            int total = retained.Sum(x => x.Text.Length);
            bool canFit = true;
            while (retained.Count >= MaxItems || total + text.Length > MaxStoredCharacters)
            {
                var oldest = retained.Where(x => !x.Pinned).MinBy(x => x.CapturedUtc);
                if (oldest == null)
                {
                    canFit = false;
                    break;
                }
                retained.Remove(oldest);
                total -= oldest.Text.Length;
            }
            if (canFit)
            {
                _items.Clear();
                _items.AddRange(retained);
                _items.Add(new(duplicate?.Id ?? Guid.NewGuid().ToString("N"), text, _utcNow(), duplicate?.Pinned ?? false));
                Persist();
                captured = true;
            }
            else if (pruned) Persist();
        }
        if (captured || pruned) Changed?.Invoke(this, EventArgs.Empty);
        return captured;
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || _paused == paused) return;
            _paused = paused;
            _captureGeneration++;
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            int index = _items.FindIndex(x => x.Id == id);
            if (index < 0 || _items[index].Pinned == pinned) return;
            _items[index] = _items[index] with { Pinned = pinned };
            PruneCore();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_disposed || _items.RemoveAll(x => x.Id == id) == 0) return;
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _items.Clear();
            _captureGeneration++;
            if (_enabled) Persist(); else Erase();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _enabled = false;
            _paused = false;
            _items.Clear();
            _captureGeneration++;
            Erase();
            UpdateRetentionTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => ApplyPreferences();
    private bool PruneCore() => _items.RemoveAll(x => !x.Pinned && (_utcNow() - x.CapturedUtc).TotalDays >= _retentionDays) > 0;
    private void Persist()
    {
        if (_enabled) _store.WriteState("clipboard.json", new ClipboardHistoryState { Paused = _paused, Items = _items.ToList() });
    }
    private void Erase()
    {
        _store.DeleteState("clipboard.json");
        _store.DeleteState("clipboard.json.tmp");
    }
    private void UpdateRetentionTicker()
    {
        if (_enabled && _automaticRetention && !_disposed)
            _retentionTicker ??= new System.Threading.Timer(_ => ApplyPreferences(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        else
        {
            _retentionTicker?.Dispose();
            _retentionTicker = null;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _items.Clear();
            _captureGeneration++;
            UpdateRetentionTicker();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ClipboardHistoryState
{
    public int SchemaVersion { get; set; } = 1;
    public bool Paused { get; set; }
    public List<ClipboardHistoryItem> Items { get; set; } = new();
}

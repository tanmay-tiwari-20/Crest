using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Crest.Features;

public sealed record MediaPlayerInfo(string Id, string Name);
public sealed record MusicSnapshot
{
    public string Title { get; init; } = "Nothing playing";
    public string Artist { get; init; } = "Open a music app to get started";
    public string PlayerName { get; init; } = "Windows media";
    public ImageSource? Artwork
    {
        get; init;
    }
    public bool HasSession
    {
        get; init;
    }
    public bool IsPlaying
    {
        get; init;
    }
    public bool CanPlayPause
    {
        get; init;
    }
    public bool CanPrevious
    {
        get; init;
    }
    public bool CanNext
    {
        get; init;
    }
    public bool CanSeek
    {
        get; init;
    }
    public double PositionSeconds
    {
        get; init;
    }
    public double DurationSeconds
    {
        get; init;
    }
    public string Status { get; init; } = "No compatible media session. Your player must publish Windows media controls.";
}
public sealed record BatterySnapshot
{
    public bool Available
    {
        get; init;
    }
    public int Percent
    {
        get; init;
    }
    public bool Charging
    {
        get; init;
    }
    public int? RemainingMinutes
    {
        get; init;
    }
    public string Status { get; init; } = "Checking battery…";
}

public sealed class HardwareHub : IDisposable
{
    private readonly CoreAudioAdapter _audio = new();
    private readonly SemaphoreSlim _mediaGate = new(1, 1);
    private readonly SemaphoreSlim _brightnessGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private ManagementEventWatcher? _brightnessWatcher;
    private System.Threading.Timer? _batteryTimer;
    private System.Threading.Timer? _visibleTimer;
    private string _preferredPlayer;
    private string? _brightnessInstance;
    private bool _visible;
    private bool _started;
    private bool _disposed;
    private long _mediaGeneration;
    private int _mediaRefreshPending;
    private long _lastBatteryRead = long.MinValue / 2;
    private string _artKey = "";
    private ImageSource? _cachedArt;
    public event EventHandler? Changed;
    public MusicSnapshot Music { get; private set; } = new();
    public IReadOnlyList<MediaPlayerInfo> Players { get; private set; } = Array.Empty<MediaPlayerInfo>();
    public double Volume => _audio.Volume;
    public bool IsMuted => _audio.Muted;
    public bool AudioAvailable => _audio.Available;
    public string AudioStatus => _audio.Status;
    public string OutputName => _audio.OutputName;
    public BatterySnapshot Battery { get; private set; } = new();
    public double Brightness
    {
        get; private set;
    }
    public bool BrightnessAvailable
    {
        get; private set;
    }
    public string BrightnessStatus { get; private set; } = "Checking built-in display…";

    public HardwareHub(string preferredPlayer = "")
    {
        _preferredPlayer = preferredPlayer;
        _audio.Changed += OnAudioChanged;
    }

    public void Start()
    {
        if (_started || _disposed)
            return;
        _started = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _batteryTimer = new System.Threading.Timer(_ => _ = Task.Run(() => ReadBattery()), null, 0, 30_000);
        _visibleTimer = new System.Threading.Timer(_ => { if (_visible) ReadTimeline(); }, null, Timeout.Infinite, Timeout.Infinite);
        _ = Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        await _audio.RefreshAsync().ConfigureAwait(false);
        await ReadBrightnessAsync(true).ConfigureAwait(false);
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
                return;
            _manager = manager;
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            await ReadMediaAsync().ConfigureAwait(false);
        }
        catch
        {
            Music = new MusicSnapshot { Status = "Windows media sessions are unavailable. Other controls still work." };
            RaiseChanged();
        }
    }

    public void SetVisible(bool visible)
    {
        if (_disposed || _visible == visible)
            return;
        _visible = visible;
        _visibleTimer?.Change(visible ? 0 : Timeout.Infinite, visible ? 1000 : Timeout.Infinite);
        if (visible)
            _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
            return;
        await Task.WhenAll(_audio.RefreshAsync(), Task.Run(() => ReadBattery()), ReadBrightnessAsync(), ReadMediaAsync()).ConfigureAwait(false);
    }

    public void SelectPlayer(string id)
    {
        _preferredPlayer = id;
        Interlocked.Increment(ref _mediaGeneration);
        _ = ReadMediaAsync();
    }

    public Task SetVolumeAsync(double value) => Task.Run(() => _audio.SetVolumeAsync(value));
    public Task ToggleMuteAsync() => Task.Run(() => _audio.ToggleMuteAsync());
    public Task TogglePlaybackAsync() => RunMediaCommand(async session =>
    {
        if (!Music.CanPlayPause)
            return;
        var playback = session.GetPlaybackInfo();
        if (playback.Controls.IsPlayPauseToggleEnabled)
            await session.TryTogglePlayPauseAsync();
        else if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && playback.Controls.IsPauseEnabled)
            await session.TryPauseAsync();
        else if (playback.Controls.IsPlayEnabled)
            await session.TryPlayAsync();
    });
    public Task PreviousAsync() => RunMediaCommand(async session => { if (Music.CanPrevious) await session.TrySkipPreviousAsync(); });
    public Task NextAsync() => RunMediaCommand(async session => { if (Music.CanNext) await session.TrySkipNextAsync(); });
    public Task SeekAsync(double seconds) => RunMediaCommand(async session => { if (Music.CanSeek) await session.TryChangePlaybackPositionAsync(session.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Music.DurationSeconds)).Ticks); });

    private async Task RunMediaCommand(Func<GlobalSystemMediaTransportControlsSession, Task> action)
    {
        var session = _session;
        if (_disposed || session is null)
            return;
        try
        {
            await action(session).ConfigureAwait(false);
        }
        catch { Music = Music with { Status = "The player stopped responding. Refresh or select another player." }; RaiseChanged(); }
        await ReadMediaAsync().ConfigureAwait(false);
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Interlocked.Increment(ref _mediaGeneration);
        _ = ReadMediaAsync();
    }
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        Interlocked.Increment(ref _mediaGeneration);
        _ = ReadMediaAsync();
    }
    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Interlocked.Increment(ref _mediaGeneration);
        _artKey = "";
        _ = ReadMediaAsync();
    }
    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = ReadMediaAsync();
    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => ReadTimeline();

    private async Task ReadMediaAsync()
    {
        if (_disposed || _manager is null)
            return;
        Interlocked.Exchange(ref _mediaRefreshPending, 1);
        if (!await _mediaGate.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            Interlocked.Exchange(ref _mediaRefreshPending, 0);
            if (_disposed || _manager is null)
                return;
            var generation = Interlocked.Read(ref _mediaGeneration);
            var sessions = _manager.GetSessions();
            Players = sessions.Select(s => new MediaPlayerInfo(s.SourceAppUserModelId, FriendlyPlayer(s.SourceAppUserModelId))).ToArray();
            var next = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _preferredPlayer) ?? _manager.GetCurrentSession() ?? sessions.FirstOrDefault();
            if (!ReferenceEquals(_session, next))
            {
                UnsubscribeSession();
                _session = next;
                if (_session is not null)
                {
                    _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                }
            }
            if (next is null)
            {
                Music = new();
                _cachedArt = null;
                _artKey = "";
                return;
            }
            var metadata = await next.TryGetMediaPropertiesAsync();
            var playback = next.GetPlaybackInfo();
            var timeline = next.GetTimelineProperties();
            var key = next.SourceAppUserModelId + "|" + metadata.Title + "|" + metadata.Artist;
            if (_artKey != key)
            {
                var art = await ReadArtworkAsync(metadata.Thumbnail).ConfigureAwait(false);
                if (_disposed || generation != Interlocked.Read(ref _mediaGeneration))
                    return;
                _cachedArt = art;
                _artKey = key;
            }
            if (_disposed || generation != Interlocked.Read(ref _mediaGeneration))
                return;
            var controls = playback.Controls;
            var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            Music = new MusicSnapshot
            {
                HasSession = true,
                Title = string.IsNullOrWhiteSpace(metadata.Title) ? "Untitled track" : metadata.Title,
                Artist = string.IsNullOrWhiteSpace(metadata.Artist) ? "Artist unavailable" : metadata.Artist,
                PlayerName = FriendlyPlayer(next.SourceAppUserModelId),
                Artwork = _cachedArt,
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPlayPause = controls.IsPlayPauseToggleEnabled || (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
                CanPrevious = controls.IsPreviousEnabled,
                CanNext = controls.IsNextEnabled,
                CanSeek = controls.IsPlaybackPositionEnabled && duration > 0,
                PositionSeconds = Position(timeline, playback),
                DurationSeconds = duration,
                Status = "Connected to " + FriendlyPlayer(next.SourceAppUserModelId)
            };
        }
        catch
        {
            UnsubscribeSession();
            _session = null;
            Music = new MusicSnapshot { Status = "The media session ended. Open a compatible player or refresh." };
        }
        finally
        {
            _mediaGate.Release();
            RaiseChanged();
            if (!_disposed && Interlocked.Exchange(ref _mediaRefreshPending, 0) != 0)
                _ = ReadMediaAsync();
        }
    }

    private void ReadTimeline()
    {
        var session = _session;
        if (_disposed || !_visible || session is null)
            return;
        try
        {
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
            Music = Music with
            {
                DurationSeconds = duration,
                PositionSeconds = Position(timeline, playback)
            };
            RaiseChanged();
        }
        catch { _ = ReadMediaAsync(); }
    }

    private static double Position(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline, GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
    {
        var seconds = (timeline.Position - timeline.StartTime).TotalSeconds;
        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && timeline.LastUpdatedTime != default)
            seconds += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * (playback.PlaybackRate ?? 1);
        return Math.Clamp(seconds, 0, Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds));
    }

    private static async Task<ImageSource?> ReadArtworkAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
            return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 2 * 1024 * 1024)
                return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var length = (uint)stream.Size;
            await reader.LoadAsync(length);
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            using var memory = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 160;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static string FriendlyPlayer(string id)
    {
        if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            return "Spotify";
        if (id.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            return "Google Chrome";
        if (id.Contains("Edge", StringComparison.OrdinalIgnoreCase))
            return "Microsoft Edge";
        if (id.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || id.Contains("MediaPlayer", StringComparison.OrdinalIgnoreCase))
            return "Media Player";
        if (id.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
            return "Firefox";
        if (id.Contains("VLC", StringComparison.OrdinalIgnoreCase))
            return "VLC";
        var name = id.Split('!')[0].Split('_')[0];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private void ReadBattery(bool force = false)
    {
        if (_disposed)
            return;
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref _lastBatteryRead);
        if (!force && now - previous < 30_000)
            return;
        if (!force && Interlocked.CompareExchange(ref _lastBatteryRead, now, previous) != previous)
            return;
        if (force)
            Interlocked.Exchange(ref _lastBatteryRead, now);
        try
        {
            if (!GetSystemPowerStatus(out var power))
                throw new InvalidOperationException();
            var available = power.BatteryFlag != 128 && power.BatteryFlag != 255 && power.BatteryLifePercent <= 100;
            var charging = available && (power.BatteryFlag & 8) != 0;
            var minutes = power.BatteryLifeTime != uint.MaxValue ? (int?)(power.BatteryLifeTime / 60) : null;
            Battery = new BatterySnapshot
            {
                Available = available,
                Percent = available ? power.BatteryLifePercent : 0,
                Charging = charging,
                RemainingMinutes = available && !charging ? minutes : null,
                Status = !available ? "No battery reported by Windows" : charging ? "Charging" : power.ACLineStatus == 1 ? "Plugged in" : "On battery"
            };
        }
        catch { Battery = new BatterySnapshot { Status = "Windows battery information is unavailable" }; }
        RaiseChanged();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        _ = Task.Run(() => ReadBattery(true));
        if (args.Mode == PowerModes.Resume)
            _ = RefreshAsync();
    }
    private async Task ReadBrightnessAsync(bool initializeWatcher = false)
    {
        if (_disposed)
            return;
        await _brightnessGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT Active,CurrentBrightness,InstanceName FROM WmiMonitorBrightness");
                searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                using var results = searcher.Get();
                var monitors = results.Cast<ManagementObject>().ToArray();
                try
                {
                    var monitor = monitors.FirstOrDefault(m => Convert.ToBoolean(m["Active"])) ?? monitors.FirstOrDefault();
                    if (monitor is null)
                    {
                        BrightnessAvailable = false;
                        _brightnessInstance = null;
                        BrightnessStatus = "This display does not expose laptop brightness controls. Use Windows display settings.";
                        return;
                    }
                    _brightnessInstance = Convert.ToString(monitor["InstanceName"]);
                    Brightness = Convert.ToDouble(monitor["CurrentBrightness"]);
                    BrightnessAvailable = true;
                    BrightnessStatus = "Built-in display";
                    if (initializeWatcher && _brightnessWatcher is null && !_disposed)
                    {
                        try
                        {
                            var watcher = new ManagementEventWatcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
                            watcher.EventArrived += OnBrightnessChanged;
                            watcher.Start();
                            _brightnessWatcher = watcher;
                        }
                        catch { /* Bounded refresh on reveal remains available when WMI events are unsupported. */ }
                    }
                }
                finally { foreach (var monitor in monitors) monitor.Dispose(); }
            }).ConfigureAwait(false);
        }
        catch { BrightnessAvailable = false; _brightnessInstance = null; BrightnessStatus = "Brightness is unavailable on this hardware. Use Windows display settings."; }
        finally { _brightnessGate.Release(); RaiseChanged(); }
    }

    private void OnBrightnessChanged(object sender, EventArrivedEventArgs args)
    {
        if (_disposed)
            return;
        try
        {
            var instance = Convert.ToString(args.NewEvent["InstanceName"]);
            if (instance == _brightnessInstance)
            {
                Brightness = Convert.ToDouble(args.NewEvent["Brightness"]);
                RaiseChanged();
            }
        }
        catch { }
    }

    public async Task SetBrightnessAsync(double value)
    {
        if (_disposed || !BrightnessAvailable || _brightnessInstance is null)
            return;
        await _brightnessGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var instance = _brightnessInstance;
            await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                using var results = searcher.Get();
                foreach (ManagementObject method in results)
                {
                    using (method)
                    {
                        if (Convert.ToString(method["InstanceName"]) != instance)
                            continue;
                        method.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)Math.Clamp(Math.Round(value), 0, 100) });
                        Brightness = Math.Clamp(value, 0, 100);
                        break;
                    }
                }
            }).ConfigureAwait(false);
        }
        catch { BrightnessAvailable = false; BrightnessStatus = "Windows could not change brightness. Use Windows display settings."; }
        finally { _brightnessGate.Release(); RaiseChanged(); }
    }

    private void OnAudioChanged(object? sender, EventArgs args) => RaiseChanged();
    private void RaiseChanged()
    {
        if (_disposed)
            return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;
        dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }));
    }

    private void UnsubscribeSession()
    {
        if (_session is null)
            return;
        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Interlocked.Increment(ref _mediaGeneration);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _batteryTimer?.Dispose();
        _visibleTimer?.Dispose();
        if (_manager is not null)
            try
            {
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
            catch { }
        UnsubscribeSession();
        _session = null;
        _manager = null;
        if (_brightnessWatcher is not null)
            try
            {
                _brightnessWatcher.EventArrived -= OnBrightnessChanged;
                _brightnessWatcher.Stop();
                _brightnessWatcher.Dispose();
            }
            catch { }
        _brightnessWatcher = null;
        _audio.Changed -= OnAudioChanged;
        _audio.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus; public byte BatteryFlag; public byte BatteryLifePercent; public byte SystemStatusFlag;
        public uint BatteryLifeTime; public uint BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

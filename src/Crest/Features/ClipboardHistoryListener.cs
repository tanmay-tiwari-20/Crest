using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Crest.Features;

/// <summary>Message-only clipboard listener; registers only while opted in and unpaused.</summary>
public sealed class ClipboardHistoryListener : IDisposable
{
    private const int ClipboardUpdate = 0x031D;
    private readonly ClipboardHistoryHub _history;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _retry;
    private HwndSource? _source;
    private bool _started, _registered, _disposed;
    private uint _sequence;
    private long _generation;
    private int _attempts;
    private Task _pendingCapture = Task.CompletedTask;
    public string Status { get; private set; } = "Clipboard history is off.";

    public ClipboardHistoryListener(ClipboardHistoryHub history)
    {
        _history = history;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _retry = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
        _retry.Tick += (_, _) => ReadCurrentChange();
        _history.Changed += HistoryChanged;
    }

    public void Start()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _started = true;
        UpdateRegistration();
    }

    private void HistoryChanged(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess()) UpdateRegistration();
        else _dispatcher.BeginInvoke(UpdateRegistration);
    }

    private void UpdateRegistration()
    {
        if (_disposed || !_started) return;
        bool capturing = _history.Capturing;
        long generation = _history.CaptureGeneration;
        if (generation != _generation)
        {
            _generation = generation;
            _retry.Stop();
            if (_source != null) _sequence = GetClipboardSequenceNumber();
        }
        if (!capturing)
        {
            _retry.Stop();
            if (_registered && _source != null) RemoveClipboardFormatListener(_source.Handle);
            _registered = false;
            Status = _history.Enabled ? "Clipboard capture is paused." : "Clipboard history is off.";
            return;
        }
        if (_registered) return;
        _source ??= new HwndSource(new HwndSourceParameters("Crest clipboard monitor")
        {
            ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0
        });
        _source.RemoveHook(WindowMessage);
        _source.AddHook(WindowMessage);
        _sequence = GetClipboardSequenceNumber();
        _generation = _history.CaptureGeneration;
        _registered = AddClipboardFormatListener(_source.Handle);
        Status = _registered ? "Capturing new text copied on this PC." : "Windows clipboard monitoring is unavailable. Toggle history off and on to retry.";
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClipboardUpdate && _registered && _history.Capturing)
        {
            if (_generation != _history.CaptureGeneration)
            {
                UpdateRegistration();
                return IntPtr.Zero;
            }
            uint sequence = GetClipboardSequenceNumber();
            if (sequence != _sequence)
            {
                _sequence = sequence;
                _generation = _history.CaptureGeneration;
                _attempts = 0;
                ReadCurrentChange();
            }
        }
        return IntPtr.Zero;
    }

    private void ReadCurrentChange()
    {
        _retry.Stop();
        if (_disposed || !_registered || !_history.Capturing || _generation != _history.CaptureGeneration
            || _sequence != GetClipboardSequenceNumber()) return;
        if (!OpenClipboard(_source?.Handle ?? IntPtr.Zero))
        {
            if (++_attempts < 4) _retry.Start();
            else Status = "A clipboard change was busy; the next text copy will be captured.";
            return;
        }
        string? text = null;
        try
        {
            // Honor the Windows privacy markers used by password managers and other sensitive producers.
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) return;
            uint permitted = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (IsClipboardFormatAvailable(permitted) && ClipboardFlagIsZero(permitted)) return;
            const uint unicodeText = 13;
            if (!IsClipboardFormatAvailable(unicodeText)) return;
            IntPtr data = GetClipboardData(unicodeText);
            if (data == IntPtr.Zero) return;
            nuint bytes = GlobalSize(data);
            if (bytes < 2) return;
            IntPtr pointer = GlobalLock(data);
            if (pointer == IntPtr.Zero) return;
            try
            {
                int characters = (int)Math.Min((ulong)bytes / 2, (ulong)ClipboardHistoryHub.MaxTextCharacters + 1);
                text = Marshal.PtrToStringUni(pointer, characters);
                int terminator = text?.IndexOf('\0') ?? -1;
                if (terminator >= 0) text = text![..terminator];
                if (text?.Length > ClipboardHistoryHub.MaxTextCharacters)
                {
                    Status = $"Text longer than {ClipboardHistoryHub.MaxTextCharacters:N0} characters is skipped.";
                    text = null;
                }
            }
            finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }
        // Capture outside the OS clipboard lock, and verify that privacy/clear state has not changed.
        if (text != null && _generation == _history.CaptureGeneration && _history.Capturing)
        {
            string copiedText = text;
            long generation = _generation;
            // Serial background persistence preserves clipboard-change order and keeps disk writes out of the input loop.
            _pendingCapture = _pendingCapture.ContinueWith(_ =>
            {
                bool stored = _history.CaptureText(copiedText, generation);
                if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() =>
                {
                    if (!_disposed && generation == _history.CaptureGeneration && _history.Capturing)
                        Status = stored ? "Capturing new text copied on this PC." : "Text was skipped because history is full of pinned items or contains no text.";
                });
            }, TaskScheduler.Default);
        }
    }

    private static bool ClipboardFlagIsZero(uint format)
    {
        IntPtr data = GetClipboardData(format);
        if (data == IntPtr.Zero || GlobalSize(data) < 4) return false;
        IntPtr pointer = GlobalLock(data);
        if (pointer == IntPtr.Zero) return false;
        try { return Marshal.ReadInt32(pointer) == 0; }
        finally { GlobalUnlock(data); }
    }

    /// <summary>Explicitly restores text only; clear/pause never alter the Windows clipboard.</summary>
    public string? CopyToClipboard(string text)
    {
        _dispatcher.VerifyAccess();
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            _sequence = GetClipboardSequenceNumber();
            _retry.Stop();
            return null;
        }
        catch (ExternalException) { return "Windows clipboard is busy. Try Copy again."; }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _history.Changed -= HistoryChanged;
        _retry.Stop();
        if (_registered && _source != null) RemoveClipboardFormatListener(_source.Handle);
        _registered = false;
        _source?.Dispose();
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr data);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr data);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(IntPtr data);
}

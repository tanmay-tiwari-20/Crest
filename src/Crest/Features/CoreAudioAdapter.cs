using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Crest.Features;

internal sealed class CoreAudioAdapter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private EndpointCallback? _endpointCallback;
    private readonly DeviceCallback _deviceCallback;
    private bool _disposed;
    public event EventHandler? Changed;
    public double Volume
    {
        get; private set;
    }
    public bool Muted
    {
        get; private set;
    }
    public bool Available
    {
        get; private set;
    }
    public string Status { get; private set; } = "Checking audio output…";
    public string OutputName { get; private set; } = "System output";

    public CoreAudioAdapter() => _deviceCallback = new DeviceCallback(() => _ = RefreshAsync(true));

    public async Task RefreshAsync(bool reconnect = false)
    {
        if (_disposed)
            return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            if (_enumerator is null)
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_deviceCallback));
            }
            if (_volume is null || reconnect)
            {
                ReleaseEndpoint();
                Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
                try
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var result));
                    _volume = (IAudioEndpointVolume)result;
                    _endpointCallback = new EndpointCallback(() => _ = RefreshAsync());
                    Marshal.ThrowExceptionForHR(_volume.RegisterControlChangeNotify(_endpointCallback));
                    OutputName = ReadName(device);
                }
                finally { ReleaseCom(device); }
            }
            Marshal.ThrowExceptionForHR(_volume.GetMasterVolumeLevelScalar(out var scalar));
            Marshal.ThrowExceptionForHR(_volume.GetMute(out var mute));
            Volume = Math.Round(Math.Clamp(scalar * 100.0, 0, 100));
            Muted = mute;
            Available = true;
            Status = "System volume";
        }
        catch (Exception)
        {
            Available = false;
            Status = "No available audio output. Connect a speaker or headphones, then refresh.";
            ReleaseEndpoint();
        }
        finally { _gate.Release(); }
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetVolumeAsync(double value)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed && _volume is not null)
            {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(_volume.SetMasterVolumeLevelScalar((float)Math.Clamp(value / 100, 0, 1), ref context));
                Volume = Math.Clamp(value, 0, 100);
            }
        }
        catch { Available = false; Status = "The audio output changed. Refresh to reconnect."; }
        finally { _gate.Release(); }
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleMuteAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed && _volume is not null)
            {
                Marshal.ThrowExceptionForHR(_volume.GetMute(out var muted));
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(_volume.SetMute(!muted, ref context));
                Muted = !muted;
            }
        }
        catch { Available = false; Status = "The audio output changed. Refresh to reconnect."; }
        finally { _gate.Release(); }
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string ReadName(IMMDevice device)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out store));
            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "System output" : "System output";
        }
        catch { return "System output"; }
        finally { PropVariantClear(ref value); ReleaseCom(store); }
    }

    private void ReleaseEndpoint()
    {
        if (_volume is not null && _endpointCallback is not null)
            try
            {
                _volume.UnregisterControlChangeNotify(_endpointCallback);
            }
            catch { }
        _endpointCallback = null;
        ReleaseCom(_volume);
        _volume = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Wait();
        try
        {
            ReleaseEndpoint();
            if (_enumerator is not null)
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
                }
                catch { }
            ReleaseCom(_enumerator);
            _enumerator = null;
        }
        finally { _gate.Release(); }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch { }
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId; public uint PropertyId;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
    [ComVisible(true), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class EndpointCallback : IAudioEndpointVolumeCallback
    {
        private readonly Action _changed; public EndpointCallback(Action changed) => _changed = changed;
        public int OnNotify(IntPtr data)
        {
            ThreadPool.QueueUserWorkItem(_ => _changed());
            return 0;
        }
    }
    [ComVisible(true), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceCallback : IMMNotificationClient
    {
        private readonly Action _changed; public DeviceCallback(Action changed) => _changed = changed;
        private int Notify()
        {
            ThreadPool.QueueUserWorkItem(_ => _changed());
            return 0;
        }
        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => flow == 0 ? Notify() : 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }
}

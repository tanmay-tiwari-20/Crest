using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Crest.Features;

public sealed record AudioOutputInfo(string Id, string Name, bool IsDefault);

internal sealed class CoreAudioAdapter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _volume;
    private EndpointCallback? _endpointCallback;
    private readonly DeviceCallback _deviceCallback;
    private bool _disposed;
    private string _defaultOutputId = "";
    public IReadOnlyList<AudioOutputInfo> Outputs { get; private set; } = Array.Empty<AudioOutputInfo>();
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
                    Marshal.ThrowExceptionForHR(device.GetId(out _defaultOutputId));
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
            Outputs = ReadOutputs();
        }
        catch (Exception)
        {
            Available = false;
            Status = "No available audio output. Connect a speaker or headphones, then refresh.";
            ReleaseEndpoint();
            Outputs = _enumerator is null ? Array.Empty<AudioOutputInfo>() : ReadOutputs();
        }
        finally { _gate.Release(); }
        if (!_disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<AudioOutputInfo> ReadOutputs()
    {
        IMMDeviceCollection? collection = null;
        var outputs = new List<AudioOutputInfo>();
        try
        {
            Marshal.ThrowExceptionForHR(_enumerator!.EnumAudioEndpoints(0, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
            for (uint index = 0; index < Math.Min(count, 64); index++)
            {
                IMMDevice? device = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    outputs.Add(new(id, ReadName(device), Available && id == _defaultOutputId));
                }
                catch { }
                finally { ReleaseCom(device); }
            }
        }
        catch { }
        finally { ReleaseCom(collection); }
        return outputs.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToArray();
    }

    public async Task<string?> SwitchOutputAsync(string id)
    {
        string? error = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return "Audio controls have closed.";
            if (!Outputs.Any(x => x.Id == id)) return "That output is no longer connected. Refresh or open Windows sound settings.";
            object? policy = null;
            IMMDevice? device = null;
            try
            {
                // PolicyConfig is provided on many Windows builds, but is not a public
                // compatibility contract. Probe at runtime and verify the actual default.
                policy = new PolicyConfigClient();
                var config = (IPolicyConfig)policy;
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, 0));
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, 1));
                Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, 2));
                Marshal.ThrowExceptionForHR(_enumerator!.GetDefaultAudioEndpoint(0, 1, out device));
                Marshal.ThrowExceptionForHR(device.GetId(out string actual));
                if (!string.Equals(actual, id, StringComparison.Ordinal))
                    error = "Windows did not confirm the selected output. Choose it in Windows sound settings.";
            }
            catch { error = "Windows could not switch this output directly. Choose it in Windows sound settings."; }
            finally { ReleaseCom(device); ReleaseCom(policy); }
        }
        finally { _gate.Release(); }
        await RefreshAsync(true).ConfigureAwait(false);
        return error;
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
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
    }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, out long period, out long minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
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

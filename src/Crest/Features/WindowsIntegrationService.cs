using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Crest.Features;

public sealed class WindowsIntegrationService : IDisposable
{
    private readonly Func<WindowsIntegrationPreferences> _preferences;
    private readonly NotificationInbox _inbox = new();
    private readonly CancellationTokenSource _lifetime = new();
    private UserNotificationListener? _listener;
    private int _notificationBusy, _batteryBusy;
    private long _generation;
    private long _notificationGeneration, _phoneGeneration;
    private bool _notificationsEnabled, _phoneEnabled;
    private Timer? _retention;
    private bool _disposed;
    public event EventHandler? Changed;
    public IReadOnlyList<NotificationPreview> Notifications { get { _inbox.Expire(DateTimeOffset.UtcNow); return _inbox.Items; } }
    public IReadOnlyList<AccessoryBattery> Accessories { get; private set; } = Array.Empty<AccessoryBattery>();
    public string NotificationStatus { get; private set; } = "Notification access is off.";
    public string BatteryStatus { get; private set; } = "Accessory battery access is off.";
    public string PhoneStatus { get; private set; } = "Phone integration is off.";
    public bool PhoneAvailable { get; private set; }
    public WindowsIntegrationService(Func<WindowsIntegrationPreferences> preferences) { _preferences = preferences; ApplyPreferences(); }

    public void ApplyPreferences()
    {
        if (_disposed) return;
        if (_notificationsEnabled != _preferences().NotificationsEnabled)
        {
            _notificationsEnabled = _preferences().NotificationsEnabled; Interlocked.Increment(ref _notificationGeneration);
            if (_notificationsEnabled) { NotificationStatus = "Notification permission is required. Request access in Settings → Integrations."; _retention ??= new Timer(_ => { _inbox.Expire(DateTimeOffset.UtcNow); if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }, null, 60000, 60000); }
            else { _retention?.Dispose(); _retention = null; }
        }
        if (_phoneEnabled != _preferences().PhoneEnabled) { _phoneEnabled = _preferences().PhoneEnabled; Interlocked.Increment(ref _phoneGeneration); if (_phoneEnabled) PhoneStatus = "Check Phone Link support before opening the installed Windows app."; }
        if (!_preferences().NotificationsEnabled)
        {
            if (_listener != null) { try { _listener.NotificationChanged -= OnNotificationChanged; } catch { } _listener = null; }
            _inbox.Reset(); NotificationStatus = "Notification access is off.";
        }
        if (!_preferences().AccessoryBatteryEnabled) { Interlocked.Increment(ref _generation); Accessories = Array.Empty<AccessoryBattery>(); BatteryStatus = "Accessory battery access is off."; }
        if (!_preferences().PhoneEnabled) { PhoneAvailable = false; PhoneStatus = "Phone integration is off."; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    // Invoked only by an explicit Settings button on the UI thread.
    public async Task RequestNotificationAccessAsync()
    {
        if (_disposed || !_preferences().NotificationsEnabled) return;
        long generation = Interlocked.Read(ref _notificationGeneration);
        try
        {
            AttachListener();
            var result = await _listener!.RequestAccessAsync().AsTask(_lifetime.Token);
            if (_disposed || !_preferences().NotificationsEnabled || generation != Interlocked.Read(ref _notificationGeneration)) return;
            if (result != UserNotificationListenerAccessStatus.Allowed)
            {
                _inbox.Reset(); NotificationStatus = "Windows denied notification access. Enable it in Windows privacy settings, or disconnect this module.";
                Changed?.Invoke(this, EventArgs.Empty); return;
            }
            await RefreshNotificationsAsync();
        }
        catch (Exception) { if (!_disposed && _preferences().NotificationsEnabled && generation == Interlocked.Read(ref _notificationGeneration)) { _inbox.Reset(); NotificationStatus = "Windows notification access is unavailable for this package. It requires a Windows package with the notification-listener capability."; Changed?.Invoke(this, EventArgs.Empty); } }
    }
    private void AttachListener()
    {
        if (_listener != null) return;
        _listener = UserNotificationListener.Current;
        _listener.NotificationChanged += OnNotificationChanged;
    }
    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => _ = RefreshNotificationsAsync();
    public async Task RefreshNotificationsAsync()
    {
        if (_disposed || !_preferences().NotificationsEnabled || Interlocked.Exchange(ref _notificationBusy, 1) != 0) return;
        long generation = Interlocked.Read(ref _notificationGeneration);
        try
        {
            AttachListener();
            if (_listener!.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            {
                _inbox.Reset(); NotificationStatus = "Notification permission is not granted. Request access in Settings → Integrations."; return;
            }
            var incoming = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5));
            if (_disposed || !_preferences().NotificationsEnabled || generation != Interlocked.Read(ref _notificationGeneration)) return;
            if (_listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { _inbox.Reset(); NotificationStatus = "Notification permission was revoked."; return; }
            var previews = new List<NotificationPreview>();
            foreach (var item in incoming.Take(1000))
            {
                try
                {
                    var texts = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().Select(t => t.Text).ToArray() ?? Array.Empty<string>();
                    previews.Add(new(item.Id, item.AppInfo.DisplayInfo.DisplayName, texts.FirstOrDefault() ?? "Notification", string.Join("\n", texts.Skip(1)), item.CreationTime.ToUniversalTime()));
                }
                catch { /* A removed notification does not invalidate the others. */ }
            }
            _inbox.Update(previews, DateTimeOffset.UtcNow); NotificationStatus = "Windows previews · memory only · expire after 24 hours";
        }
        catch { if (!_disposed && _preferences().NotificationsEnabled && generation == Interlocked.Read(ref _notificationGeneration)) { _inbox.Reset(); NotificationStatus = "Windows notification access is unavailable. Check package capabilities and Windows privacy settings."; } }
        finally { Interlocked.Exchange(ref _notificationBusy, 0); if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    }
    public void DismissNotification(uint id) { _inbox.Dismiss(id, DateTimeOffset.UtcNow); Changed?.Invoke(this, EventArgs.Empty); }
    public void ClearNotifications() { Interlocked.Increment(ref _notificationGeneration); _inbox.Clear(DateTimeOffset.UtcNow); Changed?.Invoke(this, EventArgs.Empty); }
    public async Task RefreshAccessoriesAsync()
    {
        if (_disposed || !_preferences().AccessoryBatteryEnabled || Interlocked.Exchange(ref _batteryBusy, 1) != 0) return;
        long generation = Interlocked.Read(ref _generation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            BatteryStatus = "Reading paired Bluetooth LE battery services…"; Changed?.Invoke(this, EventArgs.Empty);
            var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
            var result = new List<AccessoryBattery>();
            foreach (var info in devices.Take(16))
            {
                timeout.Token.ThrowIfCancellationRequested();
                int? percent = null; string state = "No standard battery value exposed";
                using var deviceTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                deviceTimeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(deviceTimeout.Token);
                    if (device == null) state = "Windows denied access";
                    else if (device.ConnectionStatus != BluetoothConnectionStatus.Connected) state = "Disconnected";
                    else
                    {
                        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(deviceTimeout.Token);
                        try
                        {
                            if (services.Status == GattCommunicationStatus.Success)
                                foreach (var service in services.Services.Take(2))
                                {
                                    var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(deviceTimeout.Token);
                                    if (chars.Status != GattCommunicationStatus.Success) continue;
                                    foreach (var characteristic in chars.Characteristics.Take(1))
                                    {
                                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Cached).AsTask(deviceTimeout.Token);
                                        if (value.Status == GattCommunicationStatus.Success && value.Value.Length == 1)
                                        { using var reader = DataReader.FromBuffer(value.Value); byte raw = reader.ReadByte(); if (raw <= 100) { percent = raw; state = "Windows cached Bluetooth LE battery value"; } }
                                    }
                                }
                            else state = "Battery service unavailable or access denied";
                        }
                        finally { foreach (var service in services.Services) service.Dispose(); }
                    }
                }
                catch (OperationCanceledException) { if (timeout.IsCancellationRequested) throw; state = "Battery read timed out"; }
                catch { state = "Battery read unavailable"; }
                result.Add(new(info.Id, info.Name, percent, state, DateTimeOffset.UtcNow));
            }
            if (_disposed || !_preferences().AccessoryBatteryEnabled || generation != Interlocked.Read(ref _generation)) return;
            Accessories = result; BatteryStatus = result.Count == 0 ? "No paired Bluetooth LE accessories found. Pair devices in Windows Settings." : "Paired Bluetooth LE devices · refresh on request · no inferred battery values";
        }
        catch { if (!_disposed && _preferences().AccessoryBatteryEnabled && generation == Interlocked.Read(ref _generation)) BatteryStatus = "Bluetooth battery access timed out or is unavailable. Local controls remain available."; }
        finally { Interlocked.Exchange(ref _batteryBusy, 0); if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    }
    public async Task ProbePhoneAsync()
    {
        if (_disposed || !_preferences().PhoneEnabled) return;
        long generation = Interlocked.Read(ref _phoneGeneration);
        try
        {
            var support = await Launcher.QueryUriSupportAsync(new Uri("ms-phone:"), LaunchQuerySupportType.Uri).AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3));
            if (_disposed || !_preferences().PhoneEnabled || generation != Interlocked.Read(ref _phoneGeneration)) return;
            PhoneAvailable = support == LaunchQuerySupportStatus.Available;
            PhoneStatus = PhoneAvailable ? "Open Microsoft Phone Link for supported calls, messages and device actions. Pairing and permissions stay in Phone Link." : "Microsoft Phone Link is unavailable. Install or repair it through Windows.";
        }
        catch { if (!_disposed && _preferences().PhoneEnabled && generation == Interlocked.Read(ref _phoneGeneration)) { PhoneAvailable = false; PhoneStatus = "Windows could not check Phone Link support."; } }
        if (!_disposed) Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Clear() { _inbox.Reset(); Accessories = Array.Empty<AccessoryBattery>(); Interlocked.Increment(ref _generation); Interlocked.Increment(ref _notificationGeneration); Interlocked.Increment(ref _phoneGeneration); Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); Interlocked.Increment(ref _generation);
        _retention?.Dispose(); _retention = null;
        if (_listener != null) { try { _listener.NotificationChanged -= OnNotificationChanged; } catch { } _listener = null; }
        _inbox.Reset(); Accessories = Array.Empty<AccessoryBattery>();
    }
}

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HeartRateAntPlus;

public sealed record BleDeviceInfo(ulong Address, string Name)
{
    public string DisplayName => $"{Name}  ({Address:X12})";
}

public static class BleScanner
{
    public static async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan duration, Action<BleDeviceInfo>? onFound = null, CancellationToken cancellationToken = default)
    {
        var devices = new Dictionary<ulong, BleDeviceInfo>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var name = string.IsNullOrWhiteSpace(args.Advertisement.LocalName) ? "名称なし（BLEデバイス）" : args.Advertisement.LocalName;
            var info = new BleDeviceInfo(args.BluetoothAddress, name);
            if (devices.TryAdd(info.Address, info)) onFound?.Invoke(info);
        }
        watcher.Received += Received;
        watcher.Start();
        try { await Task.Delay(duration, cancellationToken); }
        finally { watcher.Stop(); watcher.Received -= Received; }
        return devices.Values.OrderBy(x => x.Name).ToArray();
    }
}

public interface IHeartRateSource : IDisposable
{
    string SensorId { get; }
    event EventHandler<int>? HeartRateChanged;
    event EventHandler? ConnectionLost;
    Task StartAsync(CancellationToken cancellationToken = default);
    void Poll();
}

public sealed class SimulatedHeartRateSource : IHeartRateSource
{
    private readonly Random _random = new();
    public string SensorId => "SIM-120";
    public event EventHandler<int>? HeartRateChanged;
    public event EventHandler? ConnectionLost { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) { HeartRateChanged?.Invoke(this, 72); return Task.CompletedTask; }
    public void Poll() => HeartRateChanged?.Invoke(this, 68 + _random.Next(0, 18));
    public void Dispose() { }
}

public sealed class BluetoothLeHeartRateSource : IHeartRateSource
{
    private static readonly Guid HeartRateService = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
    private static readonly Guid HeartRateMeasurement = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");
    private readonly ulong _address;
    private readonly string _selectedName;
    private BluetoothLEDevice? _device;
    private GattCharacteristic? _characteristic;
    private bool _disposed;
    public string SensorId { get; private set; }
    public event EventHandler<int>? HeartRateChanged;
    public event EventHandler? ConnectionLost;

    public BluetoothLeHeartRateSource(BleDeviceInfo selected) { _address = selected.Address; _selectedName = selected.Name; SensorId = selected.Name; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(_address) ?? throw new InvalidOperationException("選択したBluetooth LEデバイスに接続できませんでした。");
        _device.ConnectionStatusChanged += DeviceConnectionStatusChanged;
        SensorId = string.IsNullOrWhiteSpace(_device.Name) ? _selectedName : _device.Name;
        var serviceResult = await _device.GetGattServicesForUuidAsync(HeartRateService, BluetoothCacheMode.Uncached);
        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0) throw new InvalidOperationException("選択したデバイスに心拍サービス(180D)がありません。心拍センサを選択してください。");
        var characteristics = await serviceResult.Services[0].GetCharacteristicsForUuidAsync(HeartRateMeasurement, BluetoothCacheMode.Uncached);
        if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0) throw new InvalidOperationException("心拍測定特性(2A37)を取得できませんでした。");
        _characteristic = characteristics.Characteristics[0];
        _characteristic.ValueChanged += OnValueChanged;
        var status = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (status != GattCommunicationStatus.Success) throw new InvalidOperationException($"心拍通知を有効化できませんでした: {status}");
    }

    private void DeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (!_disposed && sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) ConnectionLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(bytes);
        if (bytes.Length < 2) return;
        var flags = bytes[0]; int bpm;
        if ((flags & 0x01) == 0) bpm = bytes[1];
        else if (bytes.Length >= 3) bpm = BitConverter.ToUInt16(bytes, 1);
        else return;
        if (bpm is > 0 and < 250) HeartRateChanged?.Invoke(this, bpm);
    }

    public void Poll() { }
    public void Dispose()
    {
        _disposed = true;
        if (_characteristic is not null) _characteristic.ValueChanged -= OnValueChanged;
        if (_device is not null) { _device.ConnectionStatusChanged -= DeviceConnectionStatusChanged; _device.Dispose(); }
    }
}

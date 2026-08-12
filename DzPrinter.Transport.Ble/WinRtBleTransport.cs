// =====================================================================
//  Windows BLE 传输层实现。基于 WinRT 的 BluetoothLEDevice / GattCharacteristic。
//  对应 JS SDK 中 We (WebBluetoothAdapter) 与 hi (BleConnection) 的底层逻辑。
//
//  实现策略：
//    1. 通过 BluetoothLEAdvertisementWatcher 扫描（对应 JS discover）。
//    2. 通过 BluetoothLEDevice.FromIdAsync 连接设备（对应 JS createBLEConnection）。
//    3. GATT 服务默认 UUID 0x18F0；可写特征与通知特征按 NIIM/德佟标准匹配。
//    4. 写操作按 MTU-3 自动分包（默认每包 20 字节），对应 uni-app
//       uni.writeBLECharacteristicValue 的分包写入逻辑。
// =====================================================================

using DzPrinter.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace DzPrinter.Transport.Ble;

/// <summary>BLE 连接选项。</summary>
public sealed class BleConnectionOptions
{
    /// <summary>服务 UUID。默认德佟/璞趣通用服务 000018F0-0000-1000-8000-00805F9B34FB。</summary>
    public Guid ServiceUuid { get; set; } = new("000018F0-0000-1000-8000-00805F9B34FB");
    /// <summary>可写特征 UUID（可选，默认自动查找可写特征）。</summary>
    public Guid? WriteCharacteristicUuid { get; set; }
    /// <summary>通知/读特征 UUID（可选，默认自动查找）。</summary>
    public Guid? NotifyCharacteristicUuid { get; set; }
    /// <summary>每包最大字节数（MTU-3，Ble 默认 20）。</summary>
    public int PackSize { get; set; } = 20;
    /// <summary>扫描时间（毫秒）。</summary>
    public int ScanTimeoutMs { get; set; } = 3000;
}

/// <summary>
/// Windows BLE <see cref="IDeviceTransport"/> 实现。
/// 对应 JS 中 WebBluetooth 适配器 + BleConnection 底层发送逻辑。
/// </summary>
public sealed class WinRtBleTransport : IDeviceTransport, IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly BleConnectionOptions _options;
    private readonly object _sync = new();

    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _writeChar;
    private GattCharacteristic? _notifyChar;

    private Transport.ConnectionState _state = Transport.ConnectionState.Disconnected;
    private Transport.DeviceInfo? _connected;

    // 响应等待队列
    private readonly List<byte> _notifyBuffer = new();

    public WinRtBleTransport() : this(new BleConnectionOptions()) { }

    public WinRtBleTransport(BleConnectionOptions options)
    {
        _options = options ?? new BleConnectionOptions();
    }

    // ============ 属性 ============

    public Transport.ConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public Transport.DeviceInfo? ConnectedDevice
    {
        get { lock (_sync) return _connected; }
    }

    public event EventHandler<Transport.DataReceivedEventArgs>? DataReceived;
    public event EventHandler<Transport.ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    // ============ 公共方法 ============

    public async Task<IReadOnlyList<Transport.DeviceInfo>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        Log.Info("【WinRtBleTransport】DiscoverAsync() start");
        var found = new Dictionary<string, Transport.DeviceInfo>();

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        var tcs = new TaskCompletionSource<bool>();
        void OnReceived(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementReceivedEventArgs a)
        {
            var name = string.IsNullOrEmpty(a.Advertisement.LocalName)
                ? "(unknown)"
                : a.Advertisement.LocalName;
            var id = a.BluetoothAddress.ToString("X16");
            lock (found)
            {
                if (!found.ContainsKey(id))
                {
                    found[id] = new Transport.DeviceInfo
                    {
                        DeviceId = id,
                        DeviceName = name,
                        TransportType = Transport.TransportType.BluetoothLowEnergy,
                        NativeDevice = a.BluetoothAddress,
                    };
                }
            }
        }
        watcher.Received += OnReceived;

        void OnStopped(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementWatcherStoppedEventArgs a)
        {
            tcs.TrySetResult(true);
        }
        watcher.Stopped += OnStopped;

        _watcher = watcher; // 保持引用，避免 GC 提前回收
        watcher.Start();
        try
        {
            await Task.WhenAny(Task.Delay(_options.ScanTimeoutMs, cancellationToken), tcs.Task)
                .ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= OnReceived;
            watcher.Stopped -= OnStopped;
            if (ReferenceEquals(_watcher, watcher)) _watcher = null;
        }

        Log.Info($"【WinRtBleTransport】DiscoverAsync() found {found.Count} devices");
        return found.Values.ToList();
    }

    public async Task ConnectAsync(Transport.DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        SetState(Transport.ConnectionState.Connecting);
        try
        {
            ulong addr = device.NativeDevice is ulong a
                ? a
                : ulong.TryParse(device.DeviceId, System.Globalization.NumberStyles.HexNumber,
                    null, out var parsed) ? parsed : 0;

            BluetoothLEDevice? bleDev;
            if (addr != 0)
            {
                bleDev = await BluetoothLEDevice.FromBluetoothAddressAsync(addr)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                bleDev = await BluetoothLEDevice.FromIdAsync(device.DeviceId)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }

            if (bleDev == null) throw new InvalidOperationException($"无法创建 BLE 设备：{device}");

            var result = await bleDev.GetGattServicesForUuidAsync(_options.ServiceUuid)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0)
            {
                throw new InvalidOperationException($"未找到 GATT 服务 {_options.ServiceUuid}");
            }

            var service = result.Services[0];
            GattCharacteristic? writeChar = null, notifyChar = null;

            var chars = await service.GetCharacteristicsAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
            foreach (var c in chars.Characteristics)
            {
                var props = c.CharacteristicProperties;
                if (writeChar == null &&
                    (props.HasFlag(GattCharacteristicProperties.Write) ||
                     props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)))
                {
                    writeChar = c;
                }
                if (notifyChar == null &&
                    (props.HasFlag(GattCharacteristicProperties.Notify) ||
                     props.HasFlag(GattCharacteristicProperties.Indicate)))
                {
                    notifyChar = c;
                }
            }

            if (writeChar == null) throw new InvalidOperationException("未找到可写 GATT 特征");
            if (notifyChar != null)
            {
                await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                notifyChar.ValueChanged += OnNotifyValueChanged;
            }

            lock (_sync)
            {
                _device = bleDev;
                _service = service;
                _writeChar = writeChar;
                _notifyChar = notifyChar;
                _connected = device;
            }

            SetState(Transport.ConnectionState.Connected);
            Log.Info($"【WinRtBleTransport】Connected to {device}");
        }
        catch (Exception ex)
        {
            Log.Error($"【WinRtBleTransport】Connect failed: {ex.Message}");
            SetState(Transport.ConnectionState.Failed, ex.Message);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(Transport.ConnectionState.Disconnecting);
        try
        {
            lock (_sync)
            {
                if (_notifyChar != null) _notifyChar.ValueChanged -= OnNotifyValueChanged;
                _notifyChar = null;
                _writeChar = null;
                _service?.Dispose();
                _service = null;
                _device?.Dispose();
                _device = null;
                _connected = null;
            }
        }
        finally
        {
            SetState(Transport.ConnectionState.Disconnected);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        GattCharacteristic? writeChar;
        lock (_sync) writeChar = _writeChar;
        if (writeChar == null) throw new InvalidOperationException("未连接，无法发送");

        int packSize = Math.Max(1, _options.PackSize);
        int sent = 0;
        while (sent < data.Length)
        {
            int len = Math.Min(packSize, data.Length - sent);
            var slice = data.Slice(sent, len);
            using var writer = new DataWriter();
            writer.WriteBytes(slice.ToArray());
            var result = await writeChar.WriteValueAsync(writer.DetachBuffer(),
                GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken).ConfigureAwait(false);
            if (result != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"BLE write failed status={result}");
            }
            sent += len;
        }
    }

    public Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        // 简化实现：发送后等待 timeoutMs 内积累的 notify 数据。
        return RequestAsyncImpl(data, timeoutMs, cancellationToken);
    }

    private async Task<byte[]?> RequestAsyncImpl(ReadOnlyMemory<byte> data, int timeoutMs,
        CancellationToken cancellationToken)
    {
        await SendAsync(data, cancellationToken).ConfigureAwait(false);
        lock (_notifyBuffer) _notifyBuffer.Clear();
        await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
        lock (_notifyBuffer)
        {
            return _notifyBuffer.Count == 0 ? null : _notifyBuffer.ToArray();
        }
    }

    public void Dispose()
    {
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
    }

    // ============ 私有方法 ============

    private void OnNotifyValueChanged(GattCharacteristic s, GattValueChangedEventArgs a)
    {
        using var reader = DataReader.FromBuffer(a.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        lock (_notifyBuffer) _notifyBuffer.AddRange(bytes);
        DataReceived?.Invoke(this, new Transport.DataReceivedEventArgs(bytes));
    }

    private void SetState(Transport.ConnectionState state, string? msg = null)
    {
        lock (_sync) _state = state;
        ConnectionStateChanged?.Invoke(this,
            new Transport.ConnectionStateChangedEventArgs(state, msg));
    }
}

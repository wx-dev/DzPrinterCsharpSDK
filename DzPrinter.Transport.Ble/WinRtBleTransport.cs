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
using System.Collections.Concurrent;
using System.Collections.Generic;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace DzPrinter.Transport.Ble;

/// <summary>BLE 传输层选项。</summary>
public sealed class BleTransportOptions
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
public sealed class WinRtBleTransport : Transport.TransportBase
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly BleTransportOptions _options;

    private BluetoothLEAdvertisementWatcher? _watcher;
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _writeChar;
    private GattCharacteristic? _notifyChar;

    public WinRtBleTransport() : this(new BleTransportOptions()) { }

    public WinRtBleTransport(BleTransportOptions options)
    {
        _options = options ?? new BleTransportOptions();
    }

    // ============ 属性 ============

    /// <inheritdoc />
    public override Transport.TransportType TransportType => Transport.TransportType.BluetoothLowEnergy;

    // ============ 公共方法 ============

    public override async Task<IReadOnlyList<Transport.DeviceInfo>> DiscoverAsync(
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

    public override async Task ConnectAsync(Transport.DeviceInfo device,
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

            // Windows BLE 已知时序问题：FromBluetoothAddressAsync 返回后 GATT 服务可能尚未就绪。
            // 重试 3 次，每次间隔 500ms。
            GattDeviceService? service = null;
            Exception? lastError = null;
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        Log.Info($"【WinRtBleTransport】ConnectAsync() —— 重试 {attempt}/{maxRetries} ...");
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    }

                    var result = await bleDev.GetGattServicesForUuidAsync(_options.ServiceUuid,
                        BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken).ConfigureAwait(false);

                    if (result.Status == GattCommunicationStatus.Success && result.Services.Count > 0)
                    {
                        service = result.Services[0];
                        Log.Info($"【WinRtBleTransport】GATT 服务已就绪（第 {attempt} 次尝试）");
                        break;
                    }

                    Log.Warn($"【WinRtBleTransport】GetGattServicesForUuidAsync 第 {attempt} 次：" +
                             $"status={result.Status}, count={result.Services.Count}");
                }
                catch (Exception retryEx)
                {
                    lastError = retryEx;
                    Log.Warn($"【WinRtBleTransport】GetGattServicesForUuidAsync 第 {attempt} 次异常: {retryEx.Message}");
                }
            }

            // 如果按指定 UUID 查找失败，枚举所有服务以辅助诊断
            if (service == null)
            {
                Log.Warn($"【WinRtBleTransport】未找到服务 {_options.ServiceUuid}，枚举所有 GATT 服务 ...");
                try
                {
                    var allServices = await bleDev.GetGattServicesAsync(BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken).ConfigureAwait(false);

                    if (allServices.Status == GattCommunicationStatus.Success)
                    {
                        var svcList = string.Join(", ",
                            allServices.Services.Select(s => s.Uuid.ToString()));
                        Log.Info($"【WinRtBleTransport】设备实际 GATT 服务: [{svcList}]");

                        // 尝试匹配常见的打印服务 UUID 作为回退
                        var fallbackUuids = new[]
                        {
                            new Guid("0000FFE0-0000-1000-8000-00805F9B34FB"), // CC2541/HC-08
                            new Guid("0000FF00-0000-1000-8000-00805F9B34FB"), // 常见模块
                        };
                        foreach (var fb in fallbackUuids)
                        {
                            var match = allServices.Services.FirstOrDefault(s => s.Uuid == fb);
                            if (match != null)
                            {
                                service = match;
                                Log.Info($"【WinRtBleTransport】回退使用服务: {fb}");
                                break;
                            }
                        }
                    }
                }
                catch (Exception enumEx)
                {
                    Log.Warn($"【WinRtBleTransport】枚举所有服务失败: {enumEx.Message}");
                }
            }

            if (service == null)
            {
                throw new InvalidOperationException(
                    $"未找到 GATT 服务 {_options.ServiceUuid}（重试 {maxRetries} 次后仍失败）");
            }

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
                notifyChar.ValueChanged += OnNotifyValueChanged;
                await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }

            lock (_sync)
            {
                _device = bleDev;
                _service = service;
                _writeChar = writeChar;
                _notifyChar = notifyChar;
                _connectedDevice = device;
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

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
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
                _connectedDevice = null;
            }
        }
        finally
        {
            SetState(Transport.ConnectionState.Disconnected);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public override async Task SendAsync(ReadOnlyMemory<byte> data,
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

            // 【新增】打印当前分包的十六进制数组，方便排查协议数据
            string hexDump = ByteUtils.ToHexString(slice.Span);
            Console.WriteLine($"[BLE TX] 偏移={sent}, 长度={len}, 数据=[{hexDump}]");

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

    // ============ 私有方法 ============

    private void OnNotifyValueChanged(GattCharacteristic s, GattValueChangedEventArgs a)
    {
        using var reader = DataReader.FromBuffer(a.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);

        string hexDump = ByteUtils.ToHexString(bytes);
        Console.WriteLine($"[BLE RX] {bytes.Length} bytes: [{hexDump}]");

        lock (_sync)
        {
            if (_pendingResponse != null)
            {
                _responseBuffer.AddRange(bytes);
                var frame = TryExtractProtocolFrame(_responseBuffer);
                if (frame != null)
                {
                    Console.WriteLine($"[BLE RX] 完整帧: {frame.Length} bytes: {ByteUtils.ToHexString(frame)}");
                    _pendingResponse.TrySetResult(frame);
                    return;
                }
                // 帧不完整，继续累积等待后续通知
                return;
            }
        }

        RaiseDataReceived(bytes);
    }
}

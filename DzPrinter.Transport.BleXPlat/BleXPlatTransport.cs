// =====================================================================
//  跨平台 BLE 传输层实现。基于 Plugin.BLE (dotnet-bluetooth-le) 库。
//  对应 JS SDK 中 We (WebBluetoothAdapter) 与 hi (BleConnection) 的底层逻辑。
//
//  与 DzPrinter.Transport.Ble.WinRtBleTransport 并存：
//    - WinRtBleTransport：直接使用 Windows.Devices.Bluetooth WinRT API，仅 Windows。
//    - BleXPlatTransport：通过 Plugin.BLE 间接调用，跨平台（Win/Linux/macOS）。
//  首期仅 Windows 平台验证，验证通过后可移除 WinRtBleTransport。
//
//  实现策略（与 WinRT 版本逻辑对应）：
//    1. adapter.StartScanningForDevicesAsync 扫描（对应 JS discover）。
//    2. adapter.ConnectToDeviceAsync 连接设备（对应 JS createBLEConnection）。
//    3. GATT 服务默认 UUID 0x18F0；可写/通知特征按 NIIM/德佟标准匹配。
//    4. 写操作按 PackSize 自动分包（默认每包 20 字节）。
//    5. Windows BLE 已知时序问题（GATT 服务可能未立即就绪）：重试 3 次，每次间隔 500ms。
// =====================================================================

using DzPrinter.Core;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace DzPrinter.Transport.BleXPlat;

/// <summary>跨平台 BLE 传输层选项。字段与 <c>BleTransportOptions</c> 保持一致以便迁移。</summary>
public sealed class BleXPlatTransportOptions
{
    /// <summary>服务 UUID。默认德佟/璞趣通用服务 000018F0-0000-1000-8000-00805F9B34FB。</summary>
    public Guid ServiceUuid { get; set; } = new("000018F0-0000-1000-8000-00805F9B34FB");
    /// <summary>可写特征 UUID（可选，默认自动查找可写特征）。</summary>
    public Guid? WriteCharacteristicUuid { get; set; }
    /// <summary>通知/读特征 UUID（可选，默认自动查找）。</summary>
    public Guid? NotifyCharacteristicUuid { get; set; }
    /// <summary>每包最大字节数（MTU-3，BLE 默认 20）。</summary>
    public int PackSize { get; set; } = 20;
    /// <summary>扫描时间（毫秒）。</summary>
    public int ScanTimeoutMs { get; set; } = 3000;
    /// <summary>GATT 服务发现重试次数（应对 Windows BLE 时序问题）。</summary>
    public int ServiceDiscoveryRetries { get; set; } = 3;
    /// <summary>每次重试间隔毫秒。</summary>
    public int ServiceDiscoveryRetryDelayMs { get; set; } = 500;
}

/// <summary>
/// 跨平台 BLE <see cref="Transport.IDeviceTransport"/> 实现，基于 Plugin.BLE。
/// 对应 JS 中 WebBluetooth 适配器 + BleConnection 底层发送逻辑。
/// </summary>
public sealed class BleXPlatTransport : Transport.TransportBase
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly BleXPlatTransportOptions _options;
    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;

    private IDevice? _device;
    private IService? _service;
    private ICharacteristic? _writeChar;
    private ICharacteristic? _notifyChar;

    public BleXPlatTransport() : this(new BleXPlatTransportOptions()) { }

    public BleXPlatTransport(BleXPlatTransportOptions options)
    {
        _options = options ?? new BleXPlatTransportOptions();
        _ble = CrossBluetoothLE.Current;
        _adapter = _ble.Adapter;
    }

    // ============ 属性 ============

    /// <inheritdoc />
    public override Transport.TransportType TransportType =>
        Transport.TransportType.BluetoothLowEnergy;

    // ============ 公共方法 ============

    public override async Task<IReadOnlyList<Transport.DeviceInfo>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        Log.Info("【BleXPlatTransport】DiscoverAsync() start");
        var found = new Dictionary<string, Transport.DeviceInfo>();

        void OnDeviceDiscovered(object? s, DeviceEventArgs a)
        {
            var id = a.Device.Id.ToString();
            var name = string.IsNullOrEmpty(a.Device.Name) ? "(unknown)" : a.Device.Name;
            lock (found)
            {
                if (!found.ContainsKey(id))
                {
                    found[id] = new Transport.DeviceInfo
                    {
                        DeviceId = id,
                        DeviceName = name,
                        TransportType = Transport.TransportType.BluetoothLowEnergy,
                        NativeDevice = a.Device,
                    };
                }
            }
        }

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.ScanTimeout = _options.ScanTimeoutMs;
        try
        {
            await _adapter.StartScanningForDevicesAsync().ConfigureAwait(false);
        }
        finally
        {
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;
        }

        Log.Info($"【BleXPlatTransport】DiscoverAsync() found {found.Count} devices");
        return found.Values.ToList();
    }

    public override async Task ConnectAsync(Transport.DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        SetState(Transport.ConnectionState.Connecting);
        try
        {
            IDevice bleDevice;
            if (device.NativeDevice is IDevice nativeDev)
            {
                bleDevice = nativeDev;
                await _adapter.ConnectToDeviceAsync(bleDevice, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var guid = Guid.Parse(device.DeviceId);
                bleDevice = await _adapter.ConnectToKnownDeviceAsync(guid, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            // Windows BLE 已知时序问题：连接后 GATT 服务可能尚未就绪。重试多次。
            IService? service = null;
            int maxRetries = Math.Max(1, _options.ServiceDiscoveryRetries);
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (attempt > 1)
                {
                    Log.Info($"【BleXPlatTransport】ConnectAsync() —— 重试 {attempt}/{maxRetries} ...");
                    await Task.Delay(_options.ServiceDiscoveryRetryDelayMs, cancellationToken)
                        .ConfigureAwait(false);
                }

                try
                {
                    service = await bleDevice.GetServiceAsync(_options.ServiceUuid, cancellationToken)
                        .ConfigureAwait(false);
                    if (service != null)
                    {
                        Log.Info($"【BleXPlatTransport】GATT 服务已就绪（第 {attempt} 次尝试）");
                        break;
                    }

                    Log.Warn($"【BleXPlatTransport】GetServiceAsync 第 {attempt} 次返回 null");
                }
                catch (Exception retryEx)
                {
                    Log.Warn($"【BleXPlatTransport】GetServiceAsync 第 {attempt} 次异常: {retryEx.Message}");
                }
            }

            // 按指定 UUID 查找失败时，枚举所有服务以辅助诊断并尝试常见回退 UUID
            if (service == null)
            {
                Log.Warn($"【BleXPlatTransport】未找到服务 {_options.ServiceUuid}，枚举所有 GATT 服务 ...");
                try
                {
                    var allServices = await bleDevice.GetServicesAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var svcList = string.Join(", ", allServices.Select(s => s.Id.ToString()));
                    Log.Info($"【BleXPlatTransport】设备实际 GATT 服务: [{svcList}]");

                    var fallbackUuids = new[]
                    {
                        new Guid("0000FFE0-0000-1000-8000-00805F9B34FB"), // CC2541/HC-08
                        new Guid("0000FF00-0000-1000-8000-00805F9B34FB"), // 常见模块
                    };
                    foreach (var fb in fallbackUuids)
                    {
                        var match = allServices.FirstOrDefault(s => s.Id == fb);
                        if (match != null)
                        {
                            service = match;
                            Log.Info($"【BleXPlatTransport】回退使用服务: {fb}");
                            break;
                        }
                    }
                }
                catch (Exception enumEx)
                {
                    Log.Warn($"【BleXPlatTransport】枚举所有服务失败: {enumEx.Message}");
                }
            }

            if (service == null)
            {
                throw new InvalidOperationException(
                    $"未找到 GATT 服务 {_options.ServiceUuid}（重试 {maxRetries} 次后仍失败）");
            }

            ICharacteristic? writeChar = null, notifyChar = null;

            // 优先按 UUID 查找；未指定则枚举所有特征按属性匹配
            if (_options.WriteCharacteristicUuid.HasValue)
            {
                writeChar = await service.GetCharacteristicAsync(_options.WriteCharacteristicUuid.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            if (_options.NotifyCharacteristicUuid.HasValue)
            {
                notifyChar = await service.GetCharacteristicAsync(_options.NotifyCharacteristicUuid.Value,
                    cancellationToken).ConfigureAwait(false);
            }

            if (writeChar == null || notifyChar == null)
            {
                var chars = await service.GetCharacteristicsAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var c in chars)
                {
                    if (writeChar == null && c.CanWrite)
                        writeChar = c;
                    if (notifyChar == null && c.CanUpdate)
                        notifyChar = c;
                }
            }

            if (writeChar == null) throw new InvalidOperationException("未找到可写 GATT 特征");
            if (notifyChar != null)
            {
                notifyChar.ValueUpdated += OnNotifyValueUpdated;
                await notifyChar.StartUpdatesAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_sync)
            {
                _device = bleDevice;
                _service = service;
                _writeChar = writeChar;
                _notifyChar = notifyChar;
                _connectedDevice = device;
            }

            SetState(Transport.ConnectionState.Connected);
            Log.Info($"【BleXPlatTransport】Connected to {device}");
        }
        catch (Exception ex)
        {
            Log.Error($"【BleXPlatTransport】Connect failed: {ex.Message}");
            SetState(Transport.ConnectionState.Failed, ex.Message);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(Transport.ConnectionState.Disconnecting);
        try
        {
            IDevice? device;
            ICharacteristic? notifyChar;
            lock (_sync)
            {
                device = _device;
                notifyChar = _notifyChar;
                if (notifyChar != null) notifyChar.ValueUpdated -= OnNotifyValueUpdated;
                _notifyChar = null;
                _writeChar = null;
                _service = null;
                _device = null;
                _connectedDevice = null;
            }
            if (device != null)
            {
                try
                {
                    await _adapter.DisconnectDeviceAsync(device).ConfigureAwait(false);
                }
                catch (Exception discEx)
                {
                    Log.Warn($"【BleXPlatTransport】DisconnectDeviceAsync 异常: {discEx.Message}");
                }
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
        ICharacteristic? writeChar;
        lock (_sync) writeChar = _writeChar;
        if (writeChar == null) throw new InvalidOperationException("未连接，无法发送");

        int packSize = Math.Max(1, _options.PackSize);
        int sent = 0;
        while (sent < data.Length)
        {
            int len = Math.Min(packSize, data.Length - sent);
            var slice = data.Slice(sent, len);

            string hexDump = ByteUtils.ToHexString(slice.Span);
            Console.WriteLine($"[BLE-X TX] 偏移={sent}, 长度={len}, 数据=[{hexDump}]");

            await writeChar.WriteAsync(slice.ToArray(), cancellationToken).ConfigureAwait(false);
            sent += len;
        }
    }

    // ============ 私有方法 ============

    private void OnNotifyValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var bytes = e.Characteristic.Value ?? Array.Empty<byte>();
        if (bytes.Length == 0) return;

        string hexDump = ByteUtils.ToHexString(bytes);
        Console.WriteLine($"[BLE-X RX] {bytes.Length} bytes: [{hexDump}]");

        lock (_sync)
        {
            if (_pendingResponse != null)
            {
                _responseBuffer.AddRange(bytes);
                var frame = TryExtractProtocolFrame(_responseBuffer);
                if (frame != null)
                {
                    Console.WriteLine($"[BLE-X RX] 完整帧: {frame.Length} bytes: {ByteUtils.ToHexString(frame)}");
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

using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  BleConnection（BLE 蓝牙连接）。对应 JS SDK 中 <c>hi</c> 类。
//  JS 中 <c>hi extends ai</c>，针对低功耗蓝牙实现：
//    - GATT 服务/特征发现
//    - 写特征（writeCharacteristic）
//    - 通知特征（notifyCharacteristic）
//    - MTU 协商
//    - 数据分片发送
//
//  C# 实现策略：
//   - JS 通过 uni-app 的 <c>uni.createBLEConnection</c> 等接口操作蓝牙；
//     C# 中将 BLE 操作抽象为 <see cref="IDeviceTransport"/>，
//     由宿主应用注入 WinRT BLE / CoreBluetooth 等具体实现。
//   - 本类添加 BLE 特有的配置项（服务 UUID、写特征 UUID、通知特征 UUID、MTU），
//     并提供数据分片发送逻辑（对应 JS 中的 BLE 分包）。
// =====================================================================

/// <summary>
/// 低功耗蓝牙（BLE）连接。对应 JS SDK 中的 <c>hi</c>（BleConnection）类。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>hi</c> 在 <c>ai</c> 基础上添加了 GATT 服务/特征管理、
/// MTU 协商、数据分片等功能。C# 中将 GATT 操作下沉到 <see cref="IDeviceTransport"/>，
/// 本类保留 BLE 配置与分片逻辑。</para>
/// <para><b>分片策略</b>：BLE 单次写入最大 20 字节（默认 MTU 23 减去 3 字节 ATT 头），
/// 超过时自动分片发送。对应 JS <c>sendDataInBleMode</c>。</para>
/// </remarks>
public sealed class BleConnection : DeviceConnection
{
    /// <summary>
    /// BLE 默认 MTU（最大传输单元）。对应 JS <c>DEFAULT_MTU = 23</c>。
    /// 实际有效负载 = MTU - 3（ATT 协议头）。
    /// </summary>
    public const int DefaultMtu = 23;

    /// <summary>
    /// BLE 默认单包最大数据长度。对应 JS <c>DEFAULT_PACK_SIZE = 20</c>。
    /// 即 DefaultMtu - 3。
    /// </summary>
    public const int DefaultPackSize = DefaultMtu - 3;

    /// <summary>德佟打印机默认 GATT 服务 UUID。</summary>
    public const string DefaultServiceUuid = "000018F0-0000-1000-8000-00805F9B34FB";

    /// <summary>德佟打印机默认写特征 UUID。</summary>
    public const string DefaultWriteCharacteristicUuid = "00002AF1-0000-1000-8000-00805F9B34FB";

    /// <summary>德佟打印机默认通知特征 UUID。</summary>
    public const string DefaultNotifyCharacteristicUuid = "00002AF0-0000-1000-8000-00805F9B34FB";

    /// <summary>BLE 单包最大字节数（MTU - 3）。</summary>
    public int PackSize { get; set; } = DefaultPackSize;

    /// <summary>GATT 服务 UUID。</summary>
    public string ServiceUuid { get; set; } = DefaultServiceUuid;

    /// <summary>写特征 UUID。</summary>
    public string WriteCharacteristicUuid { get; set; } = DefaultWriteCharacteristicUuid;

    /// <summary>通知特征 UUID。</summary>
    public string NotifyCharacteristicUuid { get; set; } = DefaultNotifyCharacteristicUuid;

    /// <summary>分片发送间隔（毫秒）。对应 JS <c>sendInterval</c>。默认 20ms。</summary>
    public int SendIntervalMs { get; set; } = 20;

    /// <inheritdoc />
    public override LpaDeviceType DeviceType => LpaDeviceType.Ble;

    /// <summary>
    /// 构造 BLE 连接。对应 JS <c>hi.constructor(options)</c>。
    /// </summary>
    /// <param name="transport">BLE 传输层实现。</param>
    /// <param name="options">可选配置（服务/特征 UUID、MTU 等）。</param>
    public BleConnection(IDeviceTransport transport, BleConnectionOptions? options = null)
        : base(transport)
    {
        if (options != null)
        {
            ServiceUuid = options.ServiceUuid ?? ServiceUuid;
            WriteCharacteristicUuid = options.WriteCharacteristicUuid ?? WriteCharacteristicUuid;
            NotifyCharacteristicUuid = options.NotifyCharacteristicUuid ?? NotifyCharacteristicUuid;
            PackSize = options.PackSize > 0 ? options.PackSize : PackSize;
            SendIntervalMs = options.SendIntervalMs >= 0 ? options.SendIntervalMs : SendIntervalMs;
        }
        Log.Info($"【BleConnection】constructor() —— service={ServiceUuid}, packSize={PackSize}");
    }

    /// <summary>
    /// 发送数据（自动分片）。对应 JS <c>sendDataInBleMode(data)</c>。
    /// 当数据长度超过 <see cref="PackSize"/> 时，按 <see cref="PackSize"/> 分片逐包发送，
    /// 每包之间等待 <see cref="SendIntervalMs"/> 毫秒。
    /// </summary>
    public override async Task SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("BLE 设备未连接，无法发送数据。");

        var totalLen = data.Length;
        var packSize = PackSize;
        Log.Debug($"【BleConnection】SendAsync() —— total={totalLen}, packSize={packSize}");

        if (totalLen <= packSize)
        {
            await base.SendAsync(data, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 分片发送。对应 JS 中的 for 循环分包逻辑。
        PrintStatus = PrintStatus.Sending;
        try
        {
            var offset = 0;
            while (offset < totalLen)
            {
                var chunkLen = Math.Min(packSize, totalLen - offset);
                var chunk = data.Slice(offset, chunkLen);
                await Transport.SendAsync(chunk, cancellationToken).ConfigureAwait(false);
                offset += chunkLen;
                if (offset < totalLen && SendIntervalMs > 0)
                    await Task.Delay(SendIntervalMs, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            PrintStatus = PrintStatus.ReadyPrint;
        }
    }
}

/// <summary>
/// BLE 连接配置选项。对应 JS 中 <c>hi.constructor(options)</c> 的 <c>options</c> 参数。
/// </summary>
public sealed class BleConnectionOptions
{
    /// <summary>GATT 服务 UUID。默认使用 <see cref="BleConnection.DefaultServiceUuid"/>。</summary>
    public string? ServiceUuid { get; set; }

    /// <summary>写特征 UUID。默认使用 <see cref="BleConnection.DefaultWriteCharacteristicUuid"/>。</summary>
    public string? WriteCharacteristicUuid { get; set; }

    /// <summary>通知特征 UUID。默认使用 <see cref="BleConnection.DefaultNotifyCharacteristicUuid"/>。</summary>
    public string? NotifyCharacteristicUuid { get; set; }

    /// <summary>单包最大字节数。默认 <see cref="BleConnection.DefaultPackSize"/>（20）。</summary>
    public int PackSize { get; set; } = BleConnection.DefaultPackSize;

    /// <summary>分片发送间隔（毫秒）。默认 20ms。</summary>
    public int SendIntervalMs { get; set; } = 20;
}

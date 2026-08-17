// =====================================================================
//  本文件将 DzPrinter.Core.TransportInterfaces.cs 的内容复制到 DzPrinter.Transport 模块。
//  同时在 Core 中保留类型别名（通过 using static + Obsolete 转发），避免调用方大规模改动。
//
//  这是 8 模块架构重构的一部分：
//    Core       → 工具、枚举、日志、异常（不再包含传输接口）
//    Transport  → 传输接口 + DeviceConnection 基类
//    Transport.Ble / Transport.Hid → Windows 平台实现
//    Jobs       → DrawContext + DzPrinterManager
// =====================================================================

namespace DzPrinter.Transport;

/// <summary>
/// 传输层抽象接口。对应 JS SDK 中的 <c>Be</c>（BaseTransport）类与
/// <c>We</c>/<c>He</c>（WebBluetooth/WebHID）适配器。
/// <para>
/// 本接口定义了与打印机建立连接、收发数据的最小契约，
/// 由具体传输实现（BLE 低功耗蓝牙 / HID USB 等）实现。
/// </para>
/// </summary>
public interface IDeviceTransport
{
    /// <summary>当前连接状态。</summary>
    ConnectionState State { get; }

    /// <summary>已连接的设备信息（null 表示未连接）。</summary>
    DeviceInfo? ConnectedDevice { get; }

    /// <summary>
    /// 扫描/发现附近可用的打印机设备。对应 JS <c>discover()</c>。
    /// </summary>
    Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接到指定设备。对应 JS <c>connectDevice(t)</c>。
    /// </summary>
    Task ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开当前连接。对应 JS <c>disconnect()</c>。
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送原始字节数据到设备。对应 JS <c>sendData(t)</c>。
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送数据并等待响应。对应 JS <c>requestMessage()</c> 的请求-响应模式。
    /// </summary>
    /// <param name="timeoutMs">超时毫秒数，默认 2000。</param>
    /// <returns>
    /// 设备返回的原始字节数据（包含协议帧头 0x1F、CMD、EBV 长度、payload、CRC）。
    /// 调用方需使用 <c>EbvHelper.TryGetPayload</c> 剥离帧头提取 payload。超时返回 null。
    /// </returns>
    Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 当收到设备数据时触发。对应 JS <c>onDataReceived</c> 事件。
    /// </summary>
    event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// 当连接状态变化时触发。对应 JS <c>onConnectionStateChanged</c> 事件。
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
}

/// <summary>
/// 连接状态。对应 JS SDK 中 <c>We.mConnectMap</c>/<c>He.mConnectedMap</c> 的状态。
/// </summary>
public enum ConnectionState
{
    /// <summary>未连接。</summary>
    Disconnected = 0,
    /// <summary>正在连接。</summary>
    Connecting = 1,
    /// <summary>已连接。</summary>
    Connected = 2,
    /// <summary>正在断开。</summary>
    Disconnecting = 3,
    /// <summary>连接失败。</summary>
    Failed = 4,
}

/// <summary>
/// 传输类型。对应 JS SDK 中 <c>We</c>/<c>He</c>/<c>Qe</c> 等适配器类型。
/// </summary>
public enum TransportType
{
    /// <summary>未知。</summary>
    Unknown = 0,
    /// <summary>低功耗蓝牙（BLE）。对应 JS <c>We</c>（WebBluetooth）。</summary>
    BluetoothLowEnergy = 1,
    /// <summary>HID USB。对应 JS <c>He</c>（WebHID）。</summary>
    HidUsb = 2,
    /// <summary>经典蓝牙（SPP）。</summary>
    BluetoothClassic = 3,
    /// <summary>TCP/IP 网络打印。</summary>
    TcpIp = 4,
    /// <summary>文件输出虚拟传输（用于调试/测试，将打印数据写入文件）。</summary>
    File = 98,
    /// <summary>模拟/测试传输。</summary>
    Mock = 99,
}

/// <summary>
/// 设备信息。对应 JS SDK 中 <c>Fe</c>（Device）类的关键字段。
/// </summary>
public sealed class DeviceInfo
{
    /// <summary>设备唯一 ID（如 BLE deviceId 或 HID device path）。</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>设备名称。</summary>
    public string DeviceName { get; set; } = string.Empty;
    /// <summary>传输类型。</summary>
    public TransportType TransportType { get; set; }
    /// <summary>打印机能力标志（从设备握手获取）。</summary>
    public uint HardwareFlags { get; set; }
    /// <summary>软件能力标志（从设备握手获取）。</summary>
    public uint SoftwareFlags { get; set; }
    /// <summary>打印机缓冲区大小（字节）。</summary>
    public int BufferSize { get; set; }
    /// <summary>打印机 DPI。</summary>
    public int Dpi { get; set; }
    /// <summary>打印机像素宽度。</summary>
    public int PrinterWidth { get; set; }
    /// <summary>客户端类型。</summary>
    public int ClientType { get; set; }
    /// <summary>原始设备对象（由传输层提供，供上层使用）。</summary>
    public object? NativeDevice { get; set; }

    public override string ToString() =>
        $"Device[{DeviceName}]({TransportType}, id={DeviceId})";
}

/// <summary>
/// 数据接收事件参数。对应 JS <c>onDataReceive(t)</c>。
/// </summary>
public sealed class DataReceivedEventArgs : EventArgs
{
    public DataReceivedEventArgs(byte[] data) { Data = data; }
    /// <summary>接收到的数据。</summary>
    public byte[] Data { get; }
}

/// <summary>
/// 连接状态变化事件参数。对应 JS <c>EPrintStatus</c> 变化通知。
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStateChangedEventArgs(ConnectionState state, string? message = null)
    {
        State = state;
        Message = message;
    }
    /// <summary>新的连接状态。</summary>
    public ConnectionState State { get; }
    /// <summary>可选的描述信息（如错误原因）。</summary>
    public string? Message { get; }
}

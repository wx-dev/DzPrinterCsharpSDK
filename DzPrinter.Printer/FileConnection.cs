using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  FileConnection（文件输出虚拟连接）。
//  对应文件传输层（FileTransport）的设备连接实现。
//  文件传输无需分片、无需 GATT 服务，完整功能下沉到 FileTransport。
//  本类仅提供文件设备类型标识和特有的配置项。
// =====================================================================

/// <summary>
/// 文件输出虚拟连接。包装 <see cref="IDeviceTransport"/> 的文件传输实现。
/// </summary>
/// <remarks>
/// <para>与 BLE/HID 不同，FileConnection 不涉及真实设备通信，
/// 所有数据写入本地文件。适合调试、测试、脱机分析等场景。</para>
/// </remarks>
public sealed class FileConnection : DeviceConnection
{
    /// <summary>
    /// 默认虚拟设备 ID。
    /// </summary>
    public const string DefaultDeviceId = "virtual-file-printer";

    /// <summary>
    /// 默认虚拟设备名称（以 D60 开头，匹配 SupportPrinterMatcher）。
    /// </summary>
    public const string DefaultDeviceName = "D60-File";

    /// <inheritdoc />
    public override LpaDeviceType DeviceType => LpaDeviceType.File;

    /// <summary>
    /// 构造文件输出虚拟连接。
    /// </summary>
    /// <param name="transport">文件传输层实现（通常是 FileTransport）。</param>
    public FileConnection(IDeviceTransport transport) : base(transport)
    {
        Log.Info("【FileConnection】constructor() —— 文件输出虚拟传输已就绪");
    }
}

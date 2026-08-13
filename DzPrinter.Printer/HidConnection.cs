using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  HidConnection（HID USB 连接）。
//  对应 JS SDK 中 HID 适配器（<c>He</c>）相关连接逻辑。
//  JS 中 <c>He</c> 使用 WebHID API 操作 USB HID 设备。
//  C# 中将 HID 操作抽象为 <see cref="IDeviceTransport"/>，
//  本类仅提供 HID 特有的配置与设备类型标识。
// =====================================================================

/// <summary>
/// HID USB 连接。对应 JS SDK中的 HID 适配器逻辑。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>He</c>（WebHID）通过浏览器 WebHID API 操作设备；
/// C# 中将 HID 操作下沉到 <see cref="IDeviceTransport"/>。</para>
/// <para>HID 设备无需分片，单次可发送整个报告。无需 GATT 服务/特征。</para>
/// </remarks>
public sealed class HidConnection : DeviceConnection
{
    /// <summary>德佟打印机默认 HID Vendor ID。</summary>
    public const ushort DefaultVendorId = 0x0483;

    /// <summary>德佟打印机默认 HID Product ID（通配，0 表示不限制）。</summary>
    public const ushort DefaultProductId = 0;

    /// <summary>HID Vendor ID。</summary>
    public ushort VendorId { get; set; } = DefaultVendorId;

    /// <summary>HID Product ID（0 表示不限制）。</summary>
    public ushort ProductId { get; set; } = DefaultProductId;

    /// <summary>HID Usage Page（0 表示不限制）。</summary>
    public ushort UsagePage { get; set; }

    /// <inheritdoc />
    public override LpaDeviceType DeviceType => LpaDeviceType.UsbHid;

    /// <summary>
    /// 构造 HID 连接。
    /// </summary>
    /// <param name="transport">HID 传输层实现。</param>
    /// <param name="options">可选配置。</param>
    public HidConnection(IDeviceTransport transport, HidConnectionOptions? options = null)
        : base(transport)
    {
        if (options != null)
        {
            VendorId = options.VendorId;
            ProductId = options.ProductId;
            UsagePage = options.UsagePage;
        }
        Log.Info($"【HidConnection】constructor() —— vendor=0x{VendorId:X4}, product=0x{ProductId:X4}");
    }
}

/// <summary>
/// HID 连接配置选项。
/// </summary>
public sealed class HidConnectionOptions
{
    /// <summary>HID Vendor ID。默认 <see cref="HidConnection.DefaultVendorId"/>。</summary>
    public ushort VendorId { get; set; } = HidConnection.DefaultVendorId;

    /// <summary>HID Product ID。0 表示不限制。</summary>
    public ushort ProductId { get; set; } = HidConnection.DefaultProductId;

    /// <summary>HID Usage Page。0 表示不限制。</summary>
    public ushort UsagePage { get; set; }
}

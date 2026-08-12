namespace DzPrinter.Printer;

/// <summary>
/// 表示一个被发现的打印机设备。对应 JS SDK 中设备扫描结果项。
/// </summary>
public sealed class PrinterDevice
{
    /// <summary>设备唯一标识（如 BLE deviceId 或 HID device path）。</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>设备名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>从设备名称中提取的机型名称（参见 <see cref="LpaUtils.GetModelName"/>）。</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>信号强度（RSSI，单位 dBm）。</summary>
    public int Rssi { get; set; }

    /// <summary>设备类型。</summary>
    public LpaDeviceType DeviceType { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"PrinterDevice[{Name}]({DeviceType}, id={DeviceId}, rssi={Rssi})";
}

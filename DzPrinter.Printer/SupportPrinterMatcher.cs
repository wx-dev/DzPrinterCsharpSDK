namespace DzPrinter.Printer;

// =====================================================================
//  SupportPrinterMatcher（支持的打印机匹配器）。
//  对应 JS SDK 中 <c>fi</c> 类。
//  JS 中 <c>fi</c> 用于过滤扫描到的蓝牙设备，仅保留德佟打印机。
//  匹配规则：设备名匹配预定义的机型前缀列表（如 D60、D110、P2 等）。
// =====================================================================

/// <summary>
/// 支持的打印机型号匹配器。对应 JS SDK 中的 <c>fi</c>（SupportPrinterMatcher）类。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS 中 <c>fi</c> 维护一个支持的机型名称前缀列表，
/// 在扫描回调中过滤掉不匹配的设备。</para>
/// <para><b>匹配策略</b>：设备名（忽略大小写）以列表中任一前缀开头即视为匹配。
/// 列表可通过 <see cref="SupportedPrefixes"/> 静态属性扩展。</para>
/// </remarks>
public static class SupportPrinterMatcher
{
    /// <summary>
    /// 支持的打印机机型名称前缀列表。对应 JS <c>fi.SUPPORTED_PRINTERS</c>。
    /// 列表内容与 JS 完全一致，运行期可扩展。
    /// </summary>
    public static readonly HashSet<string> SupportedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // 德佟打印机机型前缀。JS: fi.SUPPORTED_PRINTERS
        "D30", "D35", "D50", "D60", "D80", "D110", "D101",
        "P2", "P8", "P18", "P50", "P60",
        "T8", "M8",
        "N1", "N2",
        "A20", "A30", "A50", "A60", "A80", "A100", "A200", "A300",
        "B1", "B3", "B6", "B18", "B50", "B80",
        "G310", "G318",
        "S6", "S8",
        "K3", "K5", "K8", "K30",
        "DT-"
    };

    /// <summary>
    /// 判断设备名是否为支持的打印机。对应 JS <c>fi.isSupported(name)</c>。
    /// </summary>
    /// <param name="name">设备名称。</param>
    /// <returns>匹配返回 true。</returns>
    public static bool IsSupported(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var prefix in SupportedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 从设备列表中筛选支持的打印机。对应 JS <c>fi.filterSupported(devices)</c>。
    /// </summary>
    public static IReadOnlyList<PrinterDevice> FilterSupported(
        IEnumerable<PrinterDevice> devices)
    {
        var result = new List<PrinterDevice>();
        foreach (var d in devices)
        {
            if (IsSupported(d.Name)) result.Add(d);
        }
        return result;
    }

    /// <summary>
    /// 尝试从设备名中提取机型名称。对应 JS <c>fi.getModelName(name)</c>。
    /// 委托 <see cref="LpaUtils.GetModelName"/> 实现。
    /// </summary>
    public static string GetModelName(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : LpaUtils.GetModelName(name);
}

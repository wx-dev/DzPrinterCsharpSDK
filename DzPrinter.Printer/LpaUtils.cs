namespace DzPrinter.Printer;

/// <summary>
/// 打印机通用工具方法集合。对应 JS SDK 中的 <c>LPAUtils</c>（minified: <c>Le</c>）类的部分静态方法。
/// 提供像素/毫米换算与机型信息解析等辅助函数。
/// </summary>
public static class LpaUtils
{
    /// <summary>
    /// 将像素值转换为毫米。对应 JS <c>pix2mm</c>。
    /// 公式：<c>25.4 * pixels / dpi</c>。
    /// </summary>
    /// <param name="pixels">像素数量。</param>
    /// <param name="dpi">DPI（每英寸点数）。</param>
    /// <returns>对应的毫米数。</returns>
    public static double Pix2MM(double pixels, double dpi) => 25.4 * pixels / dpi;

    /// <summary>
    /// 将毫米值转换为像素。对应 JS <c>mm2pix</c>。
    /// 公式：<c>mm * dpi / 25.4</c>。
    /// </summary>
    /// <param name="mm">毫米数。</param>
    /// <param name="dpi">DPI（每英寸点数）。</param>
    /// <returns>对应的像素数量。</returns>
    public static double MM2Pix(double mm, double dpi) => mm * dpi / 25.4;

    /// <summary>
    /// 从设备名称中提取机型名称。对应 JS <c>getModelName</c> 的简化版：
    /// 查找最后一个 "-"，返回其后的子串；若无 "-" 则返回完整名称。
    /// </summary>
    /// <param name="name">设备名称。</param>
    /// <returns>机型名称。</returns>
    public static string GetModelName(string name)
    {
        var idx = name.LastIndexOf('-');
        return idx >= 0 ? name.Substring(idx + 1) : name;
    }

    /// <summary>
    /// 根据机型名称获取设备 DPI。当前所有机型默认返回 203。
    /// </summary>
    /// <param name="modelName">机型名称（保留供后续按机型细化）。</param>
    /// <returns>设备 DPI（默认 203）。</returns>
    public static int GetDeviceDPI(string modelName) => 203;
}

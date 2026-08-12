using System.Text;

namespace DzPrinter.Printer;

/// <summary>
/// GBK 编码/解码工具。对应 JS SDK 中的 <c>GBKUtils</c>（minified: <c>Me</c>）类。
/// 基于 <c>System.Text.Encoding.CodePages</c> 提供的 GBK 代码页实现。
/// </summary>
public static class GbkUtils
{
    /// <summary>
    /// GBK 编码实例。在静态构造函数中注册代码页提供程序后获取，
    /// 确保跨平台可用。
    /// </summary>
    private static readonly Encoding GbkEncoding;

    static GbkUtils()
    {
        // 注册代码页提供程序，使 GBK 等区域编码在所有平台上可用。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GbkEncoding = Encoding.GetEncoding("GBK");
    }

    /// <summary>
    /// 将字符串按 GBK 编码为字节数组。对应 JS <c>GBKUtils.encode</c>。
    /// </summary>
    /// <param name="text">要编码的字符串。</param>
    /// <returns>GBK 编码后的字节数组。</returns>
    public static byte[] Encode(string text) => GbkEncoding.GetBytes(text);

    /// <summary>
    /// 将 GBK 字节数组解码为字符串。对应 JS <c>GBKUtils.decode</c>。
    /// </summary>
    /// <param name="bytes">GBK 编码的字节数组。</param>
    /// <returns>解码后的字符串。</returns>
    public static string Decode(byte[] bytes) => GbkEncoding.GetString(bytes);
}

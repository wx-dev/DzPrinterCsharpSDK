using System.Text;

namespace DzPrinter.Printer;

/// <summary>
/// 文本解码器。对应 JS SDK 中的 <c>DzTextDecoder</c> 类。
/// 将字节数组按指定编码（utf-8/gbk/ascii）解码为字符串。
/// </summary>
public static class DzTextDecoder
{
    /// <summary>
    /// 将字节数组按指定编码解码为字符串。对应 JS <c>DzTextDecoder.decode</c>。
    /// </summary>
    /// <param name="bytes">要解码的字节数组。</param>
    /// <param name="encoding">
    /// 编码名称，支持 "utf-8"、"gbk"、"ascii"；默认 "utf-8"。
    /// 未知编码回退为 utf-8。
    /// </param>
    /// <returns>解码后的字符串。</returns>
    public static string Decode(byte[] bytes, string encoding = "utf-8") =>
        NormalizeEncoding(encoding) switch
        {
            "gbk" => GbkUtils.Decode(bytes),
            "ascii" => Encoding.ASCII.GetString(bytes),
            _ => Encoding.UTF8.GetString(bytes)
        };

    /// <summary>
    /// 规范化编码名称：转小写并去除分隔符，便于匹配。
    /// </summary>
    private static string NormalizeEncoding(string encoding) =>
        (encoding ?? string.Empty).ToLowerInvariant().Replace("-", "");
}

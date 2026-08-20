using System.Text;

namespace DzPrinter.Core;

/// <summary>
/// 文本编码器。对应 JS SDK 中的 <c>DzTextEncoder</c> 类。
/// 将字符串按指定编码（utf-8/gbk/ascii）编码为字节数组。
/// </summary>
public static class DzTextEncoder
{
    /// <summary>
    /// 将字符串按指定编码编码为字节数组。对应 JS <c>DzTextEncoder.encode</c>。
    /// </summary>
    /// <param name="text">要编码的字符串。</param>
    /// <param name="encoding">
    /// 编码名称，支持 "utf-8"、"gbk"、"ascii"；默认 "utf-8"。
    /// 未知编码回退为 utf-8。
    /// </param>
    /// <returns>编码后的字节数组。</returns>
    public static byte[] Encode(string text, string encoding = "utf-8") =>
        GbkUtils.NormalizeEncoding(encoding) switch
        {
            "gbk" => GbkUtils.Encode(text),
            "ascii" => Encoding.ASCII.GetBytes(text),
            _ => Encoding.UTF8.GetBytes(text)
        };
}

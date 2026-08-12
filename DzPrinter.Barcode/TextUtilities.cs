using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// 字符与字符串工具方法。对应 JS SDK 中 <c>g</c> 类。
/// </summary>
internal static class CharUtils
{
    /// <summary>
    /// 判断字符或码点是否为数字。对应 JS <c>g.isDigit(t)</c>。
    /// </summary>
    public static bool IsDigit(int code) => code >= CharConstants.Num0 && code <= CharConstants.Num9;

    /// <summary>判断字符是否为数字。对应 JS <c>g.isDigit(t)</c> 重载。</summary>
    public static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

    /// <summary>
    /// 判断子串是否全部由数字组成。对应 JS <c>g.isDigits(t, e, i)</c>。
    /// </summary>
    public static bool IsDigits(string text, int start, int? length)
    {
        var s = start <= 0 ? 0 : start;
        var end = length.HasValue ? s + length.Value : text.Length;
        if (end > text.Length) return false;
        for (var i = s; i < end; i++)
            if (text[i] < '0' || text[i] > '9') return false;
        return true;
    }

    /// <summary>判断字符码点是否为大写字母。对应 JS <c>g.isUpper(t)</c>。</summary>
    public static bool IsUpper(int code) => code >= CharConstants.UpperA && code <= CharConstants.UpperZ;

    /// <summary>判断字符码点是否为小写字母。对应 JS <c>g.isLower(t)</c>。</summary>
    public static bool IsLower(int code) => code >= CharConstants.LowerA && code <= CharConstants.LowerZ;

    /// <summary>
    /// 字符/码点 → 0..35 数值（0-9 → 0-9, A-Z → 10-35, a-z → 10-35），其他返回 -1。
    /// 对应 JS <c>g.ctoi(t)</c>。
    /// </summary>
    public static int Ctoi(int code)
    {
        if (code >= CharConstants.Num0 && code <= CharConstants.Num9) return code - CharConstants.Num0;
        if (code >= CharConstants.UpperA && code <= CharConstants.UpperZ) return code - CharConstants.UpperA + 10;
        if (code >= CharConstants.LowerA && code <= CharConstants.LowerZ) return code - CharConstants.LowerA + 10;
        return -1;
    }

    /// <summary>
    /// 0..35 数值 → 字符（0-9 → '0'-'9', 10-35 → 'A'-'Z'）。
    /// 对应 JS <c>g.itoc(t)</c>。
    /// </summary>
    public static string Itoc(int value) =>
        value >= 0 && value <= 9 ? value.ToString() : ((char)(value - 10 + CharConstants.UpperA)).ToString();

    /// <summary>
    /// 重复字符。对应 JS <c>g.repeatChar(t, e)</c>。
    /// </summary>
    public static string RepeatChar(char ch, int count)
    {
        if (count <= 0) return string.Empty;
        return new string(ch, count);
    }

    /// <summary>
    /// 前置填充字符到指定长度。对应 JS <c>g.preFillChar(t, e, i)</c>。
    /// </summary>
    public static string PreFillChar(string text, int targetLength, char padChar) =>
        text.Length < targetLength ? new string(padChar, targetLength - text.Length) + text : text;
}

/// <summary>
/// 文本编码工具。对应 JS SDK 中 <c>p</c> 类。
/// 提供 UTF-8/ISO-8859-1/Unicode 字节序列转换。
/// </summary>
internal static class TextEncodingUtils
{
    /// <summary>
    /// 判断字符串是否全部为 ISO-8859-1 字符。对应 JS <c>p.isISO_8859_1(t)</c>。
    /// </summary>
    public static bool IsIso8859_1(string? text)
    {
        if (text == null) return false;
        foreach (var ch in text) if (ch > 255) return false;
        return true;
    }

    /// <summary>
    /// 字符串 → UTF-8 字节数组。对应 JS <c>p.getBytes_Utf8(t)</c>。
    /// JS 优先使用 <c>TextEncoder</c>，浏览器中不存在时回退到自实现 <c>encodeUtf8</c>。
    /// .NET 直接使用 <see cref="Encoding.UTF8"/>。
    /// </summary>
    public static byte[] GetBytesUtf8(string text) => Encoding.UTF8.GetBytes(text ?? string.Empty);

    /// <summary>
    /// 字符串 → ISO-8859-1 字节数组。对应 JS <c>p.getBytes_ISO8859_1(t)</c>。
    /// JS 实现使用 <c>unescape(encodeURIComponent(t))</c> 截取低 8 位。
    /// </summary>
    public static byte[] GetBytesIso8859_1(string text)
    {
        text = text ?? string.Empty;
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++) bytes[i] = (byte)(text[i] & 0xFF);
        return bytes;
    }

    /// <summary>
    /// 字符串 → UTF-16LE 字节数组（每字符两字节）。对应 JS <c>p.getBytes_Unicode(t)</c>。
    /// JS 实现返回每字符 charCodeAt 的低 8 位（即 Latin1 字节），这里与 JS 一致。
    /// </summary>
    public static byte[] GetBytesUnicode(string text)
    {
        text = text ?? string.Empty;
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++) bytes[i] = (byte)(text[i] & 0xFF);
        return bytes;
    }

    /// <summary>
    /// 按是否使用 UTF-8 编码获取字节。对应 JS <c>p.getBytes(t, e)</c>。
    /// </summary>
    public static byte[] GetBytes(string text, bool useUtf8) =>
        useUtf8 ? GetBytesUtf8(text) : GetBytesUnicode(text);

    /// <summary>
    /// 判断字符串或码点序列中是否包含 ≥ 128 的字符。对应 JS <c>p.hasBase256Chars(t)</c>。
    /// </summary>
    public static bool HasBase256Chars(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text) if (ch >= 128) return true;
        return false;
    }

    /// <summary>
    /// 自实现 UTF-8 编码。对应 JS <c>p.encodeUtf8(t)</c>。
    /// 保留备用；与 .NET <see cref="Encoding.UTF8"/> 等价。代理对正确处理。
    /// </summary>
    public static byte[] EncodeUtf8(string text)
    {
        text = text ?? string.Empty;
        var bytes = new List<byte>(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var code = (int)text[i];
            // 代理对：高代理 D800–DBFF 后跟低代理 DC00–DFFF
            if (code >= 0xD800 && code <= 0xDBFF && i + 1 < text.Length)
            {
                var lo = (int)text[i + 1];
                if (lo >= 0xDC00 && lo <= 0xDFFF)
                {
                    code = 0x10000 + ((code - 0xD800) << 10) + (lo - 0xDC00);
                    i++;
                }
            }
            if (code < 0x80)
            {
                bytes.Add((byte)code);
            }
            else if (code < 0x800)
            {
                bytes.Add((byte)((code >> 6) | 0xC0));
                bytes.Add((byte)((code & 0x3F) | 0x80));
            }
            else if (code < 0x10000)
            {
                // JS: 排除 0xD800-0xDFFF 代理范围；其他 BMP 字符按 3 字节编码
                if (code >= 0xD800 && code <= 0xDFFF)
                {
                    // JS 在该分支不会进入（前面已合并代理对），保留以防万一：编码为替换字符
                    bytes.Add(0xEF); bytes.Add(0xBF); bytes.Add(0xBD);
                }
                else
                {
                    bytes.Add((byte)(0xE0 | (code >> 12)));
                    bytes.Add((byte)(0x80 | ((code >> 6) & 0x3F)));
                    bytes.Add((byte)(0x80 | (code & 0x3F)));
                }
            }
            else if (code <= 0x10FFFF)
            {
                bytes.Add((byte)(0xF0 | (code >> 18)));
                bytes.Add((byte)(0x80 | ((code >> 12) & 0x3F)));
                bytes.Add((byte)(0x80 | ((code >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (code & 0x3F)));
            }
            else
            {
                bytes.Add(0xEF); bytes.Add(0xBF); bytes.Add(0xBD);
            }
        }
        return bytes.ToArray();
    }
}

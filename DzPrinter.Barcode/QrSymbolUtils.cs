namespace DzPrinter.Barcode;

/// <summary>
/// QR 码符号工具。对应 JS SDK 中 <c>wt</c> 类。
/// 提供版本/尺寸/BCH/水印种子等通用计算。
/// </summary>
internal static class QrSymbolUtils
{
    /// <summary>
    /// DT 水印校验和基值表。对应 JS <c>wt.sDtWaterMarkCheckSums = [197, 257, 571, 991]</c>。
    /// </summary>
    public static readonly int[] SDtWaterMarkCheckSums = { 197, 257, 571, 991 };

    /// <summary>
    /// SJIS 转换钩子。对应 JS <c>wt.toSJISFunction</c>。
    /// 在 JS SDK 中默认未设置（fallback 走 <c>charCodeAt</c>）；C# 同样默认为 null。
    /// </summary>
    public static Func<char, int>? ToSjisFunction { get; set; }

    /// <summary>
    /// 获取 QR 码符号尺寸（边长）。对应 JS <c>wt.getSymbolSize(t)</c>。
    /// 公式：<c>4 * version + 17</c>。
    /// </summary>
    public static int GetSymbolSize(int version)
    {
        if (version == 0) throw new ArgumentException("\"version\" cannot be null or undefined", nameof(version));
        if (version < 1 || version > 40) throw new ArgumentException("\"version\" should be in range from 1 to 40", nameof(version));
        return 4 * version + 17;
    }

    /// <summary>
    /// 获取指定版本 QR 码的码字总数。对应 JS <c>wt.getSymbolTotalCodewords(t)</c>。
    /// </summary>
    public static int GetSymbolTotalCodewords(int version) => QrTables.TotalCodewords[version];

    /// <summary>
    /// 计算一个数的 BCH 位数（最高位位置）。对应 JS <c>wt.getBCHDigit(t)</c>。
    /// 例如 <c>getBCHDigit(1335)</c> = 11。
    /// </summary>
    public static int GetBchDigit(int value)
    {
        var digits = 0;
        var x = value;
        while (x != 0)
        {
            digits++;
            x = (int)((uint)x >> 1);  // JS >>>= 1 无符号右移
        }
        return digits;
    }

    /// <summary>
    /// 计算水印种子。对应 JS <c>wt.calcWaterMarkSeed(t)</c>。
    /// 数字直接返回；字符串通过 UTF-8 字节累加哈希（初值 4660 = 0x1234）；
    /// 空字符串返回 1024；其他类型返回 0。
    /// </summary>
    public static int CalcWaterMarkSeed(object? seed)
    {
        if (seed is int or long or short or byte or uint or ulong or ushort or sbyte)
            return Convert.ToInt32(seed);

        if (seed is string s)
        {
            if (string.IsNullOrEmpty(s)) return 1024;
            var e = 4660;
            var bytes = TextEncodingUtils.GetBytesUtf8(s);
            for (var i = 0; i < bytes.Length; i++)
            {
                e += e >>> 5;
                e += (bytes[i] & 0xFF) * ((i & 2) != 0 ? 5 : 3);
                e += (i & 1) != 0 ? 13 : 11;
            }
            return 1025 + (e & 1048575);
        }
        return 0;
    }

    /// <summary>
    /// 检查给定字节数组是否含有匹配的 DT 水印。对应 JS <c>wt.hasWaterMarkSeed(t, e, i)</c>。
    /// t=seed, e=bytes, i=prefix。
    /// </summary>
    public static bool HasWaterMarkSeed(object? seed, byte[]? bytes, string? prefix)
    {
        if (bytes == null || bytes.Length < 3) return false;

        int s, n;
        if ((bytes[0] & 0xFF) >> 4 == 5)
        {
            if ((bytes[0] & 0x0F) == 9)
            {
                if (bytes.Length < 4) return false;
                if ((bytes[1] & 0xFF) >> 4 != 3) return false;
                if ((bytes[1] & 0x0F) != 13) return false;
                s = (bytes[2] & 0xFF) >> 6 & 3;
                n = (bytes[2] & 0x3F) << 4 | (bytes[3] & 0xFF) >> 4;
            }
            else
            {
                if ((bytes[0] & 0x0F) != 3) return false;
                if ((bytes[1] & 0xFF) >> 4 != 13) return false;
                s = (bytes[1] & 0xFF) >> 2 & 3;
                n = (bytes[1] & 0x03) << 8 | (bytes[2] & 0xFF);
            }
        }
        else
        {
            if ((bytes[0] & 0xFF) >> 4 != 3) return false;
            if ((bytes[0] & 0x0F) != 13) return false;
            s = (bytes[1] & 0xFF) >> 6 & 3;
            n = (bytes[1] & 0x3F) << 4 | (bytes[2] & 0xFF) >> 4;
        }
        return n == 1022 || n == CalcDtCheckSum(s, prefix, seed);
    }

    /// <summary>
    /// 计算 DT 校验和。对应 JS <c>wt.calcDtCheckSum(t, e, i)</c>。
    /// t=type(0-3), e=seed(stringOrNumber), i=prefix。
    /// </summary>
    public static int CalcDtCheckSum(int type, object? seed, object? prefix)
    {
        var e = seed is string ? CalcWaterMarkSeed(seed) : (seed is int n ? n : 0);
        var s = SDtWaterMarkCheckSums[type & 3];
        s = (s + e) & 1048575;
        if (prefix is string ps && ps.Length > 0)
        {
            var bytes = TextEncodingUtils.GetBytesUtf8(ps);
            for (var i = 0; i < bytes.Length; i++)
            {
                s += s >>> 5;
                s += (bytes[i] & 0xFF) * ((i & 2) != 0 ? 5 : 3);
                s += (i & 1) != 0 ? 13 : 11;
            }
        }
        return s % 1019 + 3;
    }
}

/// <summary>
/// BCH 位数常量。对应 JS <c>const Mt = wt.getBCHDigit(1335)</c>。
/// 1335 = 0b10100110111，共 11 位。
/// </summary>
internal static class BchDigitConstants
{
    /// <summary>1335（格式信息 BCH 多项式）的位数。对应 JS <c>Mt</c>。</summary>
    public const int FormatPoly = 11;

    /// <summary>7973（版本信息 BCH 多项式）的位数。对应 JS <c>const ae = wt.getBCHDigit(7973)</c>。7973 = 0b1111100100101，共 13 位。</summary>
    public const int VersionPoly = 13;
}

namespace DzPrinter.Core;

/// <summary>
/// 通用工具方法集合。对应 JS SDK 中 <c>Ie</c> 类（工具方法）与 <c>be</c> 类的
/// 静态辅助函数。供各模块共享使用。
/// </summary>
public static class ByteUtils
{
    /// <summary>
    /// 将字节数组转为十六进制字符串（每字节两位，空格分隔）。
    /// 对应 JS <c>Ie.arrayBufferToHex16(t)</c>。
    /// </summary>
    public static string ToHexString(ReadOnlySpan<byte> data, bool spaceSeparated = true)
    {
        if (data.Length == 0) return string.Empty;
        var result = new char[data.Length * (spaceSeparated ? 3 : 2)];
        int j = 0;
        for (int i = 0; i < data.Length; i++)
        {
            result[j++] = "0123456789ABCDEF"[data[i] >> 4];
            result[j++] = "0123456789ABCDEF"[data[i] & 0x0F];
            if (spaceSeparated && i < data.Length - 1) result[j++] = ' ';
        }
        return new string(result, 0, j);
    }

    /// <summary>
    /// 将十六进制字符串解析为字节数组。对应 JS <c>Ie.hexToBytes(t)</c>。
    /// </summary>
    public static byte[]? FromHexString(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var cleaned = hex.Replace(" ", "").Replace("-", "").Replace(",", "").Replace("0x", "").Replace("0X", "");
        if (cleaned.Length % 2 != 0) return null;
        var result = new byte[cleaned.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            if (!byte.TryParse(cleaned.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                return null;
            result[i] = b;
        }
        return result;
    }

}

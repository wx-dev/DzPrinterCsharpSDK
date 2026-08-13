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

    /// <summary>
    /// 按小端序将 4 个字节组装为 32 位整数（低字节在前）。对应 JS <c>be.toNumber(b0,b1,b2,b3)</c>。
    /// </summary>
    public static int ToNumber(int b0 = 0, int b1 = 0, int b2 = 0, int b3 = 0) =>
        (b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0xFF) << 16) | ((b3 & 0xFF) << 24);

    /// <summary>
    /// 计算 CRC：对 buffer[start..end) 逐字节累加，取反截低 8 位。
    /// 对应 JS <c>be.calcCRC(t, e, i)</c>。
    /// </summary>
    public static byte CalcCrc(ReadOnlySpan<byte> buffer, int start, int end)
    {
        var sum = 0;
        for (var i = start; i < end; i++) sum += buffer[i];
        return (byte)(~sum & 0xFF);
    }

    /// <summary>
    /// EBV 编码阈值（与 JS 一致）：值 ≥ 192 用双字节编码。
    /// </summary>
    public const int EbvThreshold = 192;

    /// <summary>
    /// EBV 编码最大值（14 位）。
    /// </summary>
    public const int MaxEbvValue = 16383;

    /// <summary>
    /// 计算 EBV 值编码后的字节数（1 或 2）。
    /// </summary>
    public static int GetEbvByteCount(int value) => value < EbvThreshold ? 1 : 2;

    /// <summary>
    /// 将 EBV 值编码为字节数组。对应 JS <c>be.fromEBV(t)</c>。
    /// </summary>
    public static byte[] FromEbv(int value) => value >= EbvThreshold
        ? new byte[] { (byte)((value >> 8) | 0xC0), (byte)(value & 0xFF) }
        : new byte[] { (byte)value };

    /// <summary>
    /// 从 EBV 字节对还原整数值。对应 JS <c>be.toEBV(low, high)</c>。
    /// </summary>
    public static int ToEbv(int low, int high) =>
        high != 0 && high >= EbvThreshold
            ? ToNumber(low, high & 0x3F)
            : low & 0xFF;

    /// <summary>
    /// 将 16 位整数编码为字节数组。对应 JS <c>be.getBytesFromShort(t, asEbv)</c>。
    /// </summary>
    public static byte[] GetBytesFromShort(int value, bool asEbv)
    {
        if (asEbv) return FromEbv(value);
        return [(byte)(value >> 8), (byte)value];
    }

    /// <summary>
    /// 将 32 位整数按大端序编码为 4 字节数组。对应 JS <c>be.getBytesFromInt32(t)</c>。
    /// </summary>
    public static byte[] GetBytesFromInt32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>
    /// 按大端序编码整数为指定长度的字节数组。对应 JS <c>be.getBytesFromNumber(t, byteCount)</c>。
    /// </summary>
    public static byte[] GetBytesFromNumber(int value, int byteCount)
    {
        var bytes = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
            bytes[i] = (byte)(value >> (8 * (byteCount - 1 - i)) & 0xFF);
        return bytes;
    }

    /// <summary>
    /// 计算数据长度为 dataLength 时协议帧的总字节数。
    /// JS: <c>be.getPackBytes(t) = t + (t >= 192 ? 5 : 4)</c>。
    /// </summary>
    public static int GetPackBytes(int dataLength) =>
        dataLength + (dataLength >= EbvThreshold ? 5 : 4);
}

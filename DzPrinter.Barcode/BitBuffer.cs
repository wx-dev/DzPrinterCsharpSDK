namespace DzPrinter.Barcode;

/// <summary>
/// 位缓冲区。对应 JS SDK 中 <c>Ot</c> 类。
/// 用于 QR 码数据编码阶段按位写入。
/// </summary>
internal sealed class BitBuffer
{
    private readonly List<byte> _buffer = new();
    private int _length;

    /// <summary>底层字节列表。对应 JS <c>Ot.buffer</c> getter。</summary>
    public List<byte> Buffer => _buffer;

    /// <summary>已写入的位总数。对应 JS <c>Ot.length</c> getter。</summary>
    public int Length => _length;

    /// <summary>
    /// 读取第 t 位的值。对应 JS <c>Ot.get(t)</c>。
    /// </summary>
    public bool Get(int bitIndex)
    {
        var byteIndex = bitIndex / 8;
        return ((_buffer[byteIndex] >> (7 - bitIndex % 8)) & 1) == 1;
    }

    /// <summary>
    /// 写入 value 的低 bitCount 位（高位先行）。对应 JS <c>Ot.put(t, e)</c>。
    /// </summary>
    public void Put(int value, int bitCount)
    {
        for (var i = 0; i < bitCount; i++)
        {
            var bit = (value >> (bitCount - i - 1)) & 1;
            PutBit(bit == 1);
        }
    }

    /// <summary>
    /// 写入单个布尔位。对应 JS <c>Ot.putBit(t)</c>。
    /// </summary>
    public void PutBit(bool bit)
    {
        var byteIndex = _length / 8;
        if (_buffer.Count <= byteIndex) _buffer.Add(0);
        if (bit) _buffer[byteIndex] |= (byte)(0x80 >> (_length % 8));
        _length++;
    }

    /// <summary>已写入位总数。对应 JS <c>Ot.getLengthInBits()</c>。</summary>
    public int GetLengthInBits() => _length;
}

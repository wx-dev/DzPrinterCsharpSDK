namespace DzPrinter.Printer;

/// <summary>
/// 单个发送缓冲区：对应 JS SDK 中的 <c>ve</c> 类。
/// 内部维护一段定长 <c>byte[]</c>，按序写入协议帧或原始字节。
/// </summary>
/// <remarks>
/// JS <c>ve</c> 的核心职责：
/// <list type="bullet">
///   <item><see cref="PushPackage"/>：把 CMD+data 序列化为完整协议帧后写入</item>
///   <item><see cref="Push(ReadOnlySpan{byte})"/>：写入原始字节（如 ESC/J 走纸、页结束 0x0C）</item>
///   <item><see cref="GetAllBytes"/>：取出已写入的紧凑副本</item>
/// </list>
/// </remarks>
public sealed class PackageBuffer
{
    private readonly byte[] _buffer;
    private int _bufLen;

    /// <summary>创建指定容量的缓冲区；未指定则用默认容量 1000。对应 JS <c>new ve(t)</c>。</summary>
    public PackageBuffer(int capacity = 0)
    {
        var cap = capacity > ProtocolConstants.PackageBufferDefaultLength
            ? capacity
            : ProtocolConstants.PackageBufferDefaultLength;
        _buffer = new byte[cap];
        _bufLen = 0;
    }

    /// <summary>剩余可写空间。对应 JS <c>ve.FreeSpace</c>。</summary>
    public int FreeSpace => _buffer.Length - _bufLen;

    /// <summary>已写入字节数。对应 JS <c>ve.Length</c>。</summary>
    public int Length => _bufLen;

    /// <summary>
    /// 静态：将 CMD + data 序列化为完整协议帧。对应 JS <c>ve.getBytes(t, e)</c>。
    /// </summary>
    public static byte[] GetBytes(PrinterCommand cmd, IEnumerable<byte>? data) =>
        new ProtocolPacket(cmd, data).GetBytes();

    /// <summary>追加完整协议帧（带 CRC 0x88）。对应 JS <c>ve.pushPackage(t, e)</c>。</summary>
    public bool PushPackage(PrinterCommand cmd, IEnumerable<byte>? data) =>
        Push(GetBytes(cmd, data));

    /// <summary>追加单字节协议帧。对应 JS <c>ve.pushByte(t, e)</c>。</summary>
    public bool PushByte(PrinterCommand cmd, byte value) =>
        Push(GetBytes(cmd, new[] { value }));

    /// <summary>追加 16 位协议帧。对应 JS <c>ve.pushShort(t, e, i)</c>。</summary>
    /// <param name="asEbv">true=EBV 编码；false=2 字节大端。</param>
    public bool PushShort(PrinterCommand cmd, int value, bool asEbv)
    {
        var bytes = EbvHelper.GetBytesFromShort(value, asEbv);
        return Push(GetBytes(cmd, bytes));
    }

    /// <summary>追加 32 位协议帧。对应 JS <c>ve.pushInt(t, e)</c>。</summary>
    public bool PushInt(PrinterCommand cmd, int value)
    {
        var bytes = EbvHelper.GetBytesFromInt32(value);
        return Push(GetBytes(cmd, bytes));
    }

    /// <summary>
    /// 写入原始字节区间。对应 JS <c>ve.push(t, e, i)</c>。
    /// 返回 false 表示空间不足（与 JS 一致并打印警告）。
    /// </summary>
    public bool Push(ReadOnlySpan<byte> data) => Push(data, 0, data.Length);

    /// <summary>写入 <paramref name="data"/>[<paramref name="start"/>..<paramref name="end"/>)。对应 JS <c>ve.push(t,e,i)</c>。</summary>
    public bool Push(ReadOnlySpan<byte> data, int start, int end)
    {
        var n = end - start;
        if (n > _buffer.Length - _bufLen)
        {
            DzProtocolLog.Warn("---- PackageBuffer缓存不够！");
            return false;
        }
        data.Slice(start, n).CopyTo(_buffer.AsSpan(_bufLen));
        _bufLen += n;
        return true;
    }

    /// <summary>取出已写入字节的紧凑副本。对应 JS <c>ve.getAllBytes()</c>。</summary>
    public byte[] GetAllBytes()
    {
        var result = new byte[_bufLen];
        _buffer.AsSpan(0, _bufLen).CopyTo(result);
        return result;
    }

    /// <summary>清空缓冲区并填 0。对应 JS <c>ve.clearBuffer()</c>。</summary>
    public void ClearBuffer()
    {
        _bufLen = 0;
        Array.Clear(_buffer, 0, _buffer.Length);
    }

    public override string ToString() =>
        $"{{size: {_buffer.Length}, mBufLen: {_bufLen}}}";
}

/// <summary>
/// 发送缓冲区链表：对应 JS SDK 中的 <c>Ee</c> 类（原映射 <c>$e</c>）。
/// 内部维护 <see cref="PackageBuffer"/> 列表，按需追加新块；最终通过 <see cref="ToList"/>
/// 输出多个 <c>byte[]</c> 分片（分片大小受单块缓冲区容量约束，便于 BLE/HID 分包发送）。
/// </summary>
public sealed class PackageBufferList
{
    private readonly List<PackageBuffer> _bufferList;

    public PackageBufferList() => _bufferList = new List<PackageBuffer>();

    /// <summary>缓冲区分片列表。对应 JS <c>Ee.BufferList</c>。</summary>
    public IReadOnlyList<PackageBuffer> BufferList => _bufferList;

    /// <summary>分片数量。对应 JS <c>Ee.Length</c>。</summary>
    public int Length => _bufferList.Count;

    /// <summary>清空所有分片。对应 JS <c>Ee.reset()</c>。</summary>
    public void Reset() => _bufferList.Clear();

    /// <summary>返回所有分片的浅拷贝列表。对应 JS <c>Ee.toList()</c>。</summary>
    public List<PackageBuffer> ToList() => new(_bufferList);

    /// <summary>
    /// 追加一段原始字节区间，必要时新建分片。对应 JS <c>Ee.push(t, e, i)</c>。
    /// </summary>
    public void Push(ReadOnlySpan<byte> data, int start = 0, int end = -1)
    {
        if (end < 0) end = data.Length;
        var n = end - start;
        if (n <= 0) return;

        var last = _bufferList.Count > 0 ? _bufferList[^1] : null;
        if (last == null || last.FreeSpace < n)
        {
            last = new PackageBuffer(n);
            _bufferList.Add(last);
        }
        last.Push(data, start, end);
    }

    /// <summary>
    /// 追加两段字节的<em>拼接</em>（用于协议帧头 + 位图数据连续存放，且不带 CRC）。
    /// 对应 JS <c>Ee.push2(t,e,i,s,n,r)</c>。
    /// </summary>
    /// <remarks>
    /// JS 实现是：新建临时 ve → push 两段 → 再 push(temp.getAllBytes()) 到列表。
    /// 此处等价地直接拼接后调用 <see cref="Push(ReadOnlySpan{byte}, int, int)"/>。
    /// </remarks>
    public void Push2(ReadOnlySpan<byte> a, int aStart, int aEnd,
                      ReadOnlySpan<byte> b, int bStart, int bEnd)
    {
        var aLen = aEnd - aStart;
        var bLen = bEnd - bStart;
        if (aLen <= 0 && bLen <= 0) return;

        // 拼接两段（避免二次分片，行为与 JS 等价：两段在同一新分片中连续存放）
        var combined = new byte[aLen + bLen];
        if (aLen > 0) a.Slice(aStart, aLen).CopyTo(combined.AsSpan(0));
        if (bLen > 0) b.Slice(bStart, bLen).CopyTo(combined.AsSpan(aLen));

        Push(combined, 0, combined.Length);
    }

    /// <summary>追加完整协议帧（带 CRC）。对应 JS <c>Ee.pushPackage(t, e)</c>。</summary>
    public void PushPackage(PrinterCommand cmd, IEnumerable<byte>? data) =>
        Push(PackageBuffer.GetBytes(cmd, data));

    /// <summary>
    /// 便捷：将所有分片展开为 <c>byte[]</c> 列表（每个分片一个数组）。
    /// 对应 JS <see cref="PrintEncoder"/> 中 <c>n.map(t =&gt; t.getAllBytes())</c>。
    /// </summary>
    public List<byte[]> ToByteArrayList()
    {
        var result = new List<byte[]>(_bufferList.Count);
        foreach (var buf in _bufferList)
            result.Add(buf.GetAllBytes());
        return result;
    }
}

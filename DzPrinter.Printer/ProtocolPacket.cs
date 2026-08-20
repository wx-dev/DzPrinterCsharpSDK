using DzPrinter.Core;
using System.Buffers.Binary;

namespace DzPrinter.Printer;

/// <summary>
/// EBV（Extended Byte Value）编码与 CRC 计算的静态助手。
/// 对应 JS SDK 中 <c>be</c> 类的静态方法 <c>toEBV</c>/<c>fromEBV</c>/<c>toShort</c>/
/// <c>toNumber</c>/<c>getBytesFromShort</c>/<c>getBytesFromInt32</c>/<c>calcCRC</c> 等。
/// </summary>
/// <remarks>
/// <para><b>EBV 编码规则</b>（与 JS 逐字节一致）：</para>
/// <list type="bullet">
///   <item>值 &lt; 192：单字节直接发送 <c>(byte)value</c></item>
///   <item>值 ≥ 192：双字节，高字节 = <c>(value &gt;&gt; 8) | 0xC0</c>，低字节 = <c>value &amp; 0xFF</c></item>
/// </list>
/// <para><b>CRC 规则</b>：自 CMD 字节起至数据末尾逐字节累加，取反后截低 8 位：
/// <c>(byte)(~sum)</c>，等价于 JS 的 <c>255 &amp; ~sum</c>。</para>
/// <para>
/// <b>重要</b>：JS SDK 在<em>发送</em>时并不使用计算 CRC，而是固定填 <c>0x88</c>
/// （<c>be.FIXED_PACKAGE_CRC_RESULT</c>）。设备端同时接受固定值与计算值。
/// 因此 <see cref="ProtocolPacket.GetBytes"/> 输出固定 <c>0x88</c> 以保证与 JS 逐字节一致；
/// <see cref="CalcCrc"/> 仅用于<em>校验接收</em>到的数据包。
/// </para>
/// </remarks>
public static class EbvHelper
{
    /// <summary>
    /// 计算一个 EBV 值编码后的字节数（1 或 2）。
    /// </summary>
    public static int GetEbvByteCount(int value) => value < ProtocolConstants.EbvThreshold ? 1 : 2;

    /// <summary>
    /// 计算数据长度为 <paramref name="dataLength"/> 时完整协议帧的总字节数。
    /// JS: <c>be.getPackBytes(t) = t + (t &gt;= 192 ? 5 : 4)</c>。
    /// 帧结构 = 起始符(1) + CMD(1) + EBV长度(1或2) + 数据 + CRC(1)。
    /// </summary>
    public static int GetPackBytes(int dataLength) =>
        dataLength + (dataLength >= ProtocolConstants.EbvThreshold ? 5 : 4);

    /// <summary>
    /// 将 EBV 值写入 <paramref name="buffer"/> 的 <paramref name="offset"/> 处，返回写入后的新偏移。
    /// JS: <c>be.pushEBV</c> / <c>Se.pushEBV</c>。
    /// </summary>
    public static int WriteEbv(Span<byte> buffer, int offset, int value)
    {
        if (value >= ProtocolConstants.EbvThreshold)
        {
            // 高字节：高 6 位 + 0xC0 标记位；低字节：低 8 位
            buffer[offset] = (byte)((value >> 8) | 0xC0);
            buffer[offset + 1] = (byte)(value & 0xFF);
            return offset + 2;
        }
        buffer[offset] = (byte)value;
        return offset + 1;
    }

    /// <summary>
    /// 将 EBV 值追加到 <paramref name="list"/> 末尾。
    /// </summary>
    public static void AppendEbv(List<byte> list, int value)
    {
        if (value >= ProtocolConstants.EbvThreshold)
        {
            list.Add((byte)((value >> 8) | 0xC0));
            list.Add((byte)(value & 0xFF));
        }
        else
        {
            list.Add((byte)value);
        }
    }

    /// <summary>
    /// 将 EBV 值编码为字节数组。JS: <c>be.fromEBV(t)</c>。
    /// </summary>
    public static byte[] FromEbv(int value) => value >= ProtocolConstants.EbvThreshold
        ? new byte[] { (byte)((value >> 8) | 0xC0), (byte)(value & 0xFF) }
        : new byte[] { (byte)value };

    /// <summary>
    /// 从缓冲区读取 EBV 值（剥离 0xC0 标记位）。
    /// JS: <c>be.toEBV(t, e) = e &amp;&amp; e &gt;= 192 ? toNumber(t, -193 &amp; e) : 255 &amp; t</c>。
    /// <paramref name="low"/> 为低字节，<paramref name="high"/> 为高字节（含 0xC0 标记）。
    /// </summary>
    public static int ToEbv(int low, int high) =>
        high != 0 && high >= ProtocolConstants.EbvThreshold
            ? ToNumber(low, high & 0x3F)   // -193 & e 等价于 e & 0x3F（保留低 6 位）
            : low & 0xFF;

    /// <summary>
    /// 按小端序组装 4 字节为 32 位整数（低字节在前）。
    /// JS: <c>be.toNumber(t, e, i, s) = (t&amp;0xFF) | ((e&amp;0xFF)&lt;&lt;8) | ((i&amp;0xFF)&lt;&lt;16) | ((s&amp;0xFF)&lt;&lt;24)</c>。
    /// 注意：参数顺序是低→高，用于把<em>大端存储</em>的字节序列还原成数值。
    /// </summary>
    public static int ToNumber(int b0 = 0, int b1 = 0, int b2 = 0, int b3 = 0) =>
        (b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0xFF) << 16) | ((b3 & 0xFF) << 24);

    /// <summary>
    /// 读取大端 16 位无符号整数。JS: <c>be.toShort(high, low)</c> 语义为 low|(high&lt;&lt;8)。
    /// 此处提供更直观的大端读取：高位在前。
    /// </summary>
    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> buffer) =>
        BinaryPrimitives.ReadUInt16BigEndian(buffer);

    /// <summary>
    /// 将 32 位整数按大端序写入 4 字节数组。JS: <c>be.getBytesFromInt32(t)</c>。
    /// </summary>
    public static byte[] GetBytesFromInt32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>
    /// 将 16 位整数编码为字节数组。
    /// JS: <c>be.getBytesFromShort(t, e)</c>：
    /// <list type="bullet">
    ///   <item><paramref name="asEbv"/> = true：按 EBV 编码（值≥192 时双字节，否则单字节）</item>
    ///   <item><paramref name="asEbv"/> = false：固定 2 字节大端</item>
    /// </list>
    /// </summary>
    public static byte[] GetBytesFromShort(int value, bool asEbv)
    {
        if (asEbv) return FromEbv(value);
        return [(byte)(value >> 8), (byte)value];
    }

    /// <summary>
    /// 将整数按大端序编码为指定长度的字节数组。JS: <c>be.getBytesFromNumber(t, e)</c>。
    /// </summary>
    public static byte[] GetBytesFromNumber(int value, int byteCount)
    {
        var bytes = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
            bytes[i] = (byte)(value >> (8 * (byteCount - 1 - i)) & 0xFF);
        return bytes;
    }

    /// <summary>
    /// 计算 CRC：对 <paramref name="buffer"/>[<paramref name="start"/> .. <paramref name="end"/>) 区间逐字节累加，
    /// 取反后截低 8 位。JS: <c>be.calcCRC(t, e, i) = 255 &amp; ~sum</c>。
    /// </summary>
    public static byte CalcCrc(ReadOnlySpan<byte> buffer, int start, int end)
    {
        var sum = 0;
        for (var i = start; i < end; i++)
            sum += buffer[i];
        return (byte)(~sum & 0xFF);
    }

    /// <summary>
    /// 从原始协议帧中提取 payload（剥离帧头 0x1F、CMD、EBV 长度、CRC）。
    /// 帧结构：[0x1F][CMD][EBV长度(1或2)][data...][CRC]。
    /// </summary>
    /// <param name="rawFrame">设备返回的原始字节。</param>
    /// <returns>payload 字节数组；帧格式无效时返回 null。</returns>
    public static byte[]? TryGetPayload(byte[]? rawFrame)
    {
        if (rawFrame == null || rawFrame.Length < 4) return null;
        if (rawFrame[0] != ProtocolConstants.HostToDeviceDataStart) return null;

        int dataOffset;
        int dataLength;
        if (rawFrame[2] >= ProtocolConstants.EbvThreshold)
        {
            if (rawFrame.Length < 5) return null;
            dataLength = ((rawFrame[2] & 0x3F) << 8) | rawFrame[3];
            dataOffset = 4;
        }
        else
        {
            dataLength = rawFrame[2];
            dataOffset = 3;
        }

        if (rawFrame.Length < dataOffset + dataLength + 1) return null;
        var payload = new byte[dataLength];
        Array.Copy(rawFrame, dataOffset, payload, 0, dataLength);
        return payload;
    }
}

/// <summary>
/// 协议数据包：对应 JS SDK 中的 <c>be</c> 类。
/// 负责 CMD + 数据负载的封装与解析，生成与原始 JS <c>getBytes()</c> 逐字节一致的帧。
/// </summary>
/// <remarks>
/// <para><b>发送帧结构</b>（GetBytes 输出）：</para>
/// <code>
///   值 &lt; 192 : [0x1F][CMD][len        ][data...][0x88]
///   值 ≥ 192 : [0x1F][CMD][lenHi|0xC0][lenLo][data...][0x88]
/// </code>
/// <para>其中 CRC 固定为 <c>0x88</c>（<see cref="ProtocolConstants.FixedPackageCrcResult"/>），
/// 与 JS <c>be.getBytes()</c> 完全一致。设备端同时接受 <c>0x88</c> 与计算 CRC。</para>
/// </remarks>
public sealed class ProtocolPacket
{
    private readonly List<byte> _data;
    private int _offset;
    private byte[]? _rawData;

    /// <summary>创建一个空数据包（仅指定 CMD）。对应 JS <c>new be(cmd)</c>。</summary>
    public ProtocolPacket(PrinterCommand cmd) : this(cmd, null) { }

    /// <summary>创建数据包。对应 JS <c>new be(t, e)</c>。</summary>
    public ProtocolPacket(PrinterCommand cmd, IEnumerable<byte>? data)
    {
        Command = cmd;
        _data = data == null ? new List<byte>() : new List<byte>(data);
        _offset = 0;
    }

    // ── 属性 ──────────────────────────────────────────────
    public PrinterCommand Command { get; }
    public byte CmdByte => (byte)Command;

    /// <summary>数据负载（不含帧头/CRC）。对应 JS <c>be.Data</c>。</summary>
    public IReadOnlyList<byte> Data => _data;

    /// <summary>数据长度。对应 JS <c>be.Length</c>。</summary>
    public int Length => _data.Count;

    /// <summary>剩余可读字节数。对应 JS <c>be.Remains</c>。</summary>
    public int Remains => _data.Count - _offset;

    /// <summary>原始接收到的完整帧（含帧头/CRC）。对应 JS <c>be.getRawData()</c>。</summary>
    public byte[]? RawData => _rawData;

    /// <summary>设置原始帧数据。对应 JS <c>be.setRawData(t)</c>。</summary>
    public void SetRawData(byte[] raw) => _rawData = raw;

    // ── 静态工厂 ──────────────────────────────────────────

    /// <summary>从 CMD 与数据构造。对应 JS <c>be.fromBuffer(t, e)</c>。</summary>
    public static ProtocolPacket FromBuffer(PrinterCommand cmd, ReadOnlySpan<byte> data) =>
        new(cmd, data.ToArray());

    /// <summary>
    /// 解析设备→主机的完整帧。对应 JS <c>be.fromRawData(t)</c>。
    /// 注意：JS 原始实现中双字节长度分支使用了 <c>toShort</c>（未剥离 0xC0 标记），
    /// 实为缺陷；此处使用 <see cref="EbvHelper.ToEbv"/> 正确还原长度。
    /// </summary>
    public static ProtocolPacket? FromRawData(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4 || buffer[0] != ProtocolConstants.DeviceToHostDataStart)
            return null;

        var cmd = (PrinterCommand)buffer[1];
        if ((byte)cmd == 0) return null;

        if (buffer[2] >= ProtocolConstants.EbvThreshold)
        {
            // 双字节长度：高字节含 0xC0 标记，需剥离
            var len = EbvHelper.ToEbv(buffer[3], buffer[2]);
            if (buffer.Length < len + 5) return null;

            var receivedCrc = buffer[len + 4];
            var calcCrc = EbvHelper.CalcCrc(buffer, 1, len + 4);
            if (receivedCrc != ProtocolConstants.FixedPackageCrcResult && receivedCrc != calcCrc)
            {
                DzPrinterLog.Warn(
                    $"---- CRC校验失败：cmd = 0x{(byte)cmd:X2}, receiveCRC = {receivedCrc}, calcCRC = {calcCrc}");
                return null;
            }

            var pkt = FromBuffer(cmd, buffer.Slice(4, len));
            pkt.SetRawData(buffer.Slice(0, len + 5).ToArray());
            return pkt;
        }
        else
        {
            // 单字节长度
            var len = buffer[2];
            if (buffer.Length < len + 4) return null;

            var receivedCrc = buffer[len + 3];
            var calcCrc = EbvHelper.CalcCrc(buffer, 1, len + 3);
            if (receivedCrc != ProtocolConstants.FixedPackageCrcResult && receivedCrc != calcCrc)
            {
                DzPrinterLog.Warn(
                    $"---- CRC校验失败：cmd = 0x{(byte)cmd:X2}, receiveCrc = {receivedCrc}, calcCrc = {calcCrc}");
                return null;
            }

            var pkt = FromBuffer(cmd, buffer.Slice(3, len));
            pkt.SetRawData(buffer.Slice(0, len + 4).ToArray());
            return pkt;
        }
    }

    /// <summary>解析帧的便捷入口。对应 JS <c>be.parse(t)</c>。</summary>
    public static ProtocolPacket? Parse(byte[] buffer) => FromRawData(buffer);

    // ── 写入（构建发送数据）──────────────────────────────

    /// <summary>追加单字节。对应 JS <c>be.pushByte(t)</c>。</summary>
    public void PushByte(byte value) => _data.Add(value);

    /// <summary>追加 16 位整数。对应 JS <c>be.pushShort(t, e)</c>。</summary>
    /// <param name="asEbv">true=EBV 编码；false=2 字节大端。</param>
    public void PushShort(int value, bool asEbv)
    {
        if (asEbv)
        {
            if (value >= ProtocolConstants.EbvThreshold)
            {
                _data.Add((byte)((value >> 8) | 0xC0));
                _data.Add((byte)(value & 0xFF));
            }
            else
            {
                _data.Add((byte)value);
            }
        }
        else
        {
            _data.Add((byte)(value >> 8));
            _data.Add((byte)value);
        }
    }

    /// <summary>追加 EBV 值，返回新的内部偏移。对应 JS <c>be.pushEBV(t)</c>。</summary>
    public int PushEbv(int value)
    {
        if (value >= ProtocolConstants.EbvThreshold)
        {
            _data.Add((byte)((value >> 8) | 0xC0));
            _data.Add((byte)(value & 0xFF));
            _offset += 2;
        }
        else
        {
            _data.Add((byte)value);
            _offset += 1;
        }
        return _offset;
    }

    /// <summary>追加 32 位整数（大端 4 字节）。对应 JS <c>be.pushInt(t)</c>。</summary>
    public void PushInt(int value)
    {
        _data.Add((byte)(value >> 24));
        _data.Add((byte)(value >> 16));
        _data.Add((byte)(value >> 8));
        _data.Add((byte)value);
    }

    // ── 读取（解析接收数据）──────────────────────────────

    /// <summary>读取单字节。对应 JS <c>be.popByte(t)</c>。</summary>
    public byte PopByte(byte defaultValue = 0) =>
        _offset < _data.Count ? _data[_offset++] : defaultValue;

    /// <summary>读取 16 位整数（大端）。对应 JS <c>be.popShort(t)</c>。</summary>
    public int PopShort(int defaultValue = 0)
    {
        var i = _offset;
        if (i <= _data.Count - 2)
        {
            // toShort(mData[i+1], mData[i]) = mData[i+1] | (mData[i]<<8) → 大端
            var v = (_data[i] << 8) | _data[i + 1];
            _offset += 2;
            return v;
        }
        return PopByte((byte)defaultValue);
    }

    /// <summary>读取 EBV 值。对应 JS <c>be.popEBV(t)</c>。</summary>
    public int PopEbv(int defaultValue = 0)
    {
        var i = _offset;
        if (i >= _data.Count) return defaultValue;
        if (_data[i] >= ProtocolConstants.EbvThreshold && _data.Count >= i + 2)
        {
            var v = EbvHelper.ToEbv(_data[i + 1], _data[i]);
            _offset += 2;
            return v;
        }
        return _data[_offset++];
    }

    /// <summary>读取 32 位整数（大端）。对应 JS <c>be.popInt(t)</c>。</summary>
    public int PopInt(int defaultValue = 0)
    {
        var i = _offset;
        if (i + 4 <= _data.Count)
        {
            // toNumber(b3, b2, b1, b0)：b0=LSB，即大端存储
            var v = EbvHelper.ToNumber(_data[i + 3], _data[i + 2], _data[i + 1], _data[i]);
            _offset += 4;
            return v;
        }
        return defaultValue;
    }

    /// <summary>等价于 <see cref="PopInt"/>。对应 JS <c>be.popInteger(t)</c>。</summary>
    public int PopInteger(int defaultValue = 0) => PopInt(defaultValue);

    /// <summary>
    /// 读取 GBK 编码的以 0 结尾字符串。对应 JS <c>be.popString()</c>。
    /// </summary>
    public string PopString()
    {
        var start = _offset;
        if (start >= _data.Count) return string.Empty;

        var end = start;
        while (end < _data.Count && _data[end] != 0) end++;
        _offset = end;

        var bytes = new byte[end - start];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = _data[start + i];
        return GbkUtils.Decode(bytes);
    }

    /// <summary>读取剩余全部字节。对应 JS <c>be.popByteArray()</c>。</summary>
    public byte[] PopByteArray()
    {
        if (_offset >= _data.Count) return Array.Empty<byte>();
        var result = _data.GetRange(_offset, _data.Count - _offset).ToArray();
        _offset = _data.Count;
        return result;
    }

    // ── 序列化 ────────────────────────────────────────────

    /// <summary>完整帧字节数（含帧头与 CRC）。对应 JS <c>be.getBufferLength()</c>。</summary>
    public int GetBufferLength() => EbvHelper.GetPackBytes(_data.Count);

    /// <summary>
    /// 生成完整协议帧。对应 JS <c>be.getBytes()</c>。
    /// <b>逐字节与 JS SDK 一致</b>：CRC 固定为 <c>0x88</c>。
    /// </summary>
    public byte[] GetBytes()
    {
        var len = _data.Count;
        var packLen = EbvHelper.GetPackBytes(len); // len + (len>=192 ? 5 : 4)
        var buffer = new byte[packLen];

        buffer[0] = ProtocolConstants.HostToDeviceDataStart; // 0x1F
        buffer[1] = (byte)Command;

        if (len >= ProtocolConstants.EbvThreshold)
        {
            // 双字节长度：[lenHi|0xC0][lenLo]
            buffer[2] = (byte)((len >> 8) | 0xC0);
            buffer[3] = (byte)(len & 0xFF);
            BufferCopyTo(buffer, 4);
            buffer[len + 4] = ProtocolConstants.FixedPackageCrcResult; // 0x88
        }
        else
        {
            // 单字节长度
            buffer[2] = (byte)len;
            BufferCopyTo(buffer, 3);
            buffer[len + 3] = ProtocolConstants.FixedPackageCrcResult; // 0x88
        }

        return buffer;
    }

    private void BufferCopyTo(byte[] dest, int destOffset)
    {
        // 将 _data 拷贝到 dest[destOffset..]
        if (_data.Count == 0) return;
        _data.CopyTo(dest, destOffset);
    }
}

namespace DzPrinter.Barcode;

/// <summary>
/// QR 码版本合法性校验。对应 JS SDK 中 <c>Jt</c> 类。
/// </summary>
internal static class QrVersion
{
    /// <summary>
    /// 验证版本号是否合法（1-40）。对应 JS <c>Jt.isValid(t)</c>。
    /// </summary>
    public static bool IsValid(int version) => version >= 1 && version <= 40;
}

/// <summary>
/// QR 码版本选择工具。对应 JS SDK 中 <c>oe</c> 类。
/// 根据数据长度与纠错等级选择能容纳数据的最小版本。
/// </summary>
internal static class QrVersionUtils
{
    /// <summary>
    /// 7973（版本信息 BCH 多项式）的 BCH 位数。对应 JS <c>const ae = wt.getBCHDigit(7973)</c>。
    /// 7973 = 0b1111100100101，共 13 位。
    /// </summary>
    private const int VersionPolyBchDigit = BchDigitConstants.VersionPoly;

    /// <summary>
    /// 根据数据长度查找最佳版本。对应 JS <c>oe.getBestVersionForDataLength(t, e, i)</c>。
    /// 遍历 1-40，返回首个容量 ≥ dataLength 的版本；若均不够返回 0（JS 中为 undefined）。
    /// </summary>
    public static int GetBestVersionForDataLength(int dataLength, EccLevel eccLevel, QrMode mode)
    {
        for (var v = 1; v <= 40; v++)
            if (dataLength <= GetCapacity(v, eccLevel, mode)) return v;
        return 0;
    }

    /// <summary>
    /// 获取指定模式与版本下的保留位数（模式指示 4 位 + 字符计数指示）。
    /// 对应 JS <c>oe.getReservedBitsCount(t, e)</c>。
    /// </summary>
    public static int GetReservedBitsCount(QrMode mode, int version) =>
        QrModeUtils.GetCharCountIndicator(mode, version) + 4;

    /// <summary>
    /// 计算分段数据在指定版本下的总位数。对应 JS <c>oe.getTotalBitsFromDataArray(t, e)</c>。
    /// </summary>
    public static int GetTotalBitsFromDataArray(IList<QrSegmentBase>? segments, int version)
    {
        var total = 0;
        if (segments == null) return total;
        foreach (var seg in segments)
        {
            var reserved = GetReservedBitsCount(seg.Mode, version);
            total += reserved + seg.GetBitsLength();
        }
        return total;
    }

    /// <summary>
    /// 根据混合模式数据查找最佳版本。对应 JS <c>oe.getBestVersionForMixedData(t, e, i)</c>。
    /// JS 中 <c>s = t &amp;&amp; t.length &gt; 0 ? t.length : 0</c>，注意这里直接使用 bit 长度（不除以 8）。
    /// </summary>
    /// <param name="bufferBitLength">缓冲区已存在的位数（对应 JS <c>t.length</c>）。</param>
    /// <param name="segments">分段列表。</param>
    /// <param name="eccLevel">纠错等级。</param>
    public static int GetBestVersionForMixedData(int bufferBitLength, IList<QrSegmentBase>? segments, EccLevel eccLevel)
    {
        // JS: const s = t && t.length > 0 ? t.length : 0
        var s = bufferBitLength > 0 ? bufferBitLength : 0;
        for (var v = 1; v <= 40; v++)
            if (GetTotalBitsFromDataArray(segments, v) + s <= GetCapacity(v, eccLevel, QrMode.Mixed))
                return v;
        return 0;
    }

    /// <summary>版本合法性校验。对应 JS <c>oe.isValid(t)</c>。</summary>
    public static bool IsValid(int version) => QrVersion.IsValid(version);

    /// <summary>
    /// 获取指定版本/纠错等级/模式下的数据容量。对应 JS <c>oe.getCapacity(t, e, i)</c>。
    /// </summary>
    /// <remarks>
    /// JS 中 <c>(St.getTotalCodewordsCount(t,e)||0)</c> 利用 JS 短路：若返回 undefined（default case）则取 0。
    /// C# 中 <see cref="QrBlocks.GetTotalCodewordsCount"/> 不会返回 0 以外的非法值，按相同结果处理。
    /// </remarks>
    public static int GetCapacity(int version, EccLevel eccLevel, QrMode? mode)
    {
        if (!QrVersion.IsValid(version)) throw new ArgumentException("Invalid QR Code version");
        var m = mode ?? QrMode.Byte;

        // 8 * (总码字数 - EC 码字数)
        var total = 8 * (QrSymbolUtils.GetSymbolTotalCodewords(version) - QrBlocks.GetTotalCodewordsCount(version, eccLevel));

        if (ReferenceEquals(m, QrMode.Mixed)) return total;

        var n = total - GetReservedBitsCount(m, version);
        if (ReferenceEquals(m, QrMode.Numeric)) return n / 10 * 3;
        if (ReferenceEquals(m, QrMode.Alphanumeric)) return n / 11 * 2;
        if (ReferenceEquals(m, QrMode.Kanji)) return n / 13;
        // BYTE / default
        return n / 8;
    }

    /// <summary>
    /// 根据分段数据查找最佳版本。对应 JS <c>oe.getBestVersionForData(t, e, i)</c>。
    /// </summary>
    /// <param name="bufferBitLength">缓冲区已存在的位数（对应 JS <c>t.length</c>）；为 0 表示无缓冲。</param>
    /// <param name="segments">分段列表（若为单段则按段模式计算，多段则按混合模式计算）。</param>
    /// <param name="eccLevel">纠错等级。</param>
    public static int GetBestVersionForData(int bufferBitLength, IList<QrSegmentBase> segments, EccLevel eccLevel)
    {
        // JS: if (Array.isArray(e)) { if (e.length > 1) return getBestVersionForMixedData(t, e, i); if (e.length === 0) return 1; s = e[0] } else s = e;
        if (segments != null && segments.Count > 1)
            return GetBestVersionForMixedData(bufferBitLength, segments, eccLevel);
        if (segments == null || segments.Count == 0) return 1;

        var seg = segments[0];
        // JS: const n = t ? Math.ceil(t.length / 8) : 0
        var n = bufferBitLength > 0 ? (int)Math.Ceiling(bufferBitLength / 8.0) : 0;
        return GetBestVersionForDataLength(seg.GetLength() + n, eccLevel, seg.Mode);
    }

    /// <summary>
    /// 编码版本信息位（18 位）。对应 JS <c>oe.getEncodedBits(t)</c>。
    /// 仅版本 7-40 需要写入版本信息；算法：版本号左移 12 位后与 7973 多项式做 BCH 异或。
    /// </summary>
    public static int GetEncodedBits(int version)
    {
        if (!QrVersion.IsValid(version) || version < 7)
            throw new ArgumentException("Invalid QR Code version");
        var e = version << 12;
        while (QrSymbolUtils.GetBchDigit(e) - VersionPolyBchDigit >= 0)
            e ^= 7973 << (QrSymbolUtils.GetBchDigit(e) - VersionPolyBchDigit);
        return (version << 12) | e;
    }
}

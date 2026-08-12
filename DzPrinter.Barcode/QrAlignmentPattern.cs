namespace DzPrinter.Barcode;

/// <summary>
/// QR 码对齐图案位置计算。对应 JS SDK 中 <c>Tt</c> 类。
/// </summary>
internal static class QrAlignmentPattern
{
    /// <summary>
    /// 获取对齐图案的行/列坐标。对应 JS <c>Tt.getRowColCoordinates(t)</c>。
    /// 算法：版本 1 返回空；其他版本按等距间隔生成坐标数组（含 6 与 size-7）。
    /// </summary>
    public static int[] GetRowColCoordinates(int version)
    {
        if (version == 1) return System.Array.Empty<int>();

        var e = version / 7 + 2;
        var size = QrSymbolUtils.GetSymbolSize(version);
        var step = size == 145 ? 26 : 2 * (int)System.Math.Ceiling((double)(size - 13) / (2 * e - 2));
        var coords = new List<int> { size - 7 };
        for (var i = 1; i < e - 1; i++) coords.Add(coords[i - 1] - step);
        coords.Add(6);
        coords.Reverse();
        return coords.ToArray();
    }

    /// <summary>
    /// 获取所有对齐图案的中心坐标。对应 JS <c>Tt.getPositions(t)</c>。
    /// 排除三个与探测图案重叠的角（左上、右上、左下）。
    /// </summary>
    public static int[][] GetPositions(int version)
    {
        var coords = GetRowColCoordinates(version);
        var s = coords.Length;
        var result = new List<int[]>();
        for (var i = 0; i < s; i++)
        {
            for (var j = 0; j < s; j++)
            {
                // 跳过三个角（与探测图案重叠）
                if (i == 0 && j == 0) continue;
                if (i == 0 && j == s - 1) continue;
                if (i == s - 1 && j == 0) continue;
                result.Add(new[] { coords[i], coords[j] });
            }
        }
        return result.ToArray();
    }
}

/// <summary>
/// QR 码 EC 块信息。对应 JS SDK 中 <c>St</c> 类。
/// </summary>
internal static class QrBlocks
{
    /// <summary>
    /// 获取指定版本/纠错等级的 EC 块数。对应 JS <c>St.getBlocksCount(e, i)</c>。
    /// </summary>
    public static int GetBlocksCount(int version, EccLevel eccLevel) =>
        QrTables.EcBlockCounts[4 * (version - 1) + (int)eccLevel];

    /// <summary>
    /// 获取指定版本/纠错等级的 EC 码字总数。对应 JS <c>St.getTotalCodewordsCount(e, i)</c>。
    /// </summary>
    public static int GetTotalCodewordsCount(int version, EccLevel eccLevel) =>
        QrTables.EcCodewordCounts[4 * (version - 1) + (int)eccLevel];
}

/// <summary>
/// QR 码格式信息编码。对应 JS SDK 中 <c>Bt</c> 类。
/// 格式信息为 15 位：5 位（2 位 ECC + 3 位 mask）+ 10 位 BCH 纠错，再异或 21522 掩码。
/// </summary>
internal static class QrFormatInfo
{
    /// <summary>
    /// 获取纠错等级对应的 2 位编码。对应 JS <c>Bt.getEccBit(e)</c>。
    /// L=1, M=0, Q=3, H=2（注意：不是枚举数值顺序）。
    /// </summary>
    public static int GetEccBit(EccLevel eccLevel) => eccLevel switch
    {
        EccLevel.Low => 1,
        EccLevel.Middle => 0,
        EccLevel.Quality => 3,
        EccLevel.High => 2,
        _ => 0  // JS: default case → Middle → 0
    };

    /// <summary>
    /// 编码格式信息位（15 位）。对应 JS <c>Bt.getEncodedBits(t, e)</c>。
    /// 算法：5 位数据（ecc2 + mask3）左移 10，与 1335 做 BCH 异或，最后与 21522 掩码异或。
    /// </summary>
    public static int GetEncodedBits(EccLevel eccLevel, int maskPattern)
    {
        var data = (GetEccBit(eccLevel) << 3) | maskPattern;
        var bch = data << 10;
        // BCH 多项式 1335 的位数为 11（即 Mt 常量）
        while (QrSymbolUtils.GetBchDigit(bch) - BchDigitConstants.FormatPoly >= 0)
            bch ^= 1335 << (QrSymbolUtils.GetBchDigit(bch) - BchDigitConstants.FormatPoly);
        return 21522 ^ ((data << 10) | bch);
    }
}

/// <summary>
/// QR 码探测图案位置。对应 JS SDK 中 <c>ce</c> 类。
/// </summary>
internal static class QrFinderPattern
{
    /// <summary>
    /// 获取三个探测图案的左上角坐标。对应 JS <c>ce.getPositions(t)</c>。
    /// 返回左上、右上、左下三个 7×7 探测图案的位置。
    /// </summary>
    public static int[][] GetPositions(int version)
    {
        var size = QrSymbolUtils.GetSymbolSize(version);
        return new[] { new[] { 0, 0 }, new[] { size - 7, 0 }, new[] { 0, size - 7 } };
    }
}

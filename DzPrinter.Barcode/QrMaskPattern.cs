namespace DzPrinter.Barcode;

/// <summary>
/// QR 码掩码图案与罚分计算。对应 JS SDK 中 <c>Nt</c> 类。
/// </summary>
internal static class QrMaskPattern
{
    /// <summary>
    /// 掩码图案常量。对应 JS <c>Nt.Patterns</c>。
    /// </summary>
    public static class Patterns
    {
        public const int PATTERN000 = 0;
        public const int PATTERN001 = 1;
        public const int PATTERN010 = 2;
        public const int PATTERN011 = 3;
        public const int PATTERN100 = 4;
        public const int PATTERN101 = 5;
        public const int PATTERN110 = 6;
        public const int PATTERN111 = 7;
    }

    /// <summary>
    /// 验证掩码图案编号是否合法。对应 JS <c>Nt.isValid(t)</c>。
    /// </summary>
    /// <remarks>
    /// JS Bug 保留：JS 中 <c>t &amp;&amp; !isNaN(t) &amp;&amp; t &gt;= 0 &amp;&amp; t &lt;= 7</c>，
    /// 由于 JS 的 0 是 falsy，<c>isValid(0)</c> 返回 falsy（即 PATTERN000 被错误拒绝）。
    /// C# 中按相同行为实现：pattern=0 返回 false。
    /// </remarks>
    public static bool IsValid(int pattern)
    {
        if (pattern == 0) return false;  // JS: 0 is falsy → false
        return pattern >= 0 && pattern <= 7;
    }

    /// <summary>
    /// 罚分规则 N1：连续 5+ 同色模块。对应 JS <c>Nt.getPenaltyN1(t)</c>。
    /// 每行/列中连续同色 ≥5 的段罚 (length - 5 + 3)。
    /// </summary>
    public static int GetPenaltyN1(BitMatrix matrix)
    {
        var size = matrix.Cols;
        var penalty = 0;
        for (var row = 0; row < size; row++)
        {
            int hRun = 0, vRun = 0;
            int hLast = -1, vLast = -1;
            for (var col = 0; col < size; col++)
            {
                var h = matrix.Get(row, col);
                if (h == hLast) hRun++;
                else
                {
                    if (hRun >= 5) penalty += hRun - 5 + 3;
                    hLast = h;
                    hRun = 1;
                }

                var v = matrix.Get(col, row);
                if (v == vLast) vRun++;
                else
                {
                    if (vRun >= 5) penalty += vRun - 5 + 3;
                    vLast = v;
                    vRun = 1;
                }
            }
            if (hRun >= 5) penalty += hRun - 5 + 3;
            if (vRun >= 5) penalty += vRun - 5 + 3;
        }
        return penalty;
    }

    /// <summary>
    /// 罚分规则 N2：2×2 同色块。对应 JS <c>Nt.getPenaltyN2(t)</c>。
    /// 每个 2×2 同色块罚 3 分。
    /// </summary>
    public static int GetPenaltyN2(BitMatrix matrix)
    {
        var size = matrix.Cols;
        var penalty = 0;
        for (var row = 0; row < size - 1; row++)
        {
            for (var col = 0; col < size - 1; col++)
            {
                var sum = matrix.Get(row, col) + matrix.Get(row, col + 1)
                        + matrix.Get(row + 1, col) + matrix.Get(row + 1, col + 1);
                if (sum == 4 || sum == 0) penalty++;
            }
        }
        return 3 * penalty;
    }

    /// <summary>
    /// 罚分规则 N3：1:1:3:1:1 模式（探测图案相似）。对应 JS <c>Nt.getPenaltyN3(t)</c>。
    /// 每行/列中滑动窗口检测 11 位模式 10111010000（=1488）或 0001011101（=93）。
    /// </summary>
    public static int GetPenaltyN3(BitMatrix matrix)
    {
        var size = matrix.Cols;
        var penalty = 0;
        for (var row = 0; row < size; row++)
        {
            int h = 0, v = 0;
            for (var col = 0; col < size; col++)
            {
                h = (h << 1 & 2047) | matrix.Get(row, col);
                if (col >= 10 && (h == 1488 || h == 93)) penalty++;

                v = (v << 1 & 2047) | matrix.Get(col, row);
                if (col >= 10 && (v == 1488 || v == 93)) penalty++;
            }
        }
        return 40 * penalty;
    }

    /// <summary>
    /// 罚分规则 N4：黑模块占比偏差。对应 JS <c>Nt.getPenaltyN4(t)</c>。
    /// 计算黑模块占比与 50% 的偏差，罚 10 × |round(pct/5) - 10|。
    /// </summary>
    public static int GetPenaltyN4(BitMatrix matrix)
    {
        var total = 0;
        var data = matrix.Data;
        for (var i = 0; i < data.Length; i++) total += data[i];
        return 10 * Math.Abs((int)Math.Ceiling(100.0 * total / data.Length / 5) - 10);
    }

    /// <summary>
    /// 获取指定掩码图案在 (row, col) 处的掩码位。对应 JS <c>Nt.getMaskAt(t, e, i)</c>。
    /// 注意：JS 中参数顺序为 (pattern, row, col)，与 (e=row, i=col) 对应。
    /// </summary>
    public static bool GetMaskAt(int pattern, int row, int col) => pattern switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => (row / 2 + col / 3) % 2 == 0,
        5 => (row * col % 2 + row * col % 3) == 0,
        6 => (row * col % 2 + row * col % 3) % 2 == 0,
        7 => (row * col % 3 + (row + col) % 2) % 2 == 0,
        _ => throw new ArgumentException("bad maskPattern:" + pattern)
    };

    /// <summary>
    /// 应用掩码图案到矩阵。对应 JS <c>Nt.applyMask(t, e)</c>。
    /// 仅对未保留的位置执行 XOR（保留位为功能图形）。
    /// </summary>
    public static void ApplyMask(int pattern, BitMatrix matrix)
    {
        var size = matrix.Cols;
        for (var col = 0; col < size; col++)
        {
            for (var row = 0; row < size; row++)
            {
                if (!matrix.IsReserved(row, col))
                    matrix.Xor(row, col, GetMaskAt(pattern, row, col));
            }
        }
    }

    /// <summary>
    /// 选择罚分最低的掩码图案。对应 JS <c>Nt.getBestMask(t, e)</c>。
    /// 遍历 8 种图案，对每种应用 → 计算总罚分 → 取消应用，记录最优。
    /// </summary>
    /// <param name="matrix">QR 矩阵。</param>
    /// <param name="setupFunc">每种掩码的数据设置回调（用于重新设置格式信息位）。</param>
    public static int GetBestMask(BitMatrix matrix, Action<int> setupFunc)
    {
        var patternCount = 8;
        var bestPattern = 0;
        var minPenalty = int.MaxValue;  // JS: 1/0 = Infinity
        for (var p = 0; p < patternCount; p++)
        {
            setupFunc(p);
            ApplyMask(p, matrix);
            var penalty = GetPenaltyN1(matrix) + GetPenaltyN2(matrix)
                        + GetPenaltyN3(matrix) + GetPenaltyN4(matrix);
            ApplyMask(p, matrix);  // XOR twice = identity（取消应用）
            if (penalty < minPenalty)
            {
                minPenalty = penalty;
                bestPattern = p;
            }
        }
        return bestPattern;
    }
}

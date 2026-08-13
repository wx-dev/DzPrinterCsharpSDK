namespace DzPrinter.Printer;

/// <summary>
/// 位图行数据 RLE 压缩编码器：对应 JS SDK 中的 <c>we</c> 类。
/// 实现五种压缩变体，输出与原始 JS <b>逐字节一致</b>：
/// <list type="bullet">
///   <item><term>RLEC</term><description>经典 RLE，标记字节 + 重复值</description></item>
///   <item><term>RLE5X</term><description>5 位紧致编码（单图）</description></item>
///   <item><term>RLE5D</term><description>5 位紧致编码（与上一行差分）</description></item>
///   <item><term>RLE6X</term><description>6 位紧致编码（单图）</description></item>
///   <item><term>RLE6D</term><description>6 位紧致编码（与上一行差分）</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>5 位编码</b>：每个游程编码为 5 位码 = 索引(4 位) | 颜色(1 位)。
/// 颜色位语义随编码模式而异（与 JS <c>!1</c>/<c>!0</c> 调用逐字对应）：
/// <list type="bullet">
///   <item>单图(RLE5X)：黑像素游程 → color=true(bit4=1)；白像素游程 → color=false(bit4=0)</item>
///   <item>差分(RLE5D)：差异游程 → color=true(bit4=1)；相同游程 → color=false(bit4=0)</item>
/// </list>
/// 8 个码 = 40 位 = 5 字节，按 MSB 优先写入字节流。</para>
/// <para><b>6 位编码</b>：码 = 索引(5 位) | 颜色(1 位)。4 个码 = 24 位 = 3 字节。</para>
/// </remarks>
public static class RleEncoder
{
    /// <summary>
    /// RLE5 游程长度查找表（对应 JS <c>_e</c>）。索引 0..14 共 15 项。
    /// 编码时从高索引向低索引查找第一个 ≤ 剩余计数的项。
    /// </summary>
    private static readonly int[] Rle5Runs =
        { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 24, 36, 48, 120 };

    /// <summary>
    /// RLE6 游程长度查找表（对应 JS <c>De</c>）。索引 0..31 共 32 项。
    /// </summary>
    private static readonly int[] Rle6Runs =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 41, 62, 83, 104, 125, 146, 167, 188, 209, 230, 461, 923
    };

    // ════════════════════════════════════════════════════════
    //  RLEC：经典 RLE
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 向 <paramref name="output"/> 写入一段 RLEC 编码。对应 JS <c>we.appendRLEC</c>。
    /// </summary>
    /// <param name="output">输出缓冲（须预置 0）。</param>
    /// <param name="pos">当前写入位置（引用传递，等价 JS <c>e.value</c>）。</param>
    /// <param name="value">重复的字节值。</param>
    /// <param name="count">重复次数。</param>
    /// <param name="maxSize">输出缓冲上限。</param>
    /// <returns>false 表示缓冲溢出。</returns>
    public static bool AppendRLEC(byte[] output, ref int pos, int value, int count, int maxSize)
    {
        // 计数 ≥ 63 时，先以 [255, value] 成对输出，每对表示 63 个重复
        while (count >= 63)
        {
            if (pos + 2 > maxSize) return false;
            output[pos++] = 0xFF;
            output[pos++] = (byte)value;
            count -= 63;
        }

        switch (count)
        {
            case 1:
                // 值 > 192 需用前缀字节 193 避免与长度标记冲突
                if (value > 192)
                {
                    if (pos + 2 > maxSize) return false;
                    output[pos++] = 193;
                    output[pos++] = (byte)value;
                }
                else
                {
                    if (pos + 1 > maxSize) return false;
                    output[pos++] = (byte)value;
                }
                break;

            case 2:
                if (pos + 2 > maxSize) return false;
                if (value > 192)
                {
                    output[pos++] = 194;
                    output[pos++] = (byte)value;
                }
                else
                {
                    output[pos++] = (byte)value;
                    output[pos++] = (byte)value;
                }
                break;

            default:
                // count ∈ [3, 62]：标记字节 = 192 | count，后跟值
                if (count > 0)
                {
                    if (pos + 2 > maxSize) return false;
                    output[pos++] = (byte)(192 | count);
                    output[pos++] = (byte)value;
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 计算并写入 RLEC 编码，返回输出长度（0 表示失败）。对应 JS <c>we.calcRLEC</c>。
    /// </summary>
    /// <param name="input">输入位图字节。</param>
    /// <param name="length">有效输入长度。</param>
    /// <param name="output">输出缓冲。</param>
    /// <param name="maxSize">输出缓冲上限。</param>
    public static int CalcRLEC(ReadOnlySpan<byte> input, int length, byte[] output, int maxSize)
    {
        if (length <= 0) return 0;
        var pos = 0;
        var runValue = input[0];
        var runCount = 1;

        for (var i = 1; i < length; i++)
        {
            if (input[i] == runValue) { runCount++; }
            else
            {
                if (!AppendRLEC(output, ref pos, runValue, runCount, maxSize)) return 0;
                runValue = input[i];
                runCount = 1;
            }
        }

        return AppendRLEC(output, ref pos, runValue, runCount, maxSize) ? pos : 0;
    }

    // ════════════════════════════════════════════════════════
    //  RLE5：5 位紧致编码
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 向 <paramref name="output"/> 写入一段 5 位 RLE 编码。对应 JS <c>we.appendRLE5</c>。
    /// </summary>
    /// <param name="output">输出缓冲（须预置 0）。</param>
    /// <param name="codeCount">已写入的码总数（引用传递，等价 JS <c>e.value</c>）。</param>
    /// <param name="color">颜色位：单图模式 false=白游程 / true=黑游程；差分模式 false=相同 / true=差异。对应 bit4。</param>
    /// <param name="count">本游程像素数。</param>
    /// <param name="maxSize">输出缓冲上限（字节）。</param>
    public static bool AppendRLE5(byte[] output, ref int codeCount, bool color, int count, int maxSize)
    {
        if (count <= 0) return true;

        // r = 当前码起始字节索引 = floor(5 * codeCount / 8)
        var r = 5 * codeCount / 8;
        var a = 15; // 从最大索引开始查找（15 为哨兵，_e[15] 不存在，自然衰减到 14）

        while (count > 0)
        {
            if (count >= Rle5Runs[a])
            {
                count -= Rle5Runs[a];
                // 5 位码：低 4 位 = 索引 a，bit4 = 颜色
                var o = a | (color ? 16 : 0);
                codeCount++;
                // 溢出检查：5 * codeCount 不能超过 8 * maxSize（即最多 floor(8*maxSize/5) 个码）
                if (5 * codeCount > 8 * maxSize) return false;

                // 按 (codeCount & 7) 决定 5 位码在字节流中的对齐方式（MSB 优先）
                switch (codeCount & 7)
                {
                    case 0: output[r] |= (byte)o; r++; break;
                    case 1: output[r] |= (byte)(o << 3); break;
                    case 2:
                        output[r] |= (byte)(o >> 2); r++;
                        output[r] |= (byte)((o & 3) << 6);
                        break;
                    case 3: output[r] |= (byte)(o << 1); break;
                    case 4:
                        output[r] |= (byte)(o >> 4); r++;
                        output[r] |= (byte)((o & 15) << 4);
                        break;
                    case 5:
                        output[r] |= (byte)(o >> 1); r++;
                        output[r] |= (byte)((o & 1) << 7);
                        break;
                    case 6: output[r] |= (byte)(o << 2); break;
                    case 7:
                        output[r] |= (byte)(o >> 3); r++;
                        output[r] |= (byte)((o & 7) << 5);
                        break;
                }
            }
            else
            {
                // 计数 ≤ 12 时直接定位到 count-1；否则衰减索引
                if (count <= 12) a = count - 1;
                else a--;
            }
        }

        return true;
    }

    /// <summary>
    /// 单图 5 位 RLE 编码。对应 JS <c>we.calcRLE5X</c>。
    /// 黑像素(bit=1)编码为黑游程，白像素(bit=0)编码为白游程。
    /// </summary>
    public static int CalcRLE5X(ReadOnlySpan<byte> input, int length, byte[] output, int maxSize)
    {
        if (length <= 0) return 0;
        var count = 0;
        var byteIdx = 0;
        var isBlack = false;   // 当前游程是否为黑
        var mask = 0x80;       // 位掩码，从 MSB 开始
        var codeCount = 0;

        while (true)
        {
            if ((input[byteIdx] & mask) != 0)
            {
                // 黑像素
                if (isBlack) count++;
                else
                {
                    if (!AppendRLE5(output, ref codeCount, false, count, maxSize)) return 0;
                    isBlack = true;
                    count = 1;
                }
            }
            else
            {
                // 白像素
                if (isBlack)
                {
                    if (!AppendRLE5(output, ref codeCount, true, count, maxSize)) return 0;
                    isBlack = false;
                    count = 1;
                }
                else count++;
            }

            if (mask == 1)
            {
                byteIdx++;
                if (byteIdx >= length) break;
                mask = 0x80;
            }
            else
            {
                mask >>= 1;
            }
        }

        // 收尾：最后一个黑游程
        return isBlack && !AppendRLE5(output, ref codeCount, true, count, maxSize) ? 0 : codeCount;
    }

    /// <summary>
    /// 差分 5 位 RLE 编码（与上一行异或）。对应 JS <c>we.calcRLE5D</c>。
    /// 像素与上一行<em>不同</em> → 差异游程(color=true)；<em>相同</em> → 相同游程(color=false)。
    /// </summary>
    /// <param name="cur">当前行。</param>
    /// <param name="curLen">当前行长度。</param>
    /// <param name="prev">上一行。</param>
    /// <param name="prevLen">上一行长度。</param>
    /// <param name="output">输出缓冲。</param>
    /// <param name="maxSize">输出缓冲上限。</param>
    public static int CalcRLE5D(ReadOnlySpan<byte> cur, int curLen,
                                ReadOnlySpan<byte> prev, int prevLen,
                                byte[] output, int maxSize)
    {
        var count = 0;
        var byteIdx = 0;
        var isDiff = false;   // 当前游程是否为"差异(黑)"
        var mask = 0x80;
        var codeCount = 0;
        var common = Math.Min(curLen, prevLen);

        // —— 阶段 1：两行共同长度部分，逐位比较 ——
        if (common > 0)
        {
            while (true)
            {
                if ((prev[byteIdx] & mask) != (cur[byteIdx] & mask))
                {
                    // 像素不同 → 黑游程
                    if (isDiff) count++;
                    else
                    {
                        if (!AppendRLE5(output, ref codeCount, false, count, maxSize)) return 0;
                        isDiff = true;
                        count = 1;
                    }
                }
                else
                {
                    // 像素相同 → 白游程
                    if (isDiff)
                    {
                        if (!AppendRLE5(output, ref codeCount, true, count, maxSize)) return 0;
                        isDiff = false;
                        count = 1;
                    }
                    else count++;
                }

                if (mask == 1)
                {
                    byteIdx++;
                    if (byteIdx >= common) break;
                    mask = 0x80;
                }
                else mask >>= 1;
            }
        }

        // —— 阶段 2：长度不等时，处理较长行的剩余位 ——
        // JS: e<s && (t=i, e=s) —— 若 prev 更长，令 t 指向 prev，使剩余位按"较长行"遍历
        if (curLen != prevLen)
        {
            ReadOnlySpan<byte> longer = cur;
            var longerLen = curLen;
            if (curLen < prevLen) { longer = prev; longerLen = prevLen; }

            mask = 0x80;
            while (true)
            {
                if ((longer[byteIdx] & mask) != 0)
                {
                    // 较长行该位为 1 → 与隐含的 0 不同 → 黑
                    if (isDiff) count++;
                    else
                    {
                        if (!AppendRLE5(output, ref codeCount, false, count, maxSize)) return 0;
                        isDiff = true;
                        count = 1;
                    }
                }
                else
                {
                    // 较长行该位为 0 → 与隐含 0 相同 → 白
                    if (isDiff)
                    {
                        if (!AppendRLE5(output, ref codeCount, true, count, maxSize)) return 0;
                        isDiff = false;
                        count = 1;
                    }
                    else count++;
                }

                if (mask == 1)
                {
                    byteIdx++;
                    if (byteIdx >= longerLen) break;
                    mask = 0x80;
                }
                else mask >>= 1;
            }
        }

        // 收尾
        return isDiff && !AppendRLE5(output, ref codeCount, true, count, maxSize) ? 0 : codeCount;
    }

    // ════════════════════════════════════════════════════════
    //  RLE6：6 位紧致编码
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// 向 <paramref name="output"/> 写入一段 6 位 RLE 编码。对应 JS <c>we.appendRLE6</c>。
    /// 6 位码 = 索引(5 位) | 颜色(bit5)。4 码 = 24 位 = 3 字节，MSB 优先。
    /// </summary>
    public static bool AppendRLE6(byte[] output, ref int codeCount, bool color, int count, int maxSize)
    {
        if (count <= 0) return true;

        var r = 6 * codeCount / 8;
        var a = 31; // 哨兵起始索引

        while (count > 0)
        {
            if (count >= Rle6Runs[a])
            {
                count -= Rle6Runs[a];
                var o = a | (color ? 32 : 0);
                codeCount++;
                if (6 * codeCount > 8 * maxSize) return false;

                switch (codeCount & 3)
                {
                    case 0: output[r] |= (byte)o; r++; break;
                    case 1: output[r] |= (byte)(o << 2); break;
                    case 2:
                        output[r] |= (byte)(o >> 4); r++;
                        output[r] |= (byte)((o & 15) << 4);
                        break;
                    case 3:
                        output[r] |= (byte)(o >> 2); r++;
                        output[r] |= (byte)((o & 3) << 6);
                        break;
                }
            }
            else
            {
                if (count <= 20) a = count - 1;
                else a--;
            }
        }

        return true;
    }

    /// <summary>
    /// 单图 6 位 RLE 编码。对应 JS <c>we.calcRLE6X</c>。
    /// </summary>
    public static int CalcRLE6X(ReadOnlySpan<byte> input, int length, byte[] output, int maxSize)
    {
        if (length <= 0) return 0;
        var count = 0;
        var byteIdx = 0;
        var isBlack = false;
        var mask = 0x80;
        var codeCount = 0;

        while (true)
        {
            if ((input[byteIdx] & mask) != 0)
            {
                if (isBlack) count++;
                else
                {
                    if (!AppendRLE6(output, ref codeCount, false, count, maxSize)) return 0;
                    isBlack = true;
                    count = 1;
                }
            }
            else
            {
                if (isBlack)
                {
                    if (!AppendRLE6(output, ref codeCount, true, count, maxSize)) return 0;
                    isBlack = false;
                    count = 1;
                }
                else count++;
            }

            if (mask == 1)
            {
                byteIdx++;
                if (byteIdx >= length) break;
                mask = 0x80;
            }
            else mask >>= 1;
        }

        return isBlack && !AppendRLE6(output, ref codeCount, true, count, maxSize) ? 0 : codeCount;
    }

    /// <summary>
    /// 差分 6 位 RLE 编码。对应 JS <c>we.calcRLE6D</c>。
    /// </summary>
    public static int CalcRLE6D(ReadOnlySpan<byte> cur, int curLen,
                                ReadOnlySpan<byte> prev, int prevLen,
                                byte[] output, int maxSize)
    {
        var count = 0;
        var byteIdx = 0;
        var isDiff = false;
        var mask = 0x80;
        var codeCount = 0;
        var common = Math.Min(curLen, prevLen);

        if (common > 0)
        {
            while (true)
            {
                if ((prev[byteIdx] & mask) != (cur[byteIdx] & mask))
                {
                    if (isDiff) count++;
                    else
                    {
                        if (!AppendRLE6(output, ref codeCount, false, count, maxSize)) return 0;
                        isDiff = true;
                        count = 1;
                    }
                }
                else
                {
                    if (isDiff)
                    {
                        if (!AppendRLE6(output, ref codeCount, true, count, maxSize)) return 0;
                        isDiff = false;
                        count = 1;
                    }
                    else count++;
                }

                if (mask == 1)
                {
                    byteIdx++;
                    if (byteIdx >= common) break;
                    mask = 0x80;
                }
                else mask >>= 1;
            }
        }

        if (curLen != prevLen)
        {
            ReadOnlySpan<byte> longer = cur;
            var longerLen = curLen;
            if (curLen < prevLen) { longer = prev; longerLen = prevLen; }

            mask = 0x80;
            while (true)
            {
                if ((longer[byteIdx] & mask) != 0)
                {
                    if (isDiff) count++;
                    else
                    {
                        if (!AppendRLE6(output, ref codeCount, false, count, maxSize)) return 0;
                        isDiff = true;
                        count = 1;
                    }
                }
                else
                {
                    if (isDiff)
                    {
                        if (!AppendRLE6(output, ref codeCount, true, count, maxSize)) return 0;
                        isDiff = false;
                        count = 1;
                    }
                    else count++;
                }

                if (mask == 1)
                {
                    byteIdx++;
                    if (byteIdx >= longerLen) break;
                    mask = 0x80;
                }
                else mask >>= 1;
            }
        }

        return isDiff && !AppendRLE6(output, ref codeCount, true, count, maxSize) ? 0 : codeCount;
    }
}

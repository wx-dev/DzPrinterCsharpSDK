namespace DzPrinter.Barcode;

/// <summary>
/// 二维条码创建请求。对应 JS SDK 中传入 <c>he.create(e)</c> 与 <c>ue.create2DBarcode(t)</c> 的 <c>e</c>/<c>t</c> 对象。
/// QR 码使用全部字段；PDF417/DataMatrix/GridMatrix 仅使用 Text/Content/BarcodeType。
/// </summary>
public sealed class Barcode2DRequest
{
    /// <summary>
    /// 文本内容。对应 JS <c>e.text</c>。
    /// JS 中可为字符串或数字（数字会通过 <c>String()</c> 转换）；为 null/undefined 时回退到 <see cref="Content"/>。
    /// </summary>
    public object? Text { get; set; }

    /// <summary>备用内容。对应 JS <c>e.content</c>。</summary>
    public object? Content { get; set; }

    /// <summary>二维码类型（如 "qrcode"/"pdf417"/"dataMatrix"/"gridMatrix"）。对应 JS <c>e.barcodeType</c> 或 <c>e.type</c>。</summary>
    public string? BarcodeType { get; set; }

    /// <summary>QR 纠错等级。对应 JS <c>e.eccLevel</c>，缺省为 <see cref="EccLevel.Middle"/>。</summary>
    public EccLevel? EccLevel { get; set; }

    /// <summary>QR 版本（1-40）。对应 JS <c>e.version</c>，0 或 null 表示自动选择。</summary>
    public int? Version { get; set; }

    /// <summary>QR 掩码图案（0-7）。对应 JS <c>e.qrMask</c>，0 或 null 表示自动选择。</summary>
    public int? QrMask { get; set; }

    /// <summary>SJIS 转换钩子。对应 JS <c>e.toSJISFunc</c>。</summary>
    public Func<char, int>? ToSjisFunc { get; set; }

    /// <summary>DT 水印模式（0-3）。对应 JS <c>e.waterMarkMode</c>，缺省为 4（表示不启用水印）。</summary>
    public int? WaterMarkMode { get; set; }

    /// <summary>DT 水印种子。对应 JS <c>e.waterMarkSeed</c>。</summary>
    public object? WaterMarkSeed { get; set; }
}

/// <summary>
/// QR 码矩阵构造器。对应 JS SDK 中 <c>he</c> 类。
/// 负责将分段数据组装为最终的 QR 码位矩阵，包含：
/// 探测图案、定时图案、对齐图案、版本信息、格式信息、数据填充、掩码应用。
/// </summary>
internal static class QrMatrix
{
    /// <summary>
    /// 设置探测图案（三个角的 7×7 方块）。对应 JS <c>he.setupFinderPattern(t, e)</c>。
    /// </summary>
    /// <param name="matrix">QR 矩阵。</param>
    /// <param name="version">版本号。</param>
    public static void SetupFinderPattern(BitMatrix matrix, int version)
    {
        var size = matrix.Cols;
        var positions = QrFinderPattern.GetPositions(version);
        for (var i = 0; i < positions.Length; i++)
        {
            var r0 = positions[i][0];  // JS: n (row offset)
            var c0 = positions[i][1];  // JS: r (col offset)
            for (var dr = -1; dr <= 7; dr++)
            {
                if (r0 + dr <= -1 || size <= r0 + dr) continue;
                for (var dc = -1; dc <= 7; dc++)
                {
                    if (c0 + dc <= -1 || size <= c0 + dc) continue;
                    // JS: e>=0&&e<=6&&(0===s||6===s) || s>=0&&s<=6&&(0===e||6===e) || e>=2&&e<=4&&s>=2&&s<=4
                    var isBlack = (dr >= 0 && dr <= 6 && (dc == 0 || dc == 6))
                              || (dc >= 0 && dc <= 6 && (dr == 0 || dr == 6))
                              || (dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4);
                    matrix.Set(r0 + dr, c0 + dc, isBlack, true);
                }
            }
        }
    }

    /// <summary>
    /// 设置定时图案（第 6 行与第 6 列的黑白交替线）。对应 JS <c>he.setupTimingPattern(t)</c>。
    /// </summary>
    public static void SetupTimingPattern(BitMatrix matrix)
    {
        var size = matrix.Cols;
        for (var i = 8; i < size - 8; i++)
        {
            var v = i % 2 == 0;
            matrix.Set(i, 6, v, true);
            matrix.Set(6, i, v, true);
        }
    }

    /// <summary>
    /// 设置对齐图案（版本 2+ 的 5×5 对齐方块）。对应 JS <c>he.setupAlignmentPattern(t, e)</c>。
    /// </summary>
    public static void SetupAlignmentPattern(BitMatrix matrix, int version)
    {
        var positions = QrAlignmentPattern.GetPositions(version);
        for (var i = 0; i < positions.Length; i++)
        {
            var r0 = positions[i][0];
            var c0 = positions[i][1];
            for (var dr = -2; dr <= 2; dr++)
            {
                for (var dc = -2; dc <= 2; dc++)
                {
                    // JS: -2===e || 2===e || -2===i || 2===i || 0===e && 0===i
                    var isBlack = dr == -2 || dr == 2 || dc == -2 || dc == 2 || (dr == 0 && dc == 0);
                    matrix.Set(r0 + dr, c0 + dc, isBlack, true);
                }
            }
        }
    }

    /// <summary>
    /// 设置版本信息（版本 7+ 的 18 位 BCH 编码）。对应 JS <c>he.setupVersionInfo(t, e)</c>。
    /// 版本信息分两块：6×3 块在左下角，3×6 块在右上角。
    /// </summary>
    public static void SetupVersionInfo(BitMatrix matrix, int version)
    {
        var size = matrix.Cols;
        var bits = QrVersionUtils.GetEncodedBits(version);
        for (var i = 0; i < 18; i++)
        {
            var row = i / 3;
            var col = i % 3 + size - 8 - 3;
            var bit = ((bits >> i) & 1) == 1;
            matrix.Set(row, col, bit, true);
            matrix.Set(col, row, bit, true);
        }
    }

    /// <summary>
    /// 设置格式信息（15 位 BCH 编码 + 暗模块）。对应 JS <c>he.setupFormatInfo(t, e, i)</c>。
    /// </summary>
    /// <param name="matrix">QR 矩阵。</param>
    /// <param name="eccLevel">纠错等级。</param>
    /// <param name="maskPattern">掩码图案编号（0-7）。</param>
    public static void SetupFormatInfo(BitMatrix matrix, EccLevel eccLevel, int maskPattern)
    {
        var size = matrix.Cols;
        var bits = QrFormatInfo.GetEncodedBits(eccLevel, maskPattern);
        for (var i = 0; i < 15; i++)
        {
            var r = ((bits >> i) & 1) == 1;
            // 垂直方向（col=8）
            if (i < 6) matrix.Set(i, 8, r, true);
            else if (i < 8) matrix.Set(i + 1, 8, r, true);
            else matrix.Set(size - 15 + i, 8, r, true);

            // 水平方向（row=8）
            if (i < 8) matrix.Set(8, size - i - 1, r, true);
            else if (i < 9) matrix.Set(8, 15 - i - 1 + 1, r, true);
            else matrix.Set(8, 15 - i - 1, r, true);
        }
        // 暗模块（始终为 1）
        matrix.Set(size - 8, 8, true, true);
    }

    /// <summary>
    /// 填充数据到矩阵。对应 JS <c>he.setupData(t, e)</c>。
    /// 算法：从右下角开始，按两列为一组 Zig-Zag 向上/向下扫描，跳过已保留的功能图形位置。
    /// </summary>
    public static void SetupData(BitMatrix matrix, byte[] data)
    {
        var size = matrix.Cols;
        var direction = -1;  // 初始向上
        var row = size - 1;
        var bitPos = 7;
        var byteIdx = 0;

        for (var col = size - 1; col > 0; col -= 2)
        {
            // JS: 6===o&&o-- —— 跳过第 6 列（定时图案列）
            if (col == 6) col--;

            while (true)
            {
                for (var i = 0; i < 2; i++)
                {
                    // 处理 (row, col - i) 两列
                    if (!matrix.IsReserved(row, col - i))
                    {
                        bool bit = false;
                        if (byteIdx < data.Length)
                            bit = ((data[byteIdx] >> bitPos) & 1) == 1;
                        matrix.Set(row, col - i, bit);
                        bitPos--;
                        if (bitPos == -1) { byteIdx++; bitPos = 7; }
                    }
                }
                row += direction;
                if (row < 0 || row >= size)
                {
                    row -= direction;
                    direction = -direction;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 创建数据码字序列。对应 JS <c>he.createData(t, e, i, s)</c>。
    /// 流程：写入各段模式指示+字符计数+数据 → 添加终止符 → 字节对齐 → 填充字节 → RS 纠错编码。
    /// </summary>
    /// <param name="version">版本号。</param>
    /// <param name="eccLevel">纠错等级。</param>
    /// <param name="buffer">已有缓冲区（可为 null，将创建新缓冲区；用于水印前置数据）。</param>
    /// <param name="segments">分段列表。</param>
    public static byte[]? CreateData(int version, EccLevel eccLevel, BitBuffer? buffer, IList<QrSegmentBase> segments)
    {
        buffer ??= new BitBuffer();

        // 写入各段：模式指示(4位) + 字符计数指示 + 数据
        foreach (var seg in segments)
        {
            buffer.Put(seg.Mode.Bit, 4);
            buffer.Put(seg.GetLength(), QrModeUtils.GetCharCountIndicator(seg.Mode, version));
            seg.Write(buffer);
        }

        // 总数据容量（位）
        var totalBits = 8 * (QrSymbolUtils.GetSymbolTotalCodewords(version) - QrBlocks.GetTotalCodewordsCount(version, eccLevel));

        // 终止符（4 位 0，若容量足够）
        if (buffer.GetLengthInBits() + 4 <= totalBits) buffer.Put(0, 4);

        // 字节对齐
        while (buffer.GetLengthInBits() % 8 != 0) buffer.PutBit(false);

        // 填充字节（交替 236 / 17）
        var padBytes = (totalBits - buffer.GetLengthInBits()) / 8;
        for (var i = 0; i < padBytes; i++)
            buffer.Put(i % 2 != 0 ? 17 : 236, 8);

        return CreateCodewords(buffer, version, eccLevel);
    }

    /// <summary>
    /// 生成 RS 纠错码字并交织数据。对应 JS <c>he.createCodewords(t, e, i)</c>。
    /// 算法：将数据分块 → 对每块计算 RS 纠错码 → 按列交织数据与纠错码。
    /// </summary>
    /// <param name="buffer">已填充的数据缓冲区。</param>
    /// <param name="version">版本号。</param>
    /// <param name="eccLevel">纠错等级。</param>
    public static byte[]? CreateCodewords(BitBuffer buffer, int version, EccLevel eccLevel)
    {
        var totalCodewords = QrSymbolUtils.GetSymbolTotalCodewords(version);
        var ecCodewords = QrBlocks.GetTotalCodewordsCount(version, eccLevel);
        if (ecCodewords == 0) return null;  // JS: if (!n) return;

        var dataCodewords = totalCodewords - ecCodewords;
        var blocksCount = QrBlocks.GetBlocksCount(version, eccLevel);
        if (blocksCount == 0) return null;  // JS: if (!a) return;

        // JS: o = a - s%a  —— 较短块的数量
        var shortBlocks = blocksCount - totalCodewords % blocksCount;
        // JS: c = Math.floor(s/a)  —— 每块码字数（含 EC）
        var codewordsPerBlock = totalCodewords / blocksCount;
        // JS: h = Math.floor(r/a)  —— 每块 EC 码字数
        var ecPerBlock = dataCodewords / blocksCount;
        // JS: d = h+1  —— 长块的数据码字数
        var longBlockData = ecPerBlock + 1;
        // JS: u = c-h  —— 短块的数据码字数
        var shortBlockData = codewordsPerBlock - ecPerBlock;

        var rsEncoder = new QrReedSolomonEncoder(shortBlockData);

        var blocks = new byte[blocksCount][];
        var ecBlocks = new byte[blocksCount][];
        var offset = 0;
        var maxDataLen = 0;

        // JS: P = new Uint8Array(t.buffer) —— 取底层字节
        var allBytes = buffer.Buffer.ToArray();

        for (var b = 0; b < blocksCount; b++)
        {
            // JS: e = t<o ? h : d  —— 前 o 块为短块（h 个数据码字），其余为长块（d 个）
            var dataLen = b < shortBlocks ? ecPerBlock : longBlockData;
            blocks[b] = new byte[dataLen];
            Array.Copy(allBytes, offset, blocks[b], 0, dataLen);
            ecBlocks[b] = rsEncoder.Encode(blocks[b]);
            offset += dataLen;
            if (dataLen > maxDataLen) maxDataLen = dataLen;
        }

        // 交织：先按列输出数据码字
        var result = new byte[totalCodewords];
        var idx = 0;
        for (var c = 0; c < maxDataLen; c++)
        {
            for (var b = 0; b < blocksCount; b++)
            {
                if (c < blocks[b].Length)
                    result[idx++] = blocks[b][c];
            }
        }
        // 再按列输出 EC 码字（每块 EC 长度相同 = shortBlockData）
        for (var c = 0; c < shortBlockData; c++)
        {
            for (var b = 0; b < blocksCount; b++)
                result[idx++] = ecBlocks[b][c];
        }
        return result;
    }

    /// <summary>
    /// QR 码主入口：根据请求创建 QR 矩阵。对应 JS <c>he.create(e)</c>。
    /// </summary>
    /// <returns>QR 码位矩阵；输入为空或数据过大时返回 null 或抛异常。</returns>
    public static BitMatrix? Create(Barcode2DRequest request)
    {
        // JS: let i = null===e.text||void 0===e.text ? e.content : e.text;
        var textObj = request.Text ?? request.Content;
        if (textObj == null) return null;
        var text = textObj.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return null;

        var eccLevel = request.EccLevel ?? EccLevel.Middle;
        var version = request.Version ?? 0;
        var qrMask = request.QrMask ?? 0;

        // JS: "function"==typeof e.toSJISFunc && (wt.toSJISFunction = e.toSJISFunc)
        if (request.ToSjisFunc != null)
            QrSymbolUtils.ToSjisFunction = request.ToSjisFunc;

        var buffer = new BitBuffer();

        // 水印处理
        var waterMarkMode = request.WaterMarkMode ?? 4;
        if (request.WaterMarkSeed != null && waterMarkMode >= 0)
        {
            // JS: o = o>3 ? 3&Date.now() : o
            if (waterMarkMode > 3)
                waterMarkMode = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 3);
            var checkSum = GetWaterSeedCheckSum(text, waterMarkMode, request.WaterMarkSeed);
            AppendDtWaterMark(waterMarkMode, checkSum, buffer);
        }

        // 版本选择
        var c = version;
        if (version <= 0)
        {
            var rawSegments = QrSegmentBuilder.RawSplit(text);
            c = QrVersionUtils.GetBestVersionForData(buffer.Length, rawSegments, eccLevel);
            if (c == 0) c = 0;  // JS: || 0
        }

        var segments = QrSegmentBuilder.FromString(text, c != 0 ? c : 40);
        var d = QrVersionUtils.GetBestVersionForData(buffer.Length, segments, eccLevel);
        if (d == 0)
            throw new InvalidOperationException("The amount of data is too big to be stored in a QR Code");

        if (version <= 0)
            version = d;
        else if (version < d)
            throw new InvalidOperationException(
                "\nThe chosen QR Code version cannot contain this amount of data.\n" +
                "Minimum version required to store current data is: " + d + ".\n");

        var data = CreateData(version, eccLevel, buffer, segments);
        if (data == null) return null;

        var size = QrSymbolUtils.GetSymbolSize(version);
        var matrix = new BitMatrix(size, size);
        SetupFinderPattern(matrix, version);
        SetupTimingPattern(matrix);
        SetupAlignmentPattern(matrix, version);
        SetupFormatInfo(matrix, eccLevel, 0);
        if (version >= 7) SetupVersionInfo(matrix, version);
        SetupData(matrix, data);

        // 掩码选择
        if (qrMask <= 0)
            qrMask = QrMaskPattern.GetBestMask(matrix, mask => SetupFormatInfo(matrix, eccLevel, mask));

        QrMaskPattern.ApplyMask(qrMask, matrix);
        SetupFormatInfo(matrix, eccLevel, qrMask);
        return matrix;
    }

    /// <summary>
    /// 计算水印种子校验和。对应 JS <c>he.getWaterSeedCheckSum(t, e, i)</c>。
    /// 算法与 <see cref="QrSymbolUtils.CalcDtCheckSum"/> 一致，但使用 <see cref="QrSymbolUtils.SDtWaterMarkCheckSums"/>。
    /// </summary>
    private static int GetWaterSeedCheckSum(string text, int waterMarkMode, object? seed)
    {
        // JS: const s = 3 & e
        var s = waterMarkMode & 3;
        // JS: let n = this.sDtWaterMarkCheckSums[s]
        var n = QrSymbolUtils.SDtWaterMarkCheckSums[s];
        // JS: n = n + (i = wt.calcWaterMarkSeed(i)) & 1048575
        n = (n + QrSymbolUtils.CalcWaterMarkSeed(seed)) & 1048575;
        // JS: const r = p.getBytes_Utf8(t)
        var bytes = TextEncodingUtils.GetBytesUtf8(text);
        for (var i = 0; i < bytes.Length; i++)
        {
            n += n >>> 5;
            n += (bytes[i] & 0xFF) * ((i & 2) != 0 ? 5 : 3);
            n += (i & 1) != 0 ? 13 : 11;
        }
        // JS: return n = n % 1019 + 3, n
        return n % 1019 + 3;
    }

    /// <summary>
    /// 追加 DT 水印到缓冲区。对应 JS <c>he.appendDtWaterMark(t, e, i)</c>。
    /// 将水印模式与校验和编码为结构化追加头部。
    /// </summary>
    private static void AppendDtWaterMark(int waterMarkMode, int checkSum, BitBuffer buffer)
    {
        // JS: e = 53248 | (3 & t) << 10 | 1023 & e
        checkSum = 53248 | (waterMarkMode & 3) << 10 | (1023 & checkSum);
        // JS: this.appendStructuredAppend(e >>> 8, 255 & e, i)
        AppendStructuredAppend(checkSum >> 8, checkSum & 0xFF, buffer);
    }

    /// <summary>
    /// 追加结构化追加头部。对应 JS <c>he.appendStructuredAppend(t, e, i)</c>。
    /// 写入结构化追加模式指示（4 位）+ 两个 8 位字节。
    /// </summary>
    private static void AppendStructuredAppend(int highByte, int lowByte, BitBuffer buffer)
    {
        AppendModeInfo(QrMode.Structured, buffer);
        buffer.Put(highByte, 8);
        buffer.Put(lowByte, 8);
    }

    /// <summary>
    /// 追加模式指示。对应 JS <c>he.appendModeInfo(t, e)</c>。
    /// </summary>
    private static void AppendModeInfo(QrMode mode, BitBuffer buffer) => buffer.Put(mode.Bit, 4);
}

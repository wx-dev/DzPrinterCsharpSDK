// =====================================================================
//  PrintPreviewDecoder：协议字节流 → 标签位图图像（PNG）。
//
//  解码器与 PrintEncoder 完全对称：
//    - 识别控制帧：PAGE_START / PAGE_WIDTH / GAP / DARKNESS / SPEED
//    - 识别位图帧：RAW PRINT / REPEAT / RLEC / RLE5X / RLE5D / RLE6X / RLE6D
//    - 识别走纸：ESC J n = [27, 74, n]
//    - 识别页结束：0x0C (form feed)
//    - 逐帧重建行位图 → 按序累积行 → 生成 PNG
//
//  关键常量（与 PrinterCommand 逐字节一致，为避免跨项目引用，此处硬编码）：
//    0x1F = 协议帧起始符 HostToDeviceDataStart
//    CMD_PAGE_START = 32, CMD_PAGE_WIDTH = 39
//    CMD_BITMAP_P_RLEC = 41, CMD_BITMAP_PRINT = 43, CMD_BITMAP_P_RLEX = 44
//    CMD_BITMAP_P_RLED = 45, CMD_BITMAP_REPEAT = 46, CMD_BITMAP_P_RLE6X = 60
//    CMD_BITMAP_P_RLE6D = 61
//    CRC 固定值 0x88（控制帧）
//    EBV (Extended Byte Value)：< 192 = 单字节，≥ 192 = 高 2 位为 11 头 + 14 位值
// =====================================================================

using DzPrinter.Imaging;
using SkiaSharp;

namespace DzPrinter.Transport.File;

/// <summary>
/// 打印协议预览解码器：将发送到打印机的字节流反向解码为标签图像。
/// </summary>
public static class PrintPreviewDecoder
{
    // ===== 协议常量（与 PrinterCommand/ProtocolConstants 一致，硬编码避免循环依赖）=====
    private const byte FRAME_START = 0x1F;
    private const byte CMD_PAGE_START = 32;
    private const byte CMD_PAGE_WIDTH = 39;
    private const byte CMD_BITMAP_RLEC = 41;
    private const byte CMD_BITMAP_PRINT = 43;
    private const byte CMD_BITMAP_RLE5X = 44;
    private const byte CMD_BITMAP_RLE5D = 45;
    private const byte CMD_BITMAP_REPEAT = 46;
    private const byte CMD_BITMAP_RLE6X = 60;
    private const byte CMD_BITMAP_RLE6D = 61;
    private const byte ESC = 27;
    private const byte CMD_ESC_J = 74;
    private const byte FORM_FEED = 0x0C;
    private const int EBV_THRESHOLD = 192;
    private const byte CONTROL_CRC = 0x88;

    // RLE 游程查找表（与 RleEncoder 完全一致）
    private static readonly int[] Rle5Runs =
        { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 24, 36, 48, 120 };
    private static readonly int[] Rle6Runs =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 41, 62, 83, 104, 125, 146, 167, 188, 209, 230, 461, 923
    };

    /// <summary>
    /// 解码结果。
    /// </summary>
    public sealed class DecodeResult
    {
        /// <summary>是否解码出至少一行位图。</summary>
        public bool Success { get; init; }
        /// <summary>解码出的标签图像（行列表）。行 = 字节数组（每字节 8 像素，MSB=左）。</summary>
        public List<byte[]> Rows { get; init; } = new();
        /// <summary>每行的字节宽度（由 CMD_PAGE_WIDTH 决定，0 = 未知）。</summary>
        public int ByteWidth { get; init; }
        /// <summary>像素宽度 = ByteWidth * 8。</summary>
        public int PixelWidth => ByteWidth * 8;
        /// <summary>像素高度 = Rows.Count。</summary>
        public int PixelHeight => Rows.Count;
        /// <summary>失败/警告消息集合。</summary>
        public List<string> Warnings { get; init; } = new();
    }

    /// <summary>
    /// 将协议字节流解码为标签行位图集合。
    /// </summary>
    /// <param name="data">完整字节流（含所有帧头）。</param>
    public static DecodeResult Decode(ReadOnlySpan<byte> data)
    {
        var rows = new List<byte[]>(512);
        byte[]? prevRow = null;          // 上一行（用于差分 RLE：RLE5D / RLE6D）
        byte[]? lastPrintRow = null;    // 最后一个 PRINT/RLE* 行（用于 REPEAT）
        var byteWidth = 0;
        var warnings = new List<string>();
        var i = 0;

        while (i < data.Length)
        {
            var b = data[i];

            // --- ESC J n：走纸 n 行（空行）---
            if (b == ESC && i + 2 < data.Length && data[i + 1] == CMD_ESC_J)
            {
                var n = data[i + 2];
                if (byteWidth == 0)
                {
                    warnings.Add($"ESC J {n} 在 PAGE_WIDTH 之前，暂存 0 宽空行");
                    byteWidth = GuessByteWidth(rows);
                }
                for (var k = 0; k < n; k++)
                    rows.Add(EmptyRow(byteWidth));
                i += 3;
                continue;
            }

            // --- 0x0C：页结束（FORM FEED），忽略 ---
            if (b == FORM_FEED)
            {
                i++;
                continue;
            }

            // --- 0x1F 协议帧 ---
            if (b == FRAME_START)
            {
                if (i + 2 >= data.Length) { warnings.Add("帧头截断"); break; }
                var cmd = data[i + 1];
                var p = i + 2; // 帧内解析游标

                int frameSize;
                try
                {
                    switch (cmd)
                    {
                        // ============================================================
                        //  控制帧：封装为 [0x1F][CMD][EBV(len)][payload len bytes][CRC=0x88]
                        //  通过 ProtocolPacket 生成，统一格式。
                        // ============================================================
                        case CMD_PAGE_START:
                        case CMD_PAGE_WIDTH:
                        case 66: // CMD_GAP_TYPE
                        case 67: // CMD_DARKNESS
                        case 68: // CMD_SPEED
                        case 69: // CMD_GAP_LEN
                        case 79: // CMD_COMMIT_PARAM
                        case 40: // CMD_PAGE_END
                        {
                            // 控制帧：统一格式 EBV(len) + payload + CRC
                            var (lenCtrl, lenEBVBytes) = ReadEbvWithLength(data, p, data.Length);
                            p += lenEBVBytes;
                            if (p + lenCtrl + 1 > data.Length) // +1 for CRC
                            {
                                warnings.Add($"控制帧 CMD={cmd} 数据截断（需要 {lenCtrl + 1} 字节 payload+CRC）");
                                i = data.Length;
                                continue;
                            }

                            if (cmd == CMD_PAGE_WIDTH)
                            {
                                byteWidth = ReadEbv(data.Slice(p, Math.Min(lenCtrl, data.Length - p)));
                                if (byteWidth <= 0 && lenCtrl >= 1)
                                {
                                    byteWidth = lenCtrl == 2
                                        ? ((data[p] & 0x3F) << 8) | data[p + 1]
                                        : data[p];
                                }
                            }
                            p += lenCtrl + 1; // skip payload + CRC
                            frameSize = p - i;
                        }
                            break;

                        // ============================================================
                        //  位图帧（Push2 拼接，无统一 EBV(payloadLen) 与 CRC）
                        //  各命令格式不同
                        // ============================================================
                        case CMD_BITMAP_PRINT: // 43 = RAW PRINT
                        {
                            // [0x1F][43][EBV(offset)][EBV(effLen)] lineData[effLen]
                            var (offset, ebvBytes1) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes1;
                            var (effLen, ebvBytes2) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes2;
                            if (p + effLen > data.Length)
                            {
                                warnings.Add($"RAW PRINT 截断（需要 {effLen} 字节 lineData）");
                                i = data.Length;
                                continue;
                            }
                            if (byteWidth == 0) byteWidth = GuessByteWidth(rows);
                            var line = NewRow(byteWidth);
                            var copyN = Math.Min(effLen, Math.Max(0, line.Length - offset));
                            if (copyN > 0) data.Slice(p, copyN).CopyTo(line.AsSpan(offset));
                            rows.Add(line);
                            prevRow = lastPrintRow = line;
                            p += effLen;
                            frameSize = p - i;
                        }
                            break;

                        case CMD_BITMAP_REPEAT: // 46 = REPEAT
                        {
                            // [0x1F][46][EBV(count-1)]
                            var (countMinus1, ebvBytes) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes;
                            var repeatCount = countMinus1 + 1;
                            if (lastPrintRow == null)
                            {
                                warnings.Add("REPEAT 命令在首个 PRINT 之前，忽略");
                            }
                            else
                            {
                                if (byteWidth == 0) byteWidth = lastPrintRow.Length;
                                for (var k = 0; k < repeatCount; k++)
                                {
                                    var dup = new byte[lastPrintRow.Length];
                                    Buffer.BlockCopy(lastPrintRow, 0, dup, 0, lastPrintRow.Length);
                                    rows.Add(dup);
                                }
                            }
                            frameSize = p - i;
                        }
                            break;

                        case CMD_BITMAP_RLEC: // 41
                        {
                            // [0x1F][41][EBV(rleLen)] rleData[rleLen]
                            var (rleLen, ebvBytes) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes;
                            if (p + rleLen > data.Length)
                            {
                                warnings.Add($"RLEC 截断（需要 {rleLen} 字节）");
                                i = data.Length;
                                continue;
                            }
                            if (byteWidth == 0) byteWidth = GuessByteWidth(rows);
                            var rleData = data.Slice(p, rleLen);
                            var r1 = DecodeRlec(rleData, byteWidth);
                            rows.Add(r1);
                            prevRow = lastPrintRow = r1;
                            p += rleLen;
                            frameSize = p - i;
                        }
                            break;

                        case CMD_BITMAP_RLE5X: // 44
                        case CMD_BITMAP_RLE5D: // 45
                        {
                            // [0x1F][CMD][EBV(codeCount)] packed[ceil(5*codeCount/8)]
                            var (codeCount, ebvBytes) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes;
                            var packedLen = (5 * codeCount + 7) / 8;
                            if (p + packedLen > data.Length)
                            {
                                warnings.Add($"RLE5X/D 截断（codeCount={codeCount} 需要 packedLen={packedLen} 字节）");
                                i = data.Length;
                                continue;
                            }
                            if (byteWidth == 0) byteWidth = GuessByteWidth(rows);
                            var packed = data.Slice(p, packedLen);
                            byte[] r2;
                            if (cmd == CMD_BITMAP_RLE5X)
                                r2 = DecodeRle5X(packed, codeCount, byteWidth);
                            else
                            {
                                var refR = prevRow ?? EmptyRow(byteWidth);
                                r2 = DecodeRle5D(packed, codeCount, refR, byteWidth);
                            }
                            rows.Add(r2);
                            prevRow = lastPrintRow = r2;
                            p += packedLen;
                            frameSize = p - i;
                        }
                            break;

                        case CMD_BITMAP_RLE6X: // 60
                        case CMD_BITMAP_RLE6D: // 61
                        {
                            // [0x1F][CMD][EBV(codeCount)] packed[ceil(6*codeCount/8)]
                            var (codeCount, ebvBytes) = ReadEbvWithLength(data, p, data.Length);
                            p += ebvBytes;
                            var packedLen = (6 * codeCount + 7) / 8;
                            if (p + packedLen > data.Length)
                            {
                                warnings.Add($"RLE6X/D 截断（codeCount={codeCount} 需要 packedLen={packedLen} 字节）");
                                i = data.Length;
                                continue;
                            }
                            if (byteWidth == 0) byteWidth = GuessByteWidth(rows);
                            var packed = data.Slice(p, packedLen);
                            byte[] r3;
                            if (cmd == CMD_BITMAP_RLE6X)
                                r3 = DecodeRle6X(packed, codeCount, byteWidth);
                            else
                            {
                                var refR = prevRow ?? EmptyRow(byteWidth);
                                r3 = DecodeRle6D(packed, codeCount, refR, byteWidth);
                            }
                            rows.Add(r3);
                            prevRow = lastPrintRow = r3;
                            p += packedLen;
                            frameSize = p - i;
                        }
                            break;

                        default:
                        {
                            // 未知 CMD：尝试按控制帧格式解析（EBV(len) + payload + CRC）以便跳过
                            if (data[i + 2] == 0)
                            {
                                // len=0：CRC 紧随，共 4 字节
                                frameSize = 4;
                            }
                            else if (data[i + 2] < EBV_THRESHOLD)
                            {
                                var skipLen = data[i + 2];
                                frameSize = 3 + skipLen + 1;
                            }
                            else if (i + 3 < data.Length)
                            {
                                var skipLen = ((data[i + 2] & 0x3F) << 8) | data[i + 3];
                                frameSize = 4 + skipLen + 1;
                            }
                            else
                            {
                                frameSize = data.Length - i;
                            }
                        }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"帧 CMD={cmd} 解析异常: {ex.Message}");
                    i++;
                    continue;
                }

                i += frameSize;
                continue;
            }

            // 未识别的字节：跳过
            warnings.Add($"偏移 {i}: 未识别字节 0x{b:X2}，跳过");
            i++;
        }

        return new DecodeResult
        {
            Success = rows.Count > 0,
            Rows = rows,
            ByteWidth = byteWidth,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// 将解码结果渲染为 PNG 并保存到文件。
    /// </summary>
    /// <param name="result">Decode() 返回的结果。</param>
    /// <param name="pngPath">输出 PNG 路径。</param>
    /// <param name="background">0=黑底白字，1=白底黑字（默认）。</param>
    /// <param name="scale">缩放倍数（整数），1 = 1:1，2 = 放大 2 倍便于预览。</param>
    public static void SavePng(DecodeResult result, string pngPath, int background = 1, int scale = 2)
    {
        if (result.Rows.Count == 0 || result.ByteWidth <= 0)
            throw new InvalidOperationException(
                "无有效行数据，无法生成 PNG。请先检查协议字节流是否包含位图帧。");

        var w = result.PixelWidth;
        var h = Math.Max(1, result.PixelHeight);
        var bg = background == 1 ? (byte)255 : (byte)0;  // 背景色：255 白 / 0 黑
        var fg = background == 1 ? (byte)0 : (byte)255;  // 前景色（打印点）

        // 先用 Gray8 字节数组在内存中组装像素行（Gray8），再拷贝到 SKBitmap
        var rowBytes = w; // Gray8 每像素 1 字节，对齐到 w
        var pixelBuffer = new byte[h * rowBytes];

        // 填充背景（字节值 = bg）
        for (var i = 0; i < pixelBuffer.Length; i++) pixelBuffer[i] = bg;

        // 逐行渲染：每字节 8 像素，MSB = 最左
        for (var y = 0; y < Math.Min(h, result.Rows.Count); y++)
        {
            var row = result.Rows[y];
            var rowStart = y * rowBytes;
            for (var b = 0; b < result.ByteWidth; b++)
            {
                if (b >= row.Length) break;
                var bits = row[b];
                for (var k = 0; k < 8; k++)
                {
                    var px = b * 8 + k;
                    if (px >= w) break;
                    if ((bits & (0x80 >> k)) != 0)
                        pixelBuffer[rowStart + px] = fg;
                }
            }
        }

        // 构建 SKBitmap（Gray8，Opaque）
        using var bmp = new SKBitmap();
        var info = new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelBuffer,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.InstallPixels(info, handle.AddrOfPinnedObject(), rowBytes, (addr, ctx) => handle.Free(), null);

            // 缩放或直接保存
            if (scale <= 1)
            {
                using var fs = System.IO.File.Create(pngPath);
                bmp.Encode(fs, SKEncodedImageFormat.Png, 100);
            }
            else
            {
                using var scaled = new SKBitmap(w * scale, h * scale, SKColorType.Gray8, SKAlphaType.Opaque);
                using (var canvas = new SKCanvas(scaled))
                using (var paint = new SKPaint())
                {
                    paint.IsAntialias = false;
                    paint.FilterQuality = SKFilterQuality.None; // 最近邻放大，避免模糊
                    canvas.Clear(new SKColor(bg, bg, bg));
                    canvas.DrawBitmap(bmp, SKRect.Create(w * scale, h * scale), paint);
                }
                using var fs = System.IO.File.Create(pngPath);
                scaled.Encode(fs, SKEncodedImageFormat.Png, 100);
            }
        }
        catch
        {
            if (handle.IsAllocated) handle.Free();
            throw;
        }
    }

    /// <summary>便捷方法：字节流 → 解码 → 保存 PNG，一条龙。</summary>
    public static DecodeResult DecodeAndSavePng(ReadOnlySpan<byte> data, string pngPath,
        int background = 1, int scale = 2)
    {
        var r = Decode(data);
        if (r.Rows.Count > 0) SavePng(r, pngPath, background, scale);
        return r;
    }

    /// <summary>解码结果 → DzImageData（供上层比较）。</summary>
    public static DzImageData ToDzImageData(DecodeResult result, int background = 1)
    {
        if (result.Rows.Count == 0 || result.ByteWidth <= 0)
            return default;

        var w = result.PixelWidth;
        var h = result.PixelHeight;
        var bg = background == 1 ? (byte)255 : (byte)0;
        var fg = background == 1 ? (byte)0 : (byte)255;

        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var row = result.Rows[y];
            for (var x = 0; x < w; x++)
            {
                var bit = x / 8 < row.Length ? (row[x / 8] & (0x80 >> (x & 7))) != 0 : false;
                var c = bit ? fg : bg;
                var idx = 4 * (y * w + x);
                rgba[idx] = c;     // R
                rgba[idx + 1] = c; // G
                rgba[idx + 2] = c; // B
                rgba[idx + 3] = 0xFF; // A
            }
        }
        return new DzImageData(w, h, rgba);
    }

    // =====================================================
    //  内部辅助
    // =====================================================

    private static int ReadEbv(ReadOnlySpan<byte> s)
    {
        if (s.Length == 0) return 0;
        if (s[0] < EBV_THRESHOLD) return s[0];
        return s.Length < 2 ? 0 : ((s[0] & 0x3F) << 8) | s[1];
    }

    /// <summary>读取 EBV 并返回 (值, 消费字节数 1或2)。越界则返回 (0, 1)。</summary>
    private static (int Value, int Bytes) ReadEbvWithLength(ReadOnlySpan<byte> s, int offset, int spanLength)
    {
        if (offset >= spanLength) return (0, 1);
        if (s[offset] < EBV_THRESHOLD) return (s[offset], 1);
        if (offset + 1 >= spanLength) return (0, 1);
        return (((s[offset] & 0x3F) << 8) | s[offset + 1], 2);
    }

    // 解析 [EBV(offset), EBV(effLen)]，返回 (offset, effLen, 消费的字节数)
    private static (int offset, int effLen, int bytes) ParseTwoEbv(ReadOnlySpan<byte> s, int start, int total)
    {
        var i = 0;
        if (i >= total) return (0, 0, i);
        var firstIsLong = s[start + i] >= EBV_THRESHOLD;
        var v1 = firstIsLong
            ? (i + 1 < total ? (((s[start + i] & 0x3F) << 8) | s[start + i + 1]) : 0)
            : s[start + i];
        i += firstIsLong ? 2 : 1;

        if (i >= total) return (v1, 0, i);
        var secondIsLong = s[start + i] >= EBV_THRESHOLD;
        var v2 = secondIsLong
            ? (i + 1 < total ? (((s[start + i] & 0x3F) << 8) | s[start + i + 1]) : 0)
            : s[start + i];
        i += secondIsLong ? 2 : 1;
        return (v1, v2, i);
    }

    private static int GuessByteWidth(List<byte[]> rows) => rows.Count > 0 ? rows[0].Length : 40;

    private static byte[] EmptyRow(int byteWidth)
    {
        var r = new byte[byteWidth];
        Array.Clear(r, 0, r.Length);
        return r;
    }

    private static byte[] NewRow(int byteWidth)
    {
        var r = new byte[byteWidth];
        Array.Clear(r, 0, r.Length);
        return r;
    }

    // ==================================================================
    //  RLE 解压：AppendRLE5 / AppendRLE6 / AppendRLEC 的反向操作
    // ==================================================================

    // --- RLEC 反解 ---
    private static byte[] DecodeRlec(ReadOnlySpan<byte> rle, int byteWidth)
    {
        var result = new List<byte>(byteWidth);
        var i = 0;
        while (i < rle.Length && result.Count < byteWidth)
        {
            var b = rle[i];
            if (b == 0xFF)
            {
                // 63 个重复
                var val = i + 1 < rle.Length ? rle[i + 1] : (byte)0;
                for (var k = 0; k < 63 && result.Count < byteWidth; k++) result.Add(val);
                i += 2;
            }
            else if (b >= EBV_THRESHOLD) // 192..254
            {
                // count = b - 192 (2..62); 下一字节 = value.
                // 但注意单字节 value > 192 的情形：193 + value → 1 个 value
                if (b == 193)
                {
                    var val = i + 1 < rle.Length ? rle[i + 1] : (byte)0;
                    result.Add(val);
                    i += 2;
                }
                else if (b == 194)
                {
                    // count = 2, value > 192 → 实际 2 个 value
                    var val = i + 1 < rle.Length ? rle[i + 1] : (byte)0;
                    result.Add(val);
                    if (result.Count < byteWidth) result.Add(val);
                    i += 2;
                }
                else
                {
                    // count = b - 192 (2..62)
                    var count = b - 192;
                    var val = i + 1 < rle.Length ? rle[i + 1] : (byte)0;
                    for (var k = 0; k < count && result.Count < byteWidth; k++) result.Add(val);
                    i += 2;
                }
            }
            else
            {
                // 原始值，1 个字节
                result.Add(b);
                i++;
            }
        }

        // 长度不足补 0，过多截断
        if (result.Count == byteWidth) return result.ToArray();
        var arr = new byte[byteWidth];
        var n = Math.Min(result.Count, byteWidth);
        for (var k = 0; k < n; k++) arr[k] = result[k];
        return arr;
    }

    // --- RLE5X 反解：单图 5 位紧致编码 → 位图行字节 ---
    private static byte[] DecodeRle5X(ReadOnlySpan<byte> packed, int codeCount, int byteWidth)
    {
        // 从 packed 中按 MSB 优先依次提取 5 位码
        var bits = new bool[codeCount * 5];
        ExtractPackedBits(packed, codeCount, 5, bits);

        // 逐码展开
        var pixelRunLengths = new List<int>();
        var pixelRunColors = new List<bool>();
        var totalPixels = 0;

        for (var c = 0; c < codeCount; c++)
        {
            var code5 = Read5bitCode(bits, c);
            var idx = code5 & 0x0F;       // 低 4 位 = 游程索引
            var color = (code5 & 0x10) != 0; // bit4：单图模式 color=1=黑, 0=白
            if (idx >= Rle5Runs.Length) idx = Rle5Runs.Length - 1;
            var len = Rle5Runs[idx];
            pixelRunLengths.Add(len);
            pixelRunColors.Add(color);
            totalPixels += len;
        }

        return PackRunsToBytes(pixelRunLengths, pixelRunColors, byteWidth);
    }

    // --- RLE5D 反解：差分 5 位紧致编码 + 上一行参考 → 位图行字节 ---
    private static byte[] DecodeRle5D(ReadOnlySpan<byte> packed, int codeCount,
                                      byte[] refRow, int byteWidth)
    {
        var totalBits = codeCount * 5;
        var bits = new bool[totalBits];
        ExtractPackedBits(packed, codeCount, 5, bits);

        var runLens = new List<int>();
        var runDiff = new List<bool>(); // true = 与上一行不同（反转）；false = 相同
        for (var c = 0; c < codeCount; c++)
        {
            var code5 = Read5bitCode(bits, c);
            var idx = code5 & 0x0F;
            var diff = (code5 & 0x10) != 0;
            if (idx >= Rle5Runs.Length) idx = Rle5Runs.Length - 1;
            runLens.Add(Rle5Runs[idx]);
            runDiff.Add(diff);
        }

        return PackRunsToBytesDiff(runLens, runDiff, refRow, byteWidth);
    }

    // --- RLE6X 反解 ---
    private static byte[] DecodeRle6X(ReadOnlySpan<byte> packed, int codeCount, int byteWidth)
    {
        var bits = new bool[codeCount * 6];
        ExtractPackedBits(packed, codeCount, 6, bits);

        var lens = new List<int>();
        var cols = new List<bool>();
        for (var c = 0; c < codeCount; c++)
        {
            var code6 = Read6bitCode(bits, c);
            var idx = code6 & 0x1F;
            var color = (code6 & 0x20) != 0;
            if (idx >= Rle6Runs.Length) idx = Rle6Runs.Length - 1;
            lens.Add(Rle6Runs[idx]);
            cols.Add(color);
        }
        return PackRunsToBytes(lens, cols, byteWidth);
    }

    // --- RLE6D 反解 ---
    private static byte[] DecodeRle6D(ReadOnlySpan<byte> packed, int codeCount,
                                      byte[] refRow, int byteWidth)
    {
        var bits = new bool[codeCount * 6];
        ExtractPackedBits(packed, codeCount, 6, bits);

        var lens = new List<int>();
        var diffs = new List<bool>();
        for (var c = 0; c < codeCount; c++)
        {
            var code6 = Read6bitCode(bits, c);
            var idx = code6 & 0x1F;
            var diff = (code6 & 0x20) != 0;
            if (idx >= Rle6Runs.Length) idx = Rle6Runs.Length - 1;
            lens.Add(Rle6Runs[idx]);
            diffs.Add(diff);
        }
        return PackRunsToBytesDiff(lens, diffs, refRow, byteWidth);
    }

    // --- 提取打包位：按 MSB 优先的顺序把每个码的 bitPerCode 位提取到 bits[] ---
    private static void ExtractPackedBits(ReadOnlySpan<byte> packed, int codeCount, int bitPerCode, bool[] bits)
    {
        var total = codeCount * bitPerCode;
        for (var k = 0; k < total; k++)
        {
            var byteIdx = k >> 3; // k / 8
            var bitIdx = k & 7;   // k % 8
            if (byteIdx >= packed.Length) break;
            // MSB 优先：bitIdx = 0 → b7, bitIdx=7 → b0
            bits[k] = (packed[byteIdx] & (0x80 >> bitIdx)) != 0;
        }
    }

    private static int Read5bitCode(bool[] bits, int codeIdx)
    {
        var v = 0;
        var start = codeIdx * 5;
        for (var k = 0; k < 5; k++)
        {
            v <<= 1;
            if (bits[start + k]) v |= 1;
        }
        return v;
    }

    private static int Read6bitCode(bool[] bits, int codeIdx)
    {
        var v = 0;
        var start = codeIdx * 6;
        for (var k = 0; k < 6; k++)
        {
            v <<= 1;
            if (bits[start + k]) v |= 1;
        }
        return v;
    }

    // --- 将 (len, color) 游程序列打包为 byteWidth 个字节的行位图 ---
    // color=true=黑像素(bit=1), false=白像素(bit=0)
    private static byte[] PackRunsToBytes(List<int> lens, List<bool> cols, int byteWidth)
    {
        var row = new byte[byteWidth];
        var px = 0;
        for (var r = 0; r < lens.Count; r++)
        {
            var len = lens[r];
            var col = cols[r];
            for (var k = 0; k < len && px < byteWidth * 8; k++, px++)
            {
                if (!col) continue; // 白像素 = 0，已经默认
                var byteIdx = px >> 3;
                var bitIdx = px & 7;
                row[byteIdx] |= (byte)(0x80 >> bitIdx);
            }
        }
        return row;
    }

    // --- 差分游程 → 当前行：与 refRow 对应位比较，diff=true 的位置反转 ---
    private static byte[] PackRunsToBytesDiff(List<int> lens, List<bool> diffs, byte[] refRow, int byteWidth)
    {
        // 第一步：把 (len, diff) 序列展开为每个像素的"是否不同"数组（总像素 byteWidth*8）
        var totalPixels = byteWidth * 8;
        var px = 0;
        var row = new byte[byteWidth];
        for (var r = 0; r < lens.Count; r++)
        {
            var len = lens[r];
            var diff = diffs[r];
            for (var k = 0; k < len && px < totalPixels; k++, px++)
            {
                var byteIdx = px >> 3;
                var bitIdx = px & 7;
                var mask = (byte)(0x80 >> bitIdx);

                // 从 refRow 取原始位
                bool origBit = byteIdx < refRow.Length && (refRow[byteIdx] & mask) != 0;
                bool finalBit = diff ? !origBit : origBit;
                if (finalBit) row[byteIdx] |= mask;
            }
        }
        // 剩余像素继承 refRow
        for (; px < totalPixels; px++)
        {
            var byteIdx = px >> 3;
            if (byteIdx >= refRow.Length) break;
            var bitIdx = px & 7;
            var mask = (byte)(0x80 >> bitIdx);
            if ((refRow[byteIdx] & mask) != 0) row[byteIdx] |= mask;
        }
        return row;
    }
}

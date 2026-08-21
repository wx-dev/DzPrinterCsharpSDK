using DzPrinter.Core;

namespace DzPrinter.Barcode;

/// <summary>
/// Data Matrix (ECC 200) 二维条码编码器。完全基于 ZXing 实现 (ISO/IEC 16022)。
/// 支持 ASCII 编码模式（含数字压缩）、Reed-Solomon 纠错、Utah 蛇形模块放置。
/// </summary>
public sealed class DataMatrixEncoder : IBarcode2DEncoder
{
    // ==================== 符号尺寸表 ====================

    private readonly struct SymbolInfo
    {
        public readonly bool Rectangular;
        public readonly int DataCapacity;
        public readonly int ErrorCodewords;
        public readonly int MatrixWidth;   // 单个数据区域宽度
        public readonly int MatrixHeight;  // 单个数据区域高度
        public readonly int DataRegions;   // 数据区域数
        public readonly int RsBlockData;   // 每个 RS 块数据码字数
        public readonly int RsBlockError;  // 每个 RS 块纠错码字数

        public SymbolInfo(bool rectangular, int dataCapacity, int errorCodewords,
            int matrixWidth, int matrixHeight, int dataRegions,
            int rsBlockData, int rsBlockError)
        {
            Rectangular = rectangular;
            DataCapacity = dataCapacity;
            ErrorCodewords = errorCodewords;
            MatrixWidth = matrixWidth;
            MatrixHeight = matrixHeight;
            DataRegions = dataRegions;
            RsBlockData = rsBlockData;
            RsBlockError = rsBlockError;
        }

        public int HorizontalDataRegions => DataRegions switch
        {
            1 => 1,
            2 or 4 => 2,
            16 => 4,
            36 => 6,
            _ => 1,
        };

        public int VerticalDataRegions => DataRegions switch
        {
            1 or 2 => 1,
            4 => 2,
            16 => 4,
            36 => 6,
            _ => 1,
        };

        public int SymbolDataWidth => HorizontalDataRegions * MatrixWidth;
        public int SymbolDataHeight => VerticalDataRegions * MatrixHeight;
        public int SymbolWidth => SymbolDataWidth + HorizontalDataRegions * 2;
        public int SymbolHeight => SymbolDataHeight + VerticalDataRegions * 2;
        public int InterleavedBlockCount => DataCapacity / RsBlockData;
    }

    private static readonly SymbolInfo[] ProdSymbols =
    {
        new(false, 3, 5, 8, 8, 1, 3, 5),
        new(false, 5, 7, 10, 10, 1, 5, 7),
        new(true, 5, 7, 16, 6, 1, 5, 7),
        new(false, 8, 10, 12, 12, 1, 8, 10),
        new(true, 10, 11, 14, 6, 2, 10, 11),
        new(false, 12, 12, 14, 14, 1, 12, 12),
        new(true, 16, 14, 24, 10, 1, 16, 14),
        new(false, 18, 14, 16, 16, 1, 18, 14),
        new(false, 22, 18, 18, 18, 1, 22, 18),
        new(true, 22, 18, 16, 10, 2, 22, 18),
        new(false, 30, 20, 20, 20, 1, 30, 20),
        new(true, 32, 24, 16, 14, 2, 32, 24),
        new(false, 36, 24, 22, 22, 1, 36, 24),
        new(false, 44, 28, 24, 24, 1, 44, 28),
        new(true, 49, 28, 22, 14, 2, 49, 28),
        new(false, 62, 36, 14, 14, 4, 62, 36),
        new(false, 86, 42, 16, 16, 4, 86, 42),
        new(false, 114, 48, 18, 18, 4, 114, 48),
        new(false, 144, 56, 20, 20, 4, 144, 56),
        new(false, 174, 68, 22, 22, 4, 174, 68),
        new(false, 204, 84, 24, 24, 4, 102, 42),
        new(false, 280, 112, 14, 14, 16, 140, 56),
        new(false, 368, 144, 16, 16, 16, 92, 36),
        new(false, 456, 192, 18, 18, 16, 114, 48),
        new(false, 576, 224, 20, 20, 16, 144, 56),
        new(false, 696, 272, 22, 22, 16, 174, 68),
        new(false, 816, 336, 24, 24, 16, 136, 56),
        new(false, 1050, 408, 18, 18, 36, 175, 68),
        new(false, 1304, 496, 20, 20, 36, 163, 62),
    };

    // ==================== GF(256) 表 ====================
    // 本原多项式: x^8 + x^5 + x^3 + x^2 + 1 = 0x12D (Data Matrix 专用，不同于 QR 的 0x11D)

    private static readonly int[] ALog = new int[255];
    private static readonly int[] Log = new int[256];

    static DataMatrixEncoder()
    {
        var p = 1;
        for (var i = 0; i < 255; i++)
        {
            ALog[i] = p;
            Log[p] = i;
            p <<= 1;
            if (p >= 256) p ^= 0x12D;
        }
    }

    private static int GfMul(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return ALog[(Log[a] + Log[b]) % 255];
    }

    // ==================== RS 纠错因子表 ====================

    private static readonly int[] FactorSets = { 5, 7, 10, 11, 12, 14, 18, 20, 24, 28, 36, 42, 48, 56, 62, 68 };

    private static readonly byte[][] Factors =
    {
        new byte[] {228, 48, 15, 111, 62},
        new byte[] {23, 68, 144, 134, 240, 92, 254},
        new byte[] {28, 24, 185, 166, 223, 248, 116, 255, 110, 61},
        new byte[] {175, 138, 205, 12, 194, 168, 39, 245, 60, 97, 120},
        new byte[] {41, 153, 158, 91, 61, 42, 142, 213, 97, 178, 100, 242},
        new byte[] {156, 97, 192, 252, 95, 9, 157, 119, 138, 45, 18, 186, 83, 185},
        new byte[] {83, 195, 100, 39, 188, 75, 66, 61, 241, 213, 109, 129, 94, 254, 225, 48, 90, 188},
        new byte[] {15, 195, 244, 9, 233, 71, 168, 2, 188, 160, 153, 145, 253, 79, 108, 82, 27, 174, 186, 172},
        new byte[] {52, 190, 88, 205, 109, 39, 176, 21, 155, 197, 251, 223, 155, 21, 5, 172,
            254, 124, 12, 181, 184, 96, 50, 193},
        new byte[] {211, 231, 43, 97, 71, 96, 103, 174, 37, 151, 170, 53, 75, 34, 249, 121,
            17, 138, 110, 213, 141, 136, 120, 151, 233, 168, 93, 255},
        new byte[] {245, 127, 242, 218, 130, 250, 162, 181, 102, 120, 84, 179, 220, 251, 80, 182,
            229, 18, 2, 4, 68, 33, 101, 137, 95, 119, 115, 44, 175, 184, 59, 25,
            225, 98, 81, 112},
        new byte[] {77, 193, 137, 31, 19, 38, 22, 153, 247, 105, 122, 2, 245, 133, 242, 8,
            175, 95, 100, 9, 167, 105, 214, 111, 57, 121, 21, 1, 253, 57, 54, 101,
            248, 202, 69, 50, 150, 177, 226, 5, 9, 5},
        new byte[] {245, 132, 172, 223, 96, 32, 117, 22, 238, 133, 238, 231, 205, 188, 237, 87,
            191, 106, 16, 147, 118, 23, 37, 90, 170, 205, 131, 88, 120, 100, 66, 138,
            186, 240, 82, 44, 176, 87, 187, 147, 160, 175, 69, 213, 92, 253, 225, 19},
        new byte[] {175, 9, 223, 238, 12, 17, 220, 208, 100, 29, 175, 170, 230, 192, 215, 235,
            150, 159, 36, 223, 38, 200, 132, 54, 228, 146, 218, 234, 117, 203, 29, 232,
            144, 238, 22, 150, 201, 117, 62, 207, 164, 13, 137, 245, 127, 67, 247, 28,
            155, 43, 203, 107, 233, 53, 143, 46},
        new byte[] {242, 93, 169, 50, 144, 210, 39, 118, 202, 188, 201, 189, 143, 108, 196, 37,
            185, 112, 134, 230, 245, 63, 197, 190, 250, 106, 185, 221, 175, 64, 114, 71,
            161, 44, 147, 6, 27, 218, 51, 63, 87, 10, 40, 130, 188, 17, 163, 31,
            176, 170, 4, 107, 232, 7, 94, 166, 224, 124, 86, 47, 11, 204},
        new byte[] {220, 228, 173, 89, 251, 149, 159, 56, 89, 33, 147, 244, 154, 36, 73, 127,
            213, 136, 248, 180, 234, 197, 158, 177, 68, 122, 93, 213, 15, 160, 227, 236,
            66, 139, 153, 185, 202, 167, 179, 25, 220, 232, 96, 210, 231, 136, 223, 239,
            181, 241, 59, 52, 172, 25, 49, 232, 211, 189, 64, 54, 108, 153, 132, 63,
            96, 103, 82, 186},
    };

    // ==================== 公共 API ====================

    public BitMatrix? Encode(Barcode2DRequest request)
    {
        var text = request.Text?.ToString() ?? "";
        if (string.IsNullOrEmpty(text))
            text = request.Content?.ToString() ?? "";
        if (string.IsNullOrEmpty(text)) return null;

        // Step 1: ASCII 编码
        var dataCodewords = EncodeAscii(text);

        // Step 2: 选择符号尺寸
        var symbol = LookupSymbol(dataCodewords.Length);

        // Step 3: 填充数据码字
        var padded = PadCodewords(dataCodewords, symbol.DataCapacity);

        // Step 4: 生成纠错码字
        var allCodewords = EncodeEcc200(padded, symbol);

        // Step 5: 模块放置（Utah 蛇形算法）
        var placement = PlaceModules(allCodewords, symbol.SymbolDataWidth, symbol.SymbolDataHeight);

        // Step 6: 绘制最终矩阵（定位图案 + 时钟轨道 + 数据）
        return EncodeLowLevel(placement, symbol);
    }

    // ==================== ASCII 编码 ====================

    private static byte[] EncodeAscii(string text)
    {
        var result = new List<byte>();
        var i = 0;
        while (i < text.Length)
        {
            // 检查连续数字（两位数字压缩为一个码字 +130）
            if (i + 1 < text.Length && char.IsDigit(text[i]) && char.IsDigit(text[i + 1]))
            {
                var num = (text[i] - '0') * 10 + (text[i + 1] - '0');
                result.Add((byte)(num + 130));
                i += 2;
            }
            else
            {
                var ch = text[i];
                if (ch <= 127)
                    result.Add((byte)(ch + 1));
                else
                {
                    // 扩展 ASCII: Upper Shift (235) + (ch - 128 + 1)
                    result.Add(235);
                    result.Add((byte)(ch - 127));
                }
                i++;
            }
        }
        return result.ToArray();
    }

    private static byte[] PadCodewords(byte[] data, int capacity)
    {
        var result = new byte[capacity];
        Array.Copy(data, 0, result, 0, Math.Min(data.Length, capacity));

        if (data.Length < capacity)
        {
            result[data.Length] = 129; // PAD
            for (var i = data.Length + 1; i < capacity; i++)
            {
                var pseudoRandom = ((149 * (i + 1)) % 253) + 1;
                var temp = 129 + pseudoRandom;
                result[i] = (byte)(temp <= 254 ? temp : temp - 254);
            }
        }
        return result;
    }

    // ==================== 符号查找 ====================

    private static SymbolInfo LookupSymbol(int dataCodewords)
    {
        foreach (var s in ProdSymbols)
        {
            if (!s.Rectangular && dataCodewords <= s.DataCapacity)
                return s;
        }
        return ProdSymbols[ProdSymbols.Length - 1];
    }

    // ==================== Reed-Solomon 纠错 ====================

    private static byte[] EncodeEcc200(byte[] data, SymbolInfo symbol)
    {
        var blockCount = symbol.InterleavedBlockCount;
        var total = symbol.DataCapacity + symbol.ErrorCodewords;
        var result = new byte[total];
        Array.Copy(data, 0, result, 0, symbol.DataCapacity);

        if (blockCount == 1)
        {
            var ecc = CreateEccBlock(data, symbol.ErrorCodewords);
            Array.Copy(ecc, 0, result, symbol.DataCapacity, ecc.Length);
        }
        else
        {
            for (var block = 0; block < blockCount; block++)
            {
                // 按交错方式取出该块的数据
                var blockData = new byte[symbol.RsBlockData];
                var idx = 0;
                for (var d = block; d < symbol.DataCapacity; d += blockCount)
                    blockData[idx++] = data[d];

                var ecc = CreateEccBlock(blockData, symbol.RsBlockError);

                // 按交错方式写入纠错码字
                var pos = 0;
                for (var e = block; e < symbol.RsBlockError * blockCount; e += blockCount)
                    result[symbol.DataCapacity + e] = ecc[pos++];
            }
        }
        return result;
    }

    private static byte[] CreateEccBlock(byte[] data, int numECWords)
    {
        // 查找因子表
        var table = -1;
        for (var i = 0; i < FactorSets.Length; i++)
        {
            if (FactorSets[i] == numECWords)
            {
                table = i;
                break;
            }
        }

        var poly = Factors[table];
        var ecc = new byte[numECWords];

        // 反馈移位寄存器算法
        for (var i = 0; i < data.Length; i++)
        {
            var m = (byte)(ecc[numECWords - 1] ^ data[i]);
            for (var k = numECWords - 1; k > 0; k--)
            {
                if (m != 0 && poly[k] != 0)
                    ecc[k] = (byte)(ecc[k - 1] ^ GfMul(m, poly[k]));
                else
                    ecc[k] = ecc[k - 1];
            }
            if (m != 0 && poly[0] != 0)
                ecc[0] = (byte)GfMul(m, poly[0]);
            else
                ecc[0] = 0;
        }

        // 反转
        var result = new byte[numECWords];
        for (var i = 0; i < numECWords; i++)
            result[i] = ecc[numECWords - 1 - i];
        return result;
    }

    // ==================== Utah 蛇形模块放置 ====================

    private static byte[] PlaceModules(byte[] codewords, int numcols, int numrows)
    {
        var bits = new byte[numcols * numrows];
        Array.Fill(bits, (byte)255); // 255 = 未设置

        int pos = 0;
        int row = 4;
        int col = 0;

        do
        {
            // 特殊角
            if (row == numrows && col == 0)
                PlaceCorner1(bits, codewords, numcols, numrows, pos++);
            if (row == numrows - 2 && col == 0 && numcols % 4 != 0)
                PlaceCorner2(bits, codewords, numcols, numrows, pos++);
            if (row == numrows - 2 && col == 0 && numcols % 8 == 4)
                PlaceCorner3(bits, codewords, numcols, numrows, pos++);
            if (row == numrows + 4 && col == 2 && numcols % 8 == 0)
                PlaceCorner4(bits, codewords, numcols, numrows, pos++);

            // 向上斜扫
            do
            {
                if (row < numrows && col >= 0 && bits[row * numcols + col] == 255)
                    PlaceUtah(bits, codewords, numcols, numrows, row, col, pos++);
                row -= 2;
                col += 2;
            } while (row >= 0 && col < numcols);

            row++;
            col += 3;

            // 向下斜扫
            do
            {
                if (row >= 0 && col < numcols && bits[row * numcols + col] == 255)
                    PlaceUtah(bits, codewords, numcols, numrows, row, col, pos++);
                row += 2;
                col -= 2;
            } while (row < numrows && col >= 0);

            row += 3;
            col++;

        } while (row < numrows || col < numcols);

        // 右下角固定图案
        if (bits[(numrows - 1) * numcols + numcols - 1] == 255)
        {
            bits[(numrows - 1) * numcols + numcols - 1] = 1;
            bits[(numrows - 2) * numcols + numcols - 2] = 1;
        }

        return bits;
    }

    private static void PlaceModule(byte[] bits, byte[] codewords, int numcols, int numrows,
        int row, int col, int pos, int bit)
    {
        if (row < 0)
        {
            row += numrows;
            col += 4 - ((numrows + 4) % 8);
        }
        if (col < 0)
        {
            col += numcols;
            row += 4 - ((numcols + 4) % 8);
        }

        var v = codewords[pos];
        var mask = 1 << (8 - bit);
        bits[row * numcols + col] = (byte)((v & mask) != 0 ? 1 : 0);
    }

    private static void PlaceUtah(byte[] bits, byte[] codewords, int numcols, int numrows,
        int row, int col, int pos)
    {
        PlaceModule(bits, codewords, numcols, numrows, row - 2, col - 2, pos, 1);
        PlaceModule(bits, codewords, numcols, numrows, row - 2, col - 1, pos, 2);
        PlaceModule(bits, codewords, numcols, numrows, row - 1, col - 2, pos, 3);
        PlaceModule(bits, codewords, numcols, numrows, row - 1, col - 1, pos, 4);
        PlaceModule(bits, codewords, numcols, numrows, row - 1, col, pos, 5);
        PlaceModule(bits, codewords, numcols, numrows, row, col - 2, pos, 6);
        PlaceModule(bits, codewords, numcols, numrows, row, col - 1, pos, 7);
        PlaceModule(bits, codewords, numcols, numrows, row, col, pos, 8);
    }

    private static void PlaceCorner1(byte[] bits, byte[] codewords, int numcols, int numrows, int pos)
    {
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 0, pos, 1);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 1, pos, 2);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 2, pos, 3);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 2, pos, 4);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 1, pos, 5);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 1, pos, 6);
        PlaceModule(bits, codewords, numcols, numrows, 2, numcols - 1, pos, 7);
        PlaceModule(bits, codewords, numcols, numrows, 3, numcols - 1, pos, 8);
    }

    private static void PlaceCorner2(byte[] bits, byte[] codewords, int numcols, int numrows, int pos)
    {
        PlaceModule(bits, codewords, numcols, numrows, numrows - 3, 0, pos, 1);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 2, 0, pos, 2);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 0, pos, 3);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 4, pos, 4);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 3, pos, 5);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 2, pos, 6);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 1, pos, 7);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 1, pos, 8);
    }

    private static void PlaceCorner3(byte[] bits, byte[] codewords, int numcols, int numrows, int pos)
    {
        PlaceModule(bits, codewords, numcols, numrows, numrows - 3, 0, pos, 1);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 2, 0, pos, 2);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 0, pos, 3);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 2, pos, 4);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 1, pos, 5);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 1, pos, 6);
        PlaceModule(bits, codewords, numcols, numrows, 2, numcols - 1, pos, 7);
        PlaceModule(bits, codewords, numcols, numrows, 3, numcols - 1, pos, 8);
    }

    private static void PlaceCorner4(byte[] bits, byte[] codewords, int numcols, int numrows, int pos)
    {
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, 0, pos, 1);
        PlaceModule(bits, codewords, numcols, numrows, numrows - 1, numcols - 1, pos, 2);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 3, pos, 3);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 2, pos, 4);
        PlaceModule(bits, codewords, numcols, numrows, 0, numcols - 1, pos, 5);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 3, pos, 6);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 2, pos, 7);
        PlaceModule(bits, codewords, numcols, numrows, 1, numcols - 1, pos, 8);
    }

    // ==================== 低级编码：定位图案 + 时钟轨道 + 数据 ====================

    private static BitMatrix EncodeLowLevel(byte[] placementBits, SymbolInfo symbol)
    {
        var symbolDataWidth = symbol.SymbolDataWidth;
        var symbolDataHeight = symbol.SymbolDataHeight;
        var symbolWidth = symbol.SymbolWidth;
        var symbolHeight = symbol.SymbolHeight;

        var matrix = new BitMatrix(symbolWidth, symbolHeight);

        var matrixY = 0;

        for (var y = 0; y < symbolDataHeight; y++)
        {
            int matrixX;

            // 顶部时钟轨道行（每行数据区域的顶部）
            if (y % symbol.MatrixHeight == 0)
            {
                matrixX = 0;
                for (var x = 0; x < symbolWidth; x++)
                {
                    matrix.Set(matrixY, matrixX, x % 2 == 0 ? 1 : 0);
                    matrixX++;
                }
                matrixY++;
            }

            matrixX = 0;

            for (var x = 0; x < symbolDataWidth; x++)
            {
                // 左侧实心边
                if (x % symbol.MatrixWidth == 0)
                {
                    matrix.Set(matrixY, matrixX, 1);
                    matrixX++;
                }

                // 数据模块
                var bit = placementBits[y * symbolDataWidth + x];
                matrix.Set(matrixY, matrixX, bit);
                matrixX++;

                // 右侧时钟轨道
                if (x % symbol.MatrixWidth == symbol.MatrixWidth - 1)
                {
                    matrix.Set(matrixY, matrixX, y % 2 == 0 ? 1 : 0);
                    matrixX++;
                }
            }

            matrixY++;

            // 底部实心边
            if (y % symbol.MatrixHeight == symbol.MatrixHeight - 1)
            {
                matrixX = 0;
                for (var x = 0; x < symbolWidth; x++)
                {
                    matrix.Set(matrixY, matrixX, 1);
                    matrixX++;
                }
                matrixY++;
            }
        }

        return matrix;
    }
}

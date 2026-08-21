using DzPrinter.Core;

namespace DzPrinter.Barcode;

/// <summary>
/// GridMatrix (GM) 二维条码编码器。实现 <see cref="IBarcode2DEncoder"/>。
/// GridMatrix 是中国国家标准 GB/T 21049-2007 定义的矩阵式二维条码，
/// 使用 6×6 模块的数据区，支持 Reed-Solomon 纠错。
/// </summary>
public sealed class GridMatrixEncoder : IBarcode2DEncoder
{
    private static readonly ILogger Log = DzLogger.Current;

    // GF(256) 运算表
    private static readonly int[] GfExp = new int[512];
    private static readonly int[] GfLog = new int[256];

    static GridMatrixEncoder()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            GfExp[i] = x;
            GfLog[x] = i;
            x <<= 1;
            if ((x & 256) != 0) x ^= 285;
        }
        for (var i = 255; i < 512; i++)
            GfExp[i] = GfExp[i - 255];
    }

    // GridMatrix 版本表：版本 N → 边长 = (N+1)*6+4，数据容量递增
    // (版本, 总模块数, 数据码字数, 纠错码字数)
    private static readonly (int version, int modules, int dataCW, int eccCW)[] VersionTable =
    {
        (1, 16, 8,  16),
        (2, 22, 24, 32),
        (3, 28, 48, 56),
        (4, 34, 88, 96),
        (5, 40, 144, 128),
        (6, 46, 200, 192),
    };

    public BitMatrix? Encode(Barcode2DRequest request)
    {
        var text = request.Text?.ToString() ?? "";
        if (string.IsNullOrEmpty(text))
            text = request.Content?.ToString() ?? "";
        if (string.IsNullOrEmpty(text)) return null;

        var dataCodewords = EncodeData(text);

        // 选择版本
        var versionIdx = 0;
        for (var i = 0; i < VersionTable.Length; i++)
        {
            if (VersionTable[i].dataCW >= dataCodewords.Length)
            {
                versionIdx = i;
                break;
            }
            versionIdx = i;
        }

        var (_, modules, dataCW, eccCW) = VersionTable[versionIdx];

        // 填充
        var paddedData = new byte[dataCW];
        for (var i = 0; i < dataCodewords.Length && i < dataCW; i++)
            paddedData[i] = (byte)dataCodewords[i];
        // 填充码字
        for (var i = dataCodewords.Length; i < dataCW; i++)
            paddedData[i] = 0;

        var errorCW = ComputeEcc(paddedData, eccCW);

        var allCodewords = new byte[dataCW + eccCW];
        Array.Copy(paddedData, 0, allCodewords, 0, dataCW);
        Array.Copy(errorCW, 0, allCodewords, dataCW, eccCW);

        return BuildMatrix(allCodewords, modules);
    }

    /// <summary>
    /// 将文本编码为数据码字（使用 GBK/UTF-8 字节流）。
    /// </summary>
    private static int[] EncodeData(string text)
    {
        // 尝试 GBK 编码，回退到 UTF-8
        byte[] bytes;
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var gbk = System.Text.Encoding.GetEncoding("GBK");
            bytes = gbk.GetBytes(text);
        }
        catch
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(text);
        }

        // 每个字节直接作为码字
        var codewords = new int[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
            codewords[i] = bytes[i] & 0xFF;
        return codewords;
    }

    /// <summary>
    /// GF(256) Reed-Solomon 编码。
    /// </summary>
    private static byte[] ComputeEcc(byte[] data, int eccCount)
    {
        var genPoly = new int[eccCount + 1];
        genPoly[0] = 1;
        for (var i = 0; i < eccCount; i++)
        {
            var newPoly = new int[eccCount + 1];
            for (var j = 0; j < genPoly.Length; j++)
            {
                newPoly[j] ^= genPoly[j];
                if (j + 1 < newPoly.Length)
                    newPoly[j + 1] ^= GfMul(genPoly[j], GfExp[i + 1]);
            }
            genPoly = newPoly;
        }

        var padded = new int[data.Length + eccCount];
        for (var i = 0; i < data.Length; i++)
            padded[i] = data[i];

        for (var k = 0; k < data.Length; k++)
        {
            var coef = padded[k];
            if (coef == 0) continue;
            for (var j = 1; j < genPoly.Length; j++)
                padded[k + j] ^= GfMul(genPoly[j], coef);
        }

        var result = new byte[eccCount];
        for (var i = 0; i < eccCount; i++)
            result[i] = (byte)padded[data.Length + i];
        return result;
    }

    private static int GfMul(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return GfExp[GfLog[a] + GfLog[b]];
    }

    /// <summary>
    /// 构建 GridMatrix 位矩阵。
    /// 结构：四角定位图形 + 数据区（6×6 网格）+ 信息区。
    /// </summary>
    private static BitMatrix BuildMatrix(byte[] codewords, int modules)
    {
        var matrix = new BitMatrix(modules, modules);

        // 四角定位图形（7×7 实心方块 + 1 模块间隔）
        DrawCornerFinder(matrix, modules);

        // 将码字转为位流
        var bits = new bool[codewords.Length * 8];
        for (var i = 0; i < codewords.Length; i++)
        {
            for (var b = 0; b < 8; b++)
                bits[i * 8 + b] = (codewords[i] & (1 << b)) != 0;
        }

        // 在数据区放置数据（跳过定位图形和信息区）
        var bitIdx = 0;
        for (var r = 0; r < modules && bitIdx < bits.Length; r++)
        {
            for (var c = 0; c < modules && bitIdx < bits.Length; c++)
            {
                if (matrix.IsReserved(r, c)) continue;
                matrix.Set(r, c, bits[bitIdx] ? 1 : 0);
                bitIdx++;
            }
        }

        return matrix;
    }

    /// <summary>
    /// 绘制 GridMatrix 四角定位图形。
    /// 左上、右上、左下各一个 7×7 实心定位方块（边框 + 中心 3×3）。
    /// </summary>
    private static void DrawCornerFinder(BitMatrix matrix, int size)
    {
        // 左上角定位图形（0,0）- 7×7
        DrawFinderSquare(matrix, 0, 0, size);
        // 右上角定位图形（0, size-7）
        DrawFinderSquare(matrix, 0, size - 7, size);
        // 左下角定位图形（size-7, 0）
        DrawFinderSquare(matrix, size - 7, 0, size);
    }

    /// <summary>
    /// 绘制单个 7×7 定位方块：外框 + 3×3 中心实心。
    /// </summary>
    private static void DrawFinderSquare(BitMatrix matrix, int row, int col, int size)
    {
        for (var r = 0; r < 7; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var rr = row + r;
                var cc = col + c;
                if (rr < 0 || rr >= size || cc < 0 || cc >= size) continue;

                // 外框
                var isBorder = r == 0 || r == 6 || c == 0 || c == 6;
                // 中心 3×3
                var isCenter = r >= 2 && r <= 4 && c >= 2 && c <= 4;

                if (isBorder || isCenter)
                    matrix.Set(rr, cc, 1, true);
            }
        }
    }
}

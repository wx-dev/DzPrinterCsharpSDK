using System.Text;
using DzPrinter.Core;

namespace DzPrinter.Barcode;

/// <summary>
/// GridMatrix (GM) 二维条码编码器。实现 <see cref="IBarcode2DEncoder"/>。
/// 基于 AIMD014 标准实现，移植自 OkapiBarcode (GPL) 的 GridMatrix.java。
/// 支持 Chinese/Number/Lower/Upper/Mixed/Byte 六种编码模式及多块 Reed-Solomon 纠错 (GF(128))。
/// </summary>
public sealed class GridMatrixEncoder : IBarcode2DEncoder
{
    private static readonly ILogger Log = DzLogger.Current;

    #region 常量表 (AIMD014)

    // Table 7 - 控制字符编码集 (63 个字符)
    private static readonly int[] ShiftSet =
    {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f,
        '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/', ':',
        ';', '<', '=', '>', '?', '@', '[', '\\', ']', '^', '_', '`', '{', '|', '}', '~'
    };

    // 每层的推荐码字数 (13 个版本)
    private static readonly int[] GmRecommendCw = { 9, 30, 59, 114, 170, 237, 315, 405, 506, 618, 741, 875, 1021 };

    // 每层的最大码字数 (13 个版本)
    private static readonly int[] GmMaxCw = { 11, 40, 79, 146, 218, 305, 405, 521, 650, 794, 953, 1125, 1313 };

    // 每个版本×纠错等级对应的数据码字数 (13×5 = 65)
    private static readonly int[] GmDataCodewords =
    {
        0, 15, 13, 11, 9,
        45, 40, 35, 30, 25,
        89, 79, 69, 59, 49,
        146, 130, 114, 98, 81,
        218, 194, 170, 146, 121,
        305, 271, 237, 203, 169,
        405, 360, 315, 270, 225,
        521, 463, 405, 347, 289,
        650, 578, 506, 434, 361,
        794, 706, 618, 530, 441,
        953, 847, 741, 635, 529,
        1125, 1000, 875, 750, 625,
        1313, 1167, 1021, 875, 729
    };

    // 每层的主块大小 N1 (13 个版本)
    private static readonly int[] GmN1 = { 18, 50, 98, 81, 121, 113, 113, 116, 121, 126, 118, 125, 122 };

    // 每层的主块数 B1 (13 个版本)
    private static readonly int[] GmB1 = { 1, 1, 1, 2, 2, 2, 2, 3, 2, 7, 5, 10, 6 };

    // 每层的次块数 B2 (13 个版本)
    private static readonly int[] GmB2 = { 0, 0, 0, 0, 0, 1, 2, 2, 4, 0, 4, 0, 6 };

    // 每层×纠错等级对应的 ECC 参数 E1, B3, E2, B4 (13×5×4 = 260)
    private static readonly int[] GmEbeb =
    {
        /* E1 B3 E2 B4 */
        0, 0, 0, 0, /* version 1 */
        3, 1, 0, 0,
        5, 1, 0, 0,
        7, 1, 0, 0,
        9, 1, 0, 0,
        5, 1, 0, 0, /* version 2 */
        10, 1, 0, 0,
        15, 1, 0, 0,
        20, 1, 0, 0,
        25, 1, 0, 0,
        9, 1, 0, 0, /* version 3 */
        19, 1, 0, 0,
        29, 1, 0, 0,
        39, 1, 0, 0,
        49, 1, 0, 0,
        8, 2, 0, 0, /* version 4 */
        16, 2, 0, 0,
        24, 2, 0, 0,
        32, 2, 0, 0,
        41, 1, 10, 1,
        12, 2, 0, 0, /* version 5 */
        24, 2, 0, 0,
        36, 2, 0, 0,
        48, 2, 0, 0,
        61, 1, 60, 1,
        11, 3, 0, 0, /* version 6 */
        23, 1, 22, 2,
        34, 2, 33, 1,
        45, 3, 0, 0,
        57, 1, 56, 2,
        12, 1, 11, 3, /* version 7 */
        23, 2, 22, 2,
        34, 3, 33, 1,
        45, 4, 0, 0,
        57, 1, 56, 3,
        12, 2, 11, 3, /* version 8 */
        23, 5, 0, 0,
        35, 3, 34, 2,
        47, 1, 46, 4,
        58, 4, 57, 1,
        12, 6, 0, 0, /* version 9 */
        24, 6, 0, 0,
        36, 6, 0, 0,
        48, 6, 0, 0,
        61, 1, 60, 5,
        13, 4, 12, 3, /* version 10 */
        26, 1, 25, 6,
        38, 5, 37, 2,
        51, 2, 50, 5,
        63, 7, 0, 0,
        12, 6, 11, 3, /* version 11 */
        24, 4, 23, 5,
        36, 2, 35, 7,
        47, 9, 0, 0,
        59, 7, 58, 2,
        13, 5, 12, 5, /* version 12 */
        25, 10, 0, 0,
        38, 5, 37, 5,
        50, 10, 0, 0,
        63, 5, 62, 5,
        13, 1, 12, 11, /* version 13 */
        25, 3, 24, 9,
        37, 5, 36, 7,
        49, 7, 48, 5,
        61, 9, 60, 3
    };

    // 宏模块排列顺序 (27×27 = 729)，从中心向外螺旋排列
    private static readonly int[] GmMacroMatrix =
    {
        728, 625, 626, 627, 628, 629, 630, 631, 632, 633, 634, 635, 636, 637, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649, 650,
        727, 624, 529, 530, 531, 532, 533, 534, 535, 536, 537, 538, 539, 540, 541, 542, 543, 544, 545, 546, 547, 548, 549, 550, 551, 552, 651,
        726, 623, 528, 441, 442, 443, 444, 445, 446, 447, 448, 449, 450, 451, 452, 453, 454, 455, 456, 457, 458, 459, 460, 461, 462, 553, 652,
        725, 622, 527, 440, 361, 362, 363, 364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375, 376, 377, 378, 379, 380, 463, 554, 653,
        724, 621, 526, 439, 360, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 381, 464, 555, 654,
        723, 620, 525, 438, 359, 288, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 307, 382, 465, 556, 655,
        722, 619, 524, 437, 358, 287, 224, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 241, 308, 383, 466, 557, 656,
        721, 618, 523, 436, 357, 286, 223, 168, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 183, 242, 309, 384, 467, 558, 657,
        720, 617, 522, 435, 356, 285, 222, 167, 120, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 133, 184, 243, 310, 385, 468, 559, 658,
        719, 616, 521, 434, 355, 284, 221, 166, 119, 80, 49, 50, 51, 52, 53, 54, 55, 56, 91, 134, 185, 244, 311, 386, 469, 560, 659,
        718, 615, 520, 433, 354, 283, 220, 165, 118, 79, 48, 25, 26, 27, 28, 29, 30, 57, 92, 135, 186, 245, 312, 387, 470, 561, 660,
        717, 614, 519, 432, 353, 282, 219, 164, 117, 78, 47, 24, 9, 10, 11, 12, 31, 58, 93, 136, 187, 246, 313, 388, 471, 562, 661,
        716, 613, 518, 431, 352, 281, 218, 163, 116, 77, 46, 23, 8, 1, 2, 13, 32, 59, 94, 137, 188, 247, 314, 389, 472, 563, 662,
        715, 612, 517, 430, 351, 280, 217, 162, 115, 76, 45, 22, 7, 0, 3, 14, 33, 60, 95, 138, 189, 248, 315, 390, 473, 564, 663,
        714, 611, 516, 429, 350, 279, 216, 161, 114, 75, 44, 21, 6, 5, 4, 15, 34, 61, 96, 139, 190, 249, 316, 391, 474, 565, 664,
        713, 610, 515, 428, 349, 278, 215, 160, 113, 74, 43, 20, 19, 18, 17, 16, 35, 62, 97, 140, 191, 250, 317, 392, 475, 566, 665,
        712, 609, 514, 427, 348, 277, 214, 159, 112, 73, 42, 41, 40, 39, 38, 37, 36, 63, 98, 141, 192, 251, 318, 393, 476, 567, 666,
        711, 608, 513, 426, 347, 276, 213, 158, 111, 72, 71, 70, 69, 68, 67, 66, 65, 64, 99, 142, 193, 252, 319, 394, 477, 568, 667,
        710, 607, 512, 425, 346, 275, 212, 157, 110, 109, 108, 107, 106, 105, 104, 103, 102, 101, 100, 143, 194, 253, 320, 395, 478, 569, 668,
        709, 606, 511, 424, 345, 274, 211, 156, 155, 154, 153, 152, 151, 150, 149, 148, 147, 146, 145, 144, 195, 254, 321, 396, 479, 570, 669,
        708, 605, 510, 423, 344, 273, 210, 209, 208, 207, 206, 205, 204, 203, 202, 201, 200, 199, 198, 197, 196, 255, 322, 397, 480, 571, 670,
        707, 604, 509, 422, 343, 272, 271, 270, 269, 268, 267, 266, 265, 264, 263, 262, 261, 260, 259, 258, 257, 256, 323, 398, 481, 572, 671,
        706, 603, 508, 421, 342, 341, 340, 339, 338, 337, 336, 335, 334, 333, 332, 331, 330, 329, 328, 327, 326, 325, 324, 399, 482, 573, 672,
        705, 602, 507, 420, 419, 418, 417, 416, 415, 414, 413, 412, 411, 410, 409, 408, 407, 406, 405, 404, 403, 402, 401, 400, 483, 574, 673,
        704, 601, 506, 505, 504, 503, 502, 501, 500, 499, 498, 497, 496, 495, 494, 493, 492, 491, 490, 489, 488, 487, 486, 485, 484, 575, 674,
        703, 600, 599, 598, 597, 596, 595, 594, 593, 592, 591, 590, 589, 588, 587, 586, 585, 584, 583, 582, 581, 580, 579, 578, 577, 576, 675,
        702, 701, 700, 699, 698, 697, 696, 695, 694, 693, 692, 691, 690, 689, 688, 687, 686, 685, 684, 683, 682, 681, 680, 679, 678, 677, 676
    };

    // Mixed 模式字母数字集 (63 个字符)
    private static readonly char[] MixedAlphanumSet =
    {
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
        'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
        'u', 'v', 'w', 'x', 'y', 'z',
        ' '
    };

    #endregion

    #region 模式枚举

    private enum Mode
    {
        Null, GmNumber, GmLower, GmUpper, GmMixed, GmControl, GmByte, GmChinese
    }

    #endregion

    #region 实例字段

    private StringBuilder _binary = new();
    private readonly int[] _word = new int[1460];
    private bool[] _grid = Array.Empty<bool>();
    private Mode _appxDnextSection = Mode.Null;
    private Mode _appxDlastSection = Mode.Null;
    private int[] _inputData = Array.Empty<int>();
    private int _eciMode = 3;

    #endregion

    #region IBarcode2DEncoder

    public BitMatrix? Encode(Barcode2DRequest request)
    {
        var text = request.Text?.ToString() ?? "";
        if (string.IsNullOrEmpty(text))
            text = request.Content?.ToString() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            Log.Warn("---- GridMatrix: empty input");
            return null;
        }

        // 将用户首选版本/纠错等级通过 Barcode2DRequest 的 Version 字段传入 (1-13 = 版本, 负值或 0 = 自动)
        var preferredVersion = request.Version ?? 0;
        // 复用 EccLevel 字段表示 GM 纠错等级 (1-5)，null/0 = 自动
        var preferredEccLevel = request.EccLevel.HasValue ? (int)request.EccLevel.Value + 1 : -1;

        try
        {
            return EncodeInternal(text, preferredVersion, preferredEccLevel);
        }
        catch (Exception ex)
        {
            Log.Warn($"---- GridMatrix encode error: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region 主编码流程

    private BitMatrix? EncodeInternal(string content, int preferredVersion, int preferredEccLevel)
    {
        for (var i = 0; i < 1460; i++) _word[i] = 0;

        // 尝试 GB2312 编码 (含中文压缩)
        int length;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gb2312 = Encoding.GetEncoding("GB2312");
            if (CanEncodeInCharset(gb2312, content))
            {
                var inputBytes = gb2312.GetBytes(content);
                _inputData = new int[inputBytes.Length];
                length = 0;
                for (var i = 0; i < inputBytes.Length; i++)
                {
                    var b = inputBytes[i] & 0xFF;
                    if (b >= 0xA1 && b <= 0xF7)
                    {
                        // 双字节字符
                        _inputData[length] = (b << 8) | (inputBytes[i + 1] & 0xFF);
                        i++;
                        length++;
                    }
                    else
                    {
                        _inputData[length] = b;
                        length++;
                    }
                }
                _eciMode = 29;
            }
            else
            {
                throw new NotSupportedException("Non-GB2312 content not supported in this build");
            }
        }
        catch (Exception ex)
        {
            // 回退: 按 UTF-8 字节处理 (eciMode=3 不输出 ECI 头)
            var utf8Bytes = Encoding.UTF8.GetBytes(content);
            _inputData = new int[utf8Bytes.Length];
            for (var i = 0; i < utf8Bytes.Length; i++)
                _inputData[i] = utf8Bytes[i] & 0xFF;
            length = utf8Bytes.Length;
            _eciMode = 3;
            Log.Debug($"---- GridMatrix: fallback to byte mode ({ex.Message})");
        }

        // 编码为二进制位流
        var errorNumber = EncodeGridMatrixBinary(length, false);
        if (errorNumber != 0)
        {
            Log.Warn("---- GridMatrix: input too long");
            return null;
        }

        // 确定符号尺寸
        var dataCw = _binary.Length / 7;
        var autoLayers = 1;
        for (var i = 0; i < 13; i++)
            if (GmRecommendCw[i] < dataCw)
                autoLayers = i + 1;

        var minLayers = 13;
        for (var i = 12; i > 0; i--)
            if (GmMaxCw[i - 1] >= dataCw)
                minLayers = i;

        var layers = autoLayers;
        var autoEccLevel = 3;
        if (layers == 1) autoEccLevel = 5;
        if (layers == 2 || layers == 3) autoEccLevel = 4;

        var minEccLevel = 1;
        if (layers == 1) minEccLevel = 4;
        if (layers == 2 || layers == 3) minEccLevel = 2;

        var eccLevel = autoEccLevel;
        var inputLatch = 0;

        if (preferredVersion >= 1 && preferredVersion <= 13)
        {
            inputLatch = 1;
            layers = preferredVersion > minLayers ? preferredVersion : minLayers;
        }

        if (inputLatch == 1)
        {
            autoEccLevel = 3;
            if (layers == 1) autoEccLevel = 5;
            if (layers == 2 || layers == 3) autoEccLevel = 4;
            eccLevel = autoEccLevel;
            if (dataCw > GmDataCodewords[5 * (layers - 1) + (eccLevel - 1)])
                layers++;
        }

        if (inputLatch == 0)
        {
            if (preferredEccLevel >= 1 && preferredEccLevel <= 5)
                eccLevel = preferredEccLevel > minEccLevel ? preferredEccLevel : minEccLevel;

            if (dataCw > GmDataCodewords[5 * (layers - 1) + (eccLevel - 1)])
            {
                do { layers++; }
                while (dataCw > GmDataCodewords[5 * (layers - 1) + (eccLevel - 1)] && layers <= 13);
            }
        }

        var dataMax = eccLevel switch
        {
            2 => 1167,
            3 => 1021,
            4 => 875,
            5 => 729,
            _ => 1313
        };

        if (dataCw > dataMax)
        {
            Log.Warn($"---- GridMatrix: data ({dataCw} cw) exceeds max ({dataMax}) for ecc level {eccLevel}");
            return null;
        }

        AddErrorCorrection(dataCw, layers, eccLevel);

        var size = 6 + layers * 12;
        var modules = 1 + layers * 2;

        Log.Debug($"---- GridMatrix: layers={layers}, ecc={eccLevel}, dataCW={dataCw}, " +
                  $"eccCW={GmDataCodewords[(layers - 1) * 5 + (eccLevel - 1)]}, " +
                  $"grid={modules}x{modules} ({size}x{size}px)");

        _grid = new bool[size * size];

        PlaceDataInGrid(modules, size);
        AddLayerId(size, layers, modules, eccLevel);

        // 绘制宏模块边框 (棋盘格模式)
        for (var x = 0; x < modules; x++)
        {
            var dark = 1 - (x & 1);
            for (var y = 0; y < modules; y++)
            {
                if (dark == 1)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        _grid[(y * 6) * size + (x * 6) + i] = true;
                        _grid[((y * 6 + 5) * size) + (x * 6) + i] = true;
                        _grid[((y * 6 + i) * size) + (x * 6)] = true;
                        _grid[((y * 6 + i) * size) + (x * 6) + 5] = true;
                    }
                    _grid[((y * 6 + 5) * size) + (x * 6) + 5] = true;
                    dark = 0;
                }
                else
                {
                    dark = 1;
                }
            }
        }

        // 拷贝到 BitMatrix
        var matrix = new BitMatrix(size, size);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (_grid[y * size + x])
                    matrix.Set(y, x, 1);

        return matrix;
    }

    private static bool CanEncodeInCharset(Encoding enc, string text)
    {
        try
        {
            // 简单检测：如果编码后的字节能完整还原则认为可编码
            var bytes = enc.GetBytes(text);
            var roundTrip = enc.GetString(bytes);
            return roundTrip == text;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 二进制位流编码 (encodeGridMatrixBinary)

    private int EncodeGridMatrixBinary(int length, bool reader)
    {
        var sp = 0;
        var glyph = 0;
        Mode currentMode, nextMode, lastMode;
        int c1, c2;
        bool done;
        var p = 0;
        int ppos;
        var punt = 0;
        int numberPadPosn = 0;
        var byteCountPosn = 0;
        var byteCount = 0;
        int shift;
        var numbuf = new int[3];
        var modeMap = CalculateModeMap(length);

        _binary = new StringBuilder();

        sp = 0;
        currentMode = Mode.Null;
        numberPadPosn = 0;

        if (reader)
        {
            _binary.Append("1010"); /* FNC3 - Reader Initialisation */
        }

        if (_eciMode != 3 && _eciMode != 29)
        {
            _binary.Append("1100"); /* ECI */

            if (_eciMode >= 0 && _eciMode <= 1023)
            {
                _binary.Append('0');
                for (var i = 0x200; i > 0; i >>= 1)
                    _binary.Append((_eciMode & i) != 0 ? '1' : '0');
            }

            if (_eciMode >= 1024 && _eciMode <= 32767)
            {
                _binary.Append("10");
                for (var i = 0x4000; i > 0; i >>= 1)
                    _binary.Append((_eciMode & i) != 0 ? '1' : '0');
            }

            if (_eciMode >= 32768 && _eciMode <= 811799)
            {
                _binary.Append("11");
                for (var i = 0x80000; i > 0; i >>= 1)
                    _binary.Append((_eciMode & i) != 0 ? '1' : '0');
            }
        }

        do
        {
            nextMode = modeMap[sp];

            if (nextMode != currentMode)
            {
                switch (currentMode)
                {
                    case Mode.Null:
                        switch (nextMode)
                        {
                            case Mode.GmChinese: _binary.Append("0001"); break;
                            case Mode.GmNumber: _binary.Append("0010"); break;
                            case Mode.GmLower: _binary.Append("0011"); break;
                            case Mode.GmUpper: _binary.Append("0100"); break;
                            case Mode.GmMixed: _binary.Append("0101"); break;
                            case Mode.GmByte: _binary.Append("0111"); break;
                        }
                        break;
                    case Mode.GmChinese:
                        switch (nextMode)
                        {
                            case Mode.GmNumber: _binary.Append("1111111100001"); break; // 8161
                            case Mode.GmLower: _binary.Append("1111111100010"); break; // 8162
                            case Mode.GmUpper: _binary.Append("1111111100011"); break; // 8163
                            case Mode.GmMixed: _binary.Append("1111111100100"); break; // 8164
                            case Mode.GmByte: _binary.Append("1111111100101"); break; // 8165
                        }
                        break;
                    case Mode.GmNumber:
                        /* add numeric block padding value */
                        switch (p)
                        {
                            case 1: _binary.Insert(numberPadPosn, "10"); break; // 2 pad digits
                            case 2: _binary.Insert(numberPadPosn, "01"); break; // 1 pad digit
                            case 3: _binary.Insert(numberPadPosn, "00"); break; // 0 pad digits
                        }
                        switch (nextMode)
                        {
                            case Mode.GmChinese: _binary.Append("1111111011"); break; // 1019
                            case Mode.GmLower: _binary.Append("1111111100"); break; // 1020
                            case Mode.GmUpper: _binary.Append("1111111101"); break; // 1021
                            case Mode.GmMixed: _binary.Append("1111111110"); break; // 1022
                            case Mode.GmByte: _binary.Append("1111111111"); break; // 1023
                        }
                        break;
                    case Mode.GmLower:
                    case Mode.GmUpper:
                        switch (nextMode)
                        {
                            case Mode.GmChinese: _binary.Append("11100"); break; // 28
                            case Mode.GmNumber: _binary.Append("11101"); break; // 29
                            case Mode.GmLower:
                            case Mode.GmUpper: _binary.Append("11110"); break; // 30
                            case Mode.GmMixed: _binary.Append("1111100"); break; // 124
                            case Mode.GmByte: _binary.Append("1111110"); break; // 126
                        }
                        break;
                    case Mode.GmMixed:
                        switch (nextMode)
                        {
                            case Mode.GmChinese: _binary.Append("1111110001"); break; // 1009
                            case Mode.GmNumber: _binary.Append("1111110010"); break; // 1010
                            case Mode.GmLower: _binary.Append("1111110011"); break; // 1011
                            case Mode.GmUpper: _binary.Append("1111110100"); break; // 1012
                            case Mode.GmByte: _binary.Append("1111110111"); break; // 1015
                        }
                        break;
                    case Mode.GmByte:
                        /* add byte block length indicator */
                        AddByteCount(byteCountPosn, byteCount);
                        byteCount = 0;
                        switch (nextMode)
                        {
                            case Mode.GmChinese: _binary.Append("0001"); break; // 1
                            case Mode.GmNumber: _binary.Append("0010"); break; // 2
                            case Mode.GmLower: _binary.Append("0011"); break; // 3
                            case Mode.GmUpper: _binary.Append("0100"); break; // 4
                            case Mode.GmMixed: _binary.Append("0101"); break; // 5
                        }
                        break;
                }
            }

            lastMode = currentMode;
            currentMode = nextMode;

            switch (currentMode)
            {
                case Mode.GmChinese:
                    done = false;
                    if (_inputData[sp] > 0xff)
                    {
                        /* GB2312 character */
                        c1 = (_inputData[sp] & 0xff00) >> 8;
                        c2 = _inputData[sp] & 0xff;

                        if (c1 >= 0xa0 && c1 <= 0xa9)
                            glyph = 0x60 * (c1 - 0xa1) + (c2 - 0xa0);
                        if (c1 >= 0xb0 && c1 <= 0xf7)
                            glyph = 0x60 * (c1 - 0xb0 + 9) + (c2 - 0xa0);
                        done = true;
                    }
                    if (!done)
                    {
                        if (sp != length - 1)
                        {
                            if (_inputData[sp] == 13 && _inputData[sp + 1] == 10)
                            {
                                /* End of Line */
                                glyph = 7776;
                                sp++;
                                done = true;
                            }
                        }
                    }
                    if (!done)
                    {
                        if (sp != length - 1)
                        {
                            if (_inputData[sp] >= '0' && _inputData[sp] <= '9' &&
                                _inputData[sp + 1] >= '0' && _inputData[sp + 1] <= '9')
                            {
                                /* Two digits */
                                glyph = 8033 + 10 * (_inputData[sp] - '0') + (_inputData[sp + 1] - '0');
                                sp++;
                            }
                        }
                    }
                    if (!done)
                    {
                        /* Byte value */
                        glyph = 7777 + _inputData[sp];
                    }

                    for (var i = 0x1000; i > 0; i >>= 1)
                        _binary.Append((glyph & i) != 0 ? '1' : '0');
                    sp++;
                    break;

                case Mode.GmNumber:
                    if (lastMode != currentMode)
                        numberPadPosn = _binary.Length;

                    p = 0;
                    ppos = -1;

                    numbuf[0] = '0';
                    numbuf[1] = '0';
                    numbuf[2] = '0';
                    do
                    {
                        if (_inputData[sp] >= '0' && _inputData[sp] <= '9')
                        {
                            numbuf[p] = _inputData[sp];
                            p++;
                        }
                        switch (_inputData[sp])
                        {
                            case ' ':
                            case '+':
                            case '-':
                            case '.':
                            case ',':
                                punt = _inputData[sp];
                                ppos = p;
                                break;
                        }
                        if (sp < length - 1)
                        {
                            if (_inputData[sp] == 13 && _inputData[sp + 1] == 10)
                            {
                                /* <end of line> */
                                punt = _inputData[sp];
                                sp++;
                                ppos = p;
                            }
                        }
                        sp++;
                    } while (p < 3 && sp < length);

                    if (ppos != -1)
                    {
                        glyph = punt switch
                        {
                            ' ' => 0,
                            '+' => 3,
                            '-' => 6,
                            '.' => 9,
                            ',' => 12,
                            0x13 => 15, // CR (end of line marker)
                            _ => 0
                        };
                        glyph += ppos;
                        glyph += 1000;

                        for (var i = 0x200; i > 0; i >>= 1)
                            _binary.Append((glyph & i) != 0 ? '1' : '0');
                    }

                    glyph = 100 * (numbuf[0] - '0') + 10 * (numbuf[1] - '0') + (numbuf[2] - '0');

                    for (var i = 0x200; i > 0; i >>= 1)
                        _binary.Append((glyph & i) != 0 ? '1' : '0');
                    break;

                case Mode.GmByte:
                    if (lastMode != currentMode)
                        byteCountPosn = _binary.Length;

                    if (byteCount == 512)
                    {
                        /* Maximum byte block size is 512 bytes */
                        AddByteCount(byteCountPosn, byteCount);
                        _binary.Append("0111");
                        byteCountPosn = _binary.Length;
                        byteCount = 0;
                    }

                    glyph = _inputData[sp];
                    for (var i = 0x80; i > 0; i >>= 1)
                        _binary.Append((glyph & i) != 0 ? '1' : '0');
                    sp++;
                    byteCount++;
                    break;

                case Mode.GmMixed:
                    shift = 1;
                    if (_inputData[sp] >= '0' && _inputData[sp] <= '9') shift = 0;
                    if (_inputData[sp] >= 'A' && _inputData[sp] <= 'Z') shift = 0;
                    if (_inputData[sp] >= 'a' && _inputData[sp] <= 'z') shift = 0;
                    if (_inputData[sp] == ' ') shift = 0;

                    if (shift == 0)
                    {
                        /* Mixed Mode character */
                        glyph = Array.IndexOf(MixedAlphanumSet, (char)_inputData[sp]);

                        for (var i = 0x20; i > 0; i >>= 1)
                            _binary.Append((glyph & i) != 0 ? '1' : '0');
                    }
                    else
                    {
                        /* Shift Mode character */
                        _binary.Append("1111110110"); /* 1014 - shift indicator */
                        AddShiftCharacter(_inputData[sp]);
                    }

                    sp++;
                    break;

                case Mode.GmUpper:
                    shift = 1;
                    if (_inputData[sp] >= 'A' && _inputData[sp] <= 'Z') shift = 0;
                    if (_inputData[sp] == ' ') shift = 0;

                    if (shift == 0)
                    {
                        /* Upper Case character */
                        glyph = Array.IndexOf(MixedAlphanumSet, (char)_inputData[sp]) - 10;
                        if (glyph == 52) glyph = 26; // Space

                        for (var i = 0x10; i > 0; i >>= 1)
                            _binary.Append((glyph & i) != 0 ? '1' : '0');
                    }
                    else
                    {
                        /* Shift Mode character */
                        _binary.Append("1111101"); /* 127 - shift indicator */
                        AddShiftCharacter(_inputData[sp]);
                    }

                    sp++;
                    break;

                case Mode.GmLower:
                    shift = 1;
                    if (_inputData[sp] >= 'a' && _inputData[sp] <= 'z') shift = 0;
                    if (_inputData[sp] == ' ') shift = 0;

                    if (shift == 0)
                    {
                        /* Lower Case character */
                        glyph = Array.IndexOf(MixedAlphanumSet, (char)_inputData[sp]) - 36;

                        for (var i = 0x10; i > 0; i >>= 1)
                            _binary.Append((glyph & i) != 0 ? '1' : '0');
                    }
                    else
                    {
                        /* Shift Mode character */
                        _binary.Append("1111101"); /* 127 - shift indicator */
                        AddShiftCharacter(_inputData[sp]);
                    }

                    sp++;
                    break;
            }

            if (_binary.Length > 9191)
                return 1;

        } while (sp < length);

        if (currentMode == Mode.GmNumber)
        {
            switch (p)
            {
                case 1: _binary.Insert(numberPadPosn, "10"); break; // 2 pad digits
                case 2: _binary.Insert(numberPadPosn, "01"); break; // 1 pad digit
                case 3: _binary.Insert(numberPadPosn, "00"); break; // 0 pad digits
            }
        }

        if (currentMode == Mode.GmByte)
            AddByteCount(byteCountPosn, byteCount);

        /* Add "end of data" character */
        switch (currentMode)
        {
            case Mode.GmChinese: _binary.Append("1111111100000"); break; // 8160
            case Mode.GmNumber: _binary.Append("1111111010"); break; // 1018
            case Mode.GmLower:
            case Mode.GmUpper: _binary.Append("11011"); break; // 27
            case Mode.GmMixed: _binary.Append("1111110000"); break; // 1008
            case Mode.GmByte: _binary.Append("0000"); break; // 0
        }

        /* Add padding bits if required */
        p = 7 - _binary.Length % 7;
        if (p == 7) p = 0;
        for (var i = 0; i < p; i++)
            _binary.Append('0');

        if (_binary.Length > 9191)
            return 1;

        return 0;
    }

    private void AddByteCount(int byteCountPosn, int byteCount)
    {
        /* Add the length indicator for byte encoded blocks (9 bits, inverted) */
        for (var i = 0; i < 9; i++)
        {
            _binary.Insert(byteCountPosn + i, (byteCount & (0x100 >> i)) != 0 ? '0' : '1');
        }
    }

    private void AddShiftCharacter(int shifty)
    {
        /* Add a control character to the data stream */
        var glyph = 0;
        for (var i = 0; i < 64; i++)
            if (i < ShiftSet.Length && ShiftSet[i] == shifty)
                glyph = i;

        for (var i = 0x20; i > 0; i >>= 1)
            _binary.Append((glyph & i) != 0 ? '1' : '0');
    }

    #endregion

    #region 模式映射计算 (calculateModeMap)

    private Mode[] CalculateModeMap(int length)
    {
        var modeMap = new Mode[length];
        int i;
        int digitStart, digitLength;
        bool digits;
        int spaceStart, spaceLength;
        bool spaces;

        // Step 1: GB2312 字符编码为中文
        for (i = 0; i < length; i++)
        {
            modeMap[i] = Mode.Null;
            if (_inputData[i] > 0xFF)
                modeMap[i] = Mode.GmChinese;
        }

        // 连续 <end of line> 字符，如果前后都是中文，也编码为中文
        if (length > 3)
        {
            i = 1;
            do
            {
                if (_inputData[i] == 13 && _inputData[i + 1] == 10)
                {
                    if (modeMap[i - 1] == Mode.GmChinese)
                    {
                        modeMap[i] = Mode.GmChinese;
                        modeMap[i + 1] = Mode.GmChinese;
                    }
                    i += 2;
                }
                else
                {
                    i++;
                }
            } while (i < length - 1);

            i = length - 3;
            do
            {
                if (_inputData[i] == 13 && _inputData[i + 1] == 10)
                {
                    if (modeMap[i + 2] == Mode.GmChinese)
                    {
                        modeMap[i] = Mode.GmChinese;
                        modeMap[i + 1] = Mode.GmChinese;
                    }
                    i -= 2;
                }
                else
                {
                    i--;
                }
            } while (i > 0);
        }

        // 中文之间的数字对编码为中文
        digits = false;
        digitLength = 0;
        digitStart = 0;
        for (i = 1; i < length - 1; i++)
        {
            if (_inputData[i] >= 48 && _inputData[i] <= 57)
            {
                if (!digits)
                {
                    digits = true;
                    digitLength = 1;
                    digitStart = i;
                }
                else
                {
                    digitLength++;
                }
            }
            else
            {
                if (digits)
                {
                    if (digitLength % 2 == 0)
                    {
                        if (modeMap[digitStart - 1] == Mode.GmChinese &&
                            modeMap[i] == Mode.GmChinese)
                        {
                            for (var j = 0; j < digitLength; j++)
                                modeMap[i - j - 1] = Mode.GmChinese;
                        }
                    }
                    digits = false;
                }
            }
        }

        // Step 2: 'a'-'z' 编码为小写
        for (i = 0; i < length; i++)
            if (_inputData[i] >= 97 && _inputData[i] <= 122)
                modeMap[i] = Mode.GmLower;

        // Step 3: 'A'-'Z' 编码为大写
        for (i = 0; i < length; i++)
            if (_inputData[i] >= 65 && _inputData[i] <= 90)
                modeMap[i] = Mode.GmUpper;

        // Step 4: 连续 <space> 字符，如果前后是大写或小写，也编码为对应模式
        spaces = false;
        spaceLength = 0;
        spaceStart = 0;
        for (i = 1; i < length - 1; i++)
        {
            if (_inputData[i] == 32)
            {
                if (!spaces)
                {
                    spaces = true;
                    spaceLength = 1;
                    spaceStart = i;
                }
                else
                {
                    spaceLength++;
                }
            }
            else
            {
                if (spaces)
                {
                    var modeX = modeMap[spaceStart - 1];
                    var modeY = modeMap[i];

                    if (modeX == Mode.GmLower || modeX == Mode.GmUpper)
                    {
                        for (var j = 0; j < spaceLength; j++)
                            modeMap[i - j - 1] = modeX;
                    }
                    else if (modeY == Mode.GmLower || modeY == Mode.GmUpper)
                    {
                        for (var j = 0; j < spaceLength; j++)
                            modeMap[i - j - 1] = modeY;
                    }
                    spaces = false;
                }
            }
        }

        // Step 5: 未分配的 '0'-'9' 及部分标点分配为数字模式
        for (i = 0; i < length; i++)
        {
            if (modeMap[i] == Mode.Null)
            {
                if (_inputData[i] >= 48 && _inputData[i] <= 57)
                {
                    modeMap[i] = Mode.GmNumber;
                }
                else
                {
                    switch (_inputData[i])
                    {
                        case 32: // Space
                        case 43: // '+'
                        case 45: // '-'
                        case 46: // "."
                        case 44: // ","
                            modeMap[i] = Mode.GmNumber;
                            break;
                        case 13: // CR
                            if (i < length - 1)
                            {
                                if (_inputData[i + 1] == 10) // LF
                                {
                                    modeMap[i] = Mode.GmNumber;
                                    modeMap[i + 1] = Mode.GmNumber;
                                }
                            }
                            break;
                    }
                }
            }
        }

        // Step 6: 剩余未分配字节分配为 8-bit binary
        for (i = 0; i < length; i++)
            if (modeMap[i] == Mode.Null)
                modeMap[i] = Mode.GmByte;

        // 分段
        var segmentLength = new int[length];
        var segmentType = new Mode[length];
        var segmentStart = new int[length];

        var segmentCount = 0;
        segmentLength[0] = 1;
        segmentType[0] = modeMap[0];
        segmentStart[0] = 0;
        for (i = 1; i < length; i++)
        {
            if (modeMap[i] == modeMap[i - 1])
            {
                segmentLength[segmentCount]++;
            }
            else
            {
                segmentCount++;
                segmentLength[segmentCount] = 1;
                segmentType[segmentCount] = modeMap[i];
                segmentStart[segmentCount] = i;
            }
        }

        // 控制段检测: 长度 ≤ 3 且全部是控制字符且前一段非中文
        if (segmentCount > 1)
        {
            for (i = 1; i < segmentCount; i++) // (a)
            {
                if (segmentLength[i] <= 3 && segmentType[i - 1] != Mode.GmChinese) // (c) and (d)
                {
                    var controlLatch = true;
                    for (var j = 0; j < segmentLength[i]; j++)
                    {
                        var thischarLatch = false;
                        for (var k = 0; k < 63; k++)
                        {
                            if (_inputData[segmentStart[i] + j] == ShiftSet[k])
                                thischarLatch = true;
                        }
                        if (!thischarLatch)
                            controlLatch = false;
                    }
                    if (controlLatch) // (b)
                        segmentType[i] = Mode.GmControl;
                }
            }
        }

        // Stages 7 to 9: 最优模式选择
        if (segmentCount >= 3)
        {
            for (i = 0; i < segmentCount - 1; i++)
            {
                Mode pm, tm, nm, lm;
                int tl, nl, ll, position;
                var lastSegment = false;

                pm = i == 0 ? Mode.Null : segmentType[i - 1];
                tm = segmentType[i];
                tl = segmentLength[i];
                nm = segmentType[i + 1];
                nl = segmentLength[i + 1];
                lm = segmentType[i + 2];
                ll = segmentLength[i + 2];
                position = segmentStart[i];

                if (i + 2 == segmentCount)
                    lastSegment = true;

                segmentType[i] = GetBestMode(pm, tm, nm, lm, tl, nl, ll, position, lastSegment);

                if (segmentType[i] == Mode.GmControl)
                    segmentType[i] = segmentType[i - 1];
            }

            segmentType[i] = _appxDnextSection;
            segmentType[i + 1] = _appxDlastSection;

            if (segmentType[i] == Mode.GmControl)
                segmentType[i] = segmentType[i - 1];
            if (segmentType[i + 1] == Mode.GmControl)
                segmentType[i + 1] = segmentType[i];
        }

        // 将分段拷回 modeMap
        for (i = 0; i <= segmentCount; i++)
        {
            if (i < segmentType.Length && segmentType[i] != Mode.Null)
            {
                for (var j = 0; j < segmentLength[i] && segmentStart[i] + j < length; j++)
                    modeMap[segmentStart[i] + j] = segmentType[i];
            }
        }

        return modeMap;
    }

    private bool IsTransitionValid(Mode previousMode, Mode thisMode)
    {
        return previousMode switch
        {
            Mode.GmChinese => thisMode == Mode.GmChinese || thisMode == Mode.GmByte,
            Mode.GmNumber => thisMode == Mode.GmNumber || thisMode == Mode.GmMixed ||
                              thisMode == Mode.GmByte || thisMode == Mode.GmChinese,
            Mode.GmLower => thisMode == Mode.GmLower || thisMode == Mode.GmMixed ||
                             thisMode == Mode.GmByte || thisMode == Mode.GmChinese,
            Mode.GmUpper => thisMode == Mode.GmUpper || thisMode == Mode.GmMixed ||
                             thisMode == Mode.GmByte || thisMode == Mode.GmChinese,
            Mode.GmControl => thisMode == Mode.GmControl || thisMode == Mode.GmByte ||
                                thisMode == Mode.GmChinese,
            Mode.GmByte => thisMode == Mode.GmByte || thisMode == Mode.GmChinese,
            _ => false
        };
    }

    private Mode IntToMode(int input)
    {
        return input switch
        {
            1 => Mode.GmChinese,
            2 => Mode.GmByte,
            3 => Mode.GmControl,
            4 => Mode.GmMixed,
            5 => Mode.GmUpper,
            6 => Mode.GmLower,
            7 => Mode.GmNumber,
            _ => Mode.Null
        };
    }

    private Mode GetBestMode(Mode pm, Mode tm, Mode nm, Mode lm, int tl, int nl, int ll, int position, bool lastSegment)
    {
        Mode bestMode = tm;
        var bestBinaryLength = int.MaxValue;

        for (var tmi = 1; tmi < 8; tmi++)
        {
            if (IsTransitionValid(tm, IntToMode(tmi)))
            {
                for (var nmi = 1; nmi < 8; nmi++)
                {
                    if (IsTransitionValid(nm, IntToMode(nmi)))
                    {
                        for (var lmi = 1; lmi < 8; lmi++)
                        {
                            if (IsTransitionValid(lm, IntToMode(lmi)))
                            {
                                var binaryLength = GetBinaryLength(pm, IntToMode(tmi), IntToMode(nmi),
                                    IntToMode(lmi), tl, nl, ll, position, lastSegment);
                                if (binaryLength <= bestBinaryLength)
                                {
                                    bestMode = IntToMode(tmi);
                                    _appxDnextSection = IntToMode(nmi);
                                    _appxDlastSection = IntToMode(lmi);
                                    bestBinaryLength = binaryLength;
                                }
                            }
                        }
                    }
                }
            }
        }

        return bestMode;
    }

    private int GetBinaryLength(Mode pm, Mode tm, Mode nm, Mode lm, int tl, int nl, int ll, int position, bool lastSegment)
    {
        var binaryLength = GetChunkLength(pm, tm, tl, position);
        binaryLength += GetChunkLength(tm, nm, nl, position + tl);
        binaryLength += GetChunkLength(nm, lm, ll, position + tl + nl);

        if (lastSegment)
        {
            binaryLength += lm switch
            {
                Mode.GmChinese => 13,
                Mode.GmNumber => 10,
                Mode.GmLower or Mode.GmUpper => 5,
                Mode.GmMixed => 10,
                Mode.GmByte => 4,
                _ => 0
            };
        }

        return binaryLength;
    }

    private int GetChunkLength(Mode lastMode, Mode thisMode, int thisLength, int position)
    {
        int byteLength;

        switch (thisMode)
        {
            case Mode.GmChinese:
                byteLength = CalcChineseLength(position, thisLength);
                break;
            case Mode.GmNumber:
                byteLength = CalcNumberLength(position, thisLength);
                break;
            case Mode.GmLower:
            case Mode.GmUpper:
                byteLength = 5 * thisLength;
                break;
            case Mode.GmMixed:
                byteLength = CalcMixedLength(position, thisLength);
                break;
            case Mode.GmControl:
                byteLength = 6 * thisLength;
                break;
            default: // GM_BYTE
                byteLength = CalcByteLength(position, thisLength);
                break;
        }

        switch (lastMode)
        {
            case Mode.Null:
                byteLength += 4;
                break;
            case Mode.GmChinese:
                if (thisMode != Mode.GmChinese && thisMode != Mode.GmControl)
                    byteLength += 13;
                break;
            case Mode.GmNumber:
                if (thisMode != Mode.GmChinese && thisMode != Mode.GmControl)
                    byteLength += 10;
                break;
            case Mode.GmLower:
                switch (thisMode)
                {
                    case Mode.GmChinese:
                    case Mode.GmNumber:
                    case Mode.GmUpper:
                        byteLength += 5;
                        break;
                    case Mode.GmMixed:
                    case Mode.GmControl:
                    case Mode.GmByte:
                        byteLength += 7;
                        break;
                }
                break;
            case Mode.GmUpper:
                switch (thisMode)
                {
                    case Mode.GmChinese:
                    case Mode.GmNumber:
                    case Mode.GmLower:
                        byteLength += 5;
                        break;
                    case Mode.GmMixed:
                    case Mode.GmControl:
                    case Mode.GmByte:
                        byteLength += 7;
                        break;
                }
                break;
            case Mode.GmMixed:
                if (thisMode != Mode.GmMixed)
                    byteLength += 10;
                break;
            case Mode.GmByte:
                if (thisMode != Mode.GmByte)
                    byteLength += 4;
                break;
        }

        if (lastMode != Mode.GmByte && thisMode == Mode.GmByte)
            byteLength += 9;

        if (lastMode != Mode.GmNumber && thisMode == Mode.GmNumber)
            byteLength += 2;

        return byteLength;
    }

    private int CalcChineseLength(int position, int length)
    {
        var i = 0;
        var bits = 0;

        do
        {
            bits += 13;

            if (i < length)
            {
                if (position + i + 1 < _inputData.Length &&
                    _inputData[position + i] == 13 && _inputData[position + i + 1] == 10)
                    i++; // <end of line>

                if (position + i + 1 < _inputData.Length &&
                    _inputData[position + i] >= 48 && _inputData[position + i] <= 57 &&
                    _inputData[position + i + 1] >= 48 && _inputData[position + i + 1] <= 57)
                    i++; // two digits
            }
            i++;
        } while (i < length);

        return bits;
    }

    private int CalcMixedLength(int position, int length)
    {
        var bits = 0;

        for (var i = 0; i < length; i++)
        {
            bits += 6;
            for (var k = 0; k < 63; k++)
            {
                if (position + i < _inputData.Length && _inputData[position + i] == ShiftSet[k])
                    bits += 10;
            }
        }

        return bits;
    }

    private int CalcNumberLength(int position, int length)
    {
        int i;
        var bits = 0;
        var numbers = 0;
        var nonnumbers = 0;

        for (i = 0; i < length; i++)
        {
            if (position + i >= _inputData.Length) break;

            if (_inputData[position + i] >= 48 && _inputData[position + i] <= 57)
                numbers++;
            else
                nonnumbers++;

            if (i != 0)
            {
                if (position + i < _inputData.Length &&
                    _inputData[position + i] == 10 && _inputData[position + i - 1] == 13)
                    nonnumbers--; // <end of line>
            }

            if (numbers == 3)
            {
                if (nonnumbers == 1)
                    bits += 20;
                else
                    bits += 10;
                if (nonnumbers > 1)
                    bits += 100; // Invalid encoding
                numbers = 0;
                nonnumbers = 0;
            }
        }

        if (numbers > 0)
        {
            if (nonnumbers == 1)
                bits += 20;
            else
                bits += 10;
        }

        if (nonnumbers > 1)
            bits += 100; // Invalid

        if (position + i - 1 < _inputData.Length &&
            !(_inputData[position + i - 1] >= 48 && _inputData[position + i - 1] <= 57))
            bits += 100; // Data must end with a digit

        return bits;
    }

    private int CalcByteLength(int position, int length)
    {
        var bits = 0;

        for (var i = 0; i < length; i++)
        {
            if (position + i >= _inputData.Length) break;
            if (_inputData[position + i] <= 0xFF)
                bits += 8;
            else
                bits += 16;
        }

        return bits;
    }

    #endregion

    #region Reed-Solomon 纠错 (GF(128), 多块交织)

    private void AddErrorCorrection(int dataPosn, int layers, int eccLevel)
    {
        int i, j, wp;
        int n1, b1, n2, b2, e1, b3, e2;
        int blockSize, dataSize, eccSize;

        var data = new int[1320];
        var block = new int[130];
        var dataBlock = new int[115];
        var eccBlock = new int[70];

        var dataCw = GmDataCodewords[(layers - 1) * 5 + (eccLevel - 1)];

        for (i = 0; i < 1320; i++)
            data[i] = 0;

        /* Convert from binary stream to 7-bit codewords */
        for (i = 0; i < dataPosn; i++)
        {
            for (j = 0; j < 7; j++)
            {
                if (_binary[i * 7 + j] == '1')
                    data[i] += 0x40 >> j;
            }
        }

        /* Add padding codewords */
        data[dataPosn] = 0x00;
        for (i = dataPosn + 1; i < dataCw; i++)
        {
            if ((i & 1) != 0)
                data[i] = 0x7e;
            else
                data[i] = 0x00;
        }

        /* Get block sizes */
        n1 = GmN1[layers - 1];
        b1 = GmB1[layers - 1];
        n2 = n1 - 1;
        b2 = GmB2[layers - 1];
        e1 = GmEbeb[(layers - 1) * 20 + (eccLevel - 1) * 4];
        b3 = GmEbeb[(layers - 1) * 20 + (eccLevel - 1) * 4 + 1];
        e2 = GmEbeb[(layers - 1) * 20 + (eccLevel - 1) * 4 + 2];

        /* Split the data into blocks and calculate ECC */
        wp = 0;
        for (i = 0; i < (b1 + b2); i++)
        {
            blockSize = i < b1 ? n1 : n2;
            eccSize = i < b3 ? e1 : e2;
            dataSize = blockSize - eccSize;

            for (j = 0; j < dataSize; j++)
            {
                dataBlock[j] = data[wp];
                wp++;
            }

            /* Calculate ECC data for this block (GF(128), polynomial 0x89, first root α^1) */
            var rs = new GmReedSolomon(0x89, eccSize, 1);
            rs.Encode(dataSize, dataBlock);
            Array.Copy(rs.Result, 0, eccBlock, 0, eccSize);

            /* Assemble block: data + reversed ECC */
            for (j = 0; j < dataSize; j++)
                block[j] = dataBlock[j];
            for (j = 0; j < eccSize; j++)
                block[j + dataSize] = eccBlock[eccSize - j - 1];

            /* Interleave into word array */
            for (j = 0; j < n2; j++)
                _word[(b1 + b2) * j + i] = block[j];
            if (blockSize == n1)
                _word[(b1 + b2) * (n1 - 1) + i] = block[n1 - 1];
        }
    }

    /// <summary>
    /// GF(2^m) Reed-Solomon 编码器，移植自 OkapiBarcode ReedSolomon.java。
    /// GridMatrix 使用 GF(128) 即 m=7，本原多项式 0x89 = x^7 + x^3 + 1。
    /// </summary>
    private sealed class GmReedSolomon
    {
        private int _logmod;
        private int _rlen;
        private int[] _logt = Array.Empty<int>();
        private int[] _alog = Array.Empty<int>();
        private int[] _rspoly = Array.Empty<int>();
        public int[] Result = Array.Empty<int>();

        public GmReedSolomon(int poly, int nsym, int index)
        {
            InitGf(poly);
            InitCode(nsym, index);
        }

        private void InitGf(int poly)
        {
            int m, b, p, v;

            // Find the top bit, and hence the symbol size
            b = 1;
            m = 0;
            while (b <= poly)
            {
                b <<= 1;
                m++;
            }
            b >>= 1;
            m--;

            _logmod = (1 << m) - 1;
            _logt = new int[_logmod + 1];
            _alog = new int[_logmod];

            p = 1;
            for (v = 0; v < _logmod; v++)
            {
                _alog[v] = p;
                _logt[p] = v;
                p <<= 1;
                if ((p & b) != 0)
                    p ^= poly;
            }
        }

        private void InitCode(int nsym, int index)
        {
            int i, k;

            _rspoly = new int[nsym + 1];
            _rlen = nsym;

            _rspoly[0] = 1;
            for (i = 1; i <= nsym; i++)
            {
                _rspoly[i] = 1;
                for (k = i - 1; k > 0; k--)
                {
                    if (_rspoly[k] != 0)
                        _rspoly[k] = _alog[(_logt[_rspoly[k]] + index) % _logmod];
                    _rspoly[k] ^= _rspoly[k - 1];
                }
                _rspoly[0] = _alog[(_logt[_rspoly[0]] + index) % _logmod];
                index++;
            }
        }

        public void Encode(int len, int[] data)
        {
            int i, k, m;

            _result = new int[_rlen];
            Result = new int[_rlen];
            for (i = 0; i < _rlen; i++)
                Result[i] = 0;

            for (i = 0; i < len; i++)
            {
                m = Result[_rlen - 1] ^ data[i];
                for (k = _rlen - 1; k > 0; k--)
                {
                    if (m != 0 && _rspoly[k] != 0)
                        Result[k] = Result[k - 1] ^ _alog[(_logt[m] + _logt[_rspoly[k]]) % _logmod];
                    else
                        Result[k] = Result[k - 1];
                }
                if (m != 0 && _rspoly[0] != 0)
                    Result[0] = _alog[(_logt[m] + _logt[_rspoly[0]]) % _logmod];
                else
                    Result[0] = 0;
            }
        }

        // Note: field name kept as _result for historical compatibility but unused
        private int[] _result = Array.Empty<int>();
    }

    #endregion

    #region 矩阵放置 (placeDataInGrid / placeMacroModule / addLayerId)

    private void PlaceDataInGrid(int modules, int size)
    {
        var offset = 13 - (modules - 1) / 2;
        for (var y = 0; y < modules; y++)
        {
            for (var x = 0; x < modules; x++)
            {
                var macromodule = GmMacroMatrix[(y + offset) * 27 + (x + offset)];
                PlaceMacroModule(x, y, _word[macromodule * 2], _word[macromodule * 2 + 1], size);
            }
        }
    }

    private void PlaceMacroModule(int x, int y, int word1, int word2, int size)
    {
        var i = (x * 6) + 1;
        var j = (y * 6) + 1;

        // word2: 上半部分 (7 bits, MSB first)
        if ((word2 & 0x40) != 0) _grid[(j * size) + i + 2] = true;
        if ((word2 & 0x20) != 0) _grid[(j * size) + i + 3] = true;
        if ((word2 & 0x10) != 0) _grid[((j + 1) * size) + i] = true;
        if ((word2 & 0x08) != 0) _grid[((j + 1) * size) + i + 1] = true;
        if ((word2 & 0x04) != 0) _grid[((j + 1) * size) + i + 2] = true;
        if ((word2 & 0x02) != 0) _grid[((j + 1) * size) + i + 3] = true;
        if ((word2 & 0x01) != 0) _grid[((j + 2) * size) + i] = true;

        // word1: 下半部分 (7 bits, MSB first)
        if ((word1 & 0x40) != 0) _grid[((j + 2) * size) + i + 1] = true;
        if ((word1 & 0x20) != 0) _grid[((j + 2) * size) + i + 2] = true;
        if ((word1 & 0x10) != 0) _grid[((j + 2) * size) + i + 3] = true;
        if ((word1 & 0x08) != 0) _grid[((j + 3) * size) + i] = true;
        if ((word1 & 0x04) != 0) _grid[((j + 3) * size) + i + 1] = true;
        if ((word1 & 0x02) != 0) _grid[((j + 3) * size) + i + 2] = true;
        if ((word1 & 0x01) != 0) _grid[((j + 3) * size) + i + 3] = true;
    }

    private void AddLayerId(int size, int layers, int modules, int eccLevel)
    {
        /* Place the layer ID into each macromodule */
        int i, j, layer;
        int start, stop;
        var layerid = new int[layers + 1];
        var id = new int[modules * modules];

        /* Calculate Layer IDs */
        for (i = 0; i <= layers; i++)
        {
            if (eccLevel == 1)
                layerid[i] = 3 - (i % 4);
            else
                layerid[i] = (i + 5 - eccLevel) % 4;
        }

        for (i = 0; i < modules; i++)
            for (j = 0; j < modules; j++)
                id[(i * modules) + j] = 0;

        /* Calculate which value goes in each macromodule (from center outward) */
        start = modules / 2;
        stop = modules / 2;
        for (layer = 0; layer <= layers; layer++)
        {
            for (i = start; i <= stop; i++)
            {
                id[(start * modules) + i] = layerid[layer];
                id[(i * modules) + start] = layerid[layer];
                id[((modules - start - 1) * modules) + i] = layerid[layer];
                id[(i * modules) + (modules - start - 1)] = layerid[layer];
            }
            start--;
            stop++;
        }

        /* Place the data in the grid (2 bits per macromodule: bit1→(row+1,col+1), bit0→(row+1,col+2)) */
        for (i = 0; i < modules; i++)
        {
            for (j = 0; j < modules; j++)
            {
                if ((id[(i * modules) + j] & 0x02) != 0)
                    _grid[(((i * 6) + 1) * size) + (j * 6) + 1] = true;
                if ((id[(i * modules) + j] & 0x01) != 0)
                    _grid[(((i * 6) + 1) * size) + (j * 6) + 2] = true;
            }
        }
    }

    #endregion
}

using System.Text;
using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// Code128 条码编码器。对应 JS SDK 中 <c>D</c> 类（基编码器）与 <c>S</c> 类（自动模式选择）。
/// 实现完整的 Code128 A/B/C 三种字符集与自动切换。
/// </summary>
/// <remarks>
/// JS 中通过字符串中的特殊字节标记模式切换：
/// <list type="bullet">
///   <item>203 (0xCB) = SHIFT，下一字符在相反模式（A↔B）</item>
///   <item>204 (0xCC) = CODE_C 切换</item>
///   <item>205 (0xCD) = CODE_B 切换</item>
///   <item>206 (0xCE) = CODE_A 切换</item>
///   <item>207 (0xCF) = FNC1（GS1-128/AI 标识）</item>
///   <item>208 (0xD0) = START_A</item>
///   <item>209 (0xD1) = START_B</item>
///   <item>210 (0xD2) = START_C</item>
/// </list>
/// 模式编号：0=Code A, 1=Code B, 2=Code C。
/// </remarks>
internal sealed class Code128Encoder : Barcode1DEncoder
{
    /// <summary>START_C 编码值。对应 JS <c>f=103</c>。</summary>
    private const int StartC = 103;

    /// <summary>START_B 编码值。对应 JS <c>P=104</c>。</summary>
    private const int StartB = 104;

    /// <summary>START_A 编码值。对应 JS <c>R=105</c>。</summary>
    private const int StartA = 105;

    /// <summary>起始码 → 模式编号映射。对应 JS <c>C={[f]:0,[P]:1,[R]:2}</c>。</summary>
    private static readonly Dictionary<int, int> StartToMode = new()
    {
        [StartC] = 0,
        [StartB] = 1,
        [StartA] = 2
    };

    /// <summary>切换码 → 目标模式编号映射。对应 JS <c>A={101:0,100:1,99:2}</c>。</summary>
    private static readonly Dictionary<int, int> SwitchToMode = new()
    {
        [101] = 0,  // CODE_A → 模式 0
        [100] = 1,  // CODE_B → 模式 1
        [99] = 2   // CODE_C → 模式 2
    };

    /// <summary>SHIFT 字符串标记（chr 203）。对应 JS <c>I=String.fromCharCode(203)</c> 中的 SHIFT 标记。</summary>
    private const char ShiftMarker = (char)203;

    /// <summary>CODE_C 字符串标记（chr 204）。对应 JS <c>String.fromCharCode(204)</c>。</summary>
    private const char CodeCMarker = (char)204;

    /// <summary>CODE_B 字符串标记（chr 205）。对应 JS <c>String.fromCharCode(205)</c>。</summary>
    private const char CodeBMarker = (char)205;

    /// <summary>CODE_A 字符串标记（chr 206）。对应 JS <c>String.fromCharCode(206)</c>。</summary>
    private const char CodeAMarker = (char)206;

    /// <summary>START_A 字符串标记（chr 208）。对应 JS <c>I=String.fromCharCode(208)</c>。</summary>
    private const char StartAMarker = (char)208;

    /// <summary>START_B 字符串标记（chr 209）。对应 JS <c>y=String.fromCharCode(209)</c>。</summary>
    private const char StartBMarker = (char)209;

    /// <summary>START_C 字符串标记（chr 210）。对应 JS <c>b=String.fromCharCode(210)</c>。</summary>
    private const char StartCMarker = (char)210;

    /// <summary>
    /// Code128 11 位模块模式表（编码 0-106）。对应 JS <c>_</c> 数组。
    /// 每个值为 11 位（编码 0-105）或 13 位（编码 106 = STOP）的模块序列，以长整型存储。
    /// </summary>
    private static readonly long[] Patterns = new long[]
    {
        11011001100, 11001101100, 11001100110, 10010011000, 10010001100, 10001001100, 10011001000, 10011000100, 10001100100, 11001001000, 11001000100, 11000100100, 10110011100, 10011011100, 10011001110, 10111001100, 10011101100, 10011100110, 11001110010, 11001011100, 11001001110, 11011100100, 11001110100, 11101101110, 11101001100, 11100101100, 11100100110, 11101100100, 11100110100, 11100110010, 11011011000, 11011000110, 11000110110, 10100011000, 10001011000, 10001000110, 10110001000, 10001101000, 10001100010, 11010001000, 11000101000, 11000100010, 10110111000, 10110001110, 10001101110, 10111011000, 10111000110, 10001110110, 11101110110, 11010001110, 11000101110, 11011101000, 11011100010, 11011101110, 11101011000, 11101000110, 11100010110, 11101101000, 11101100010, 11100011010, 11101111010, 11001000010, 11110001010, 10100110000, 10100001100, 10010110000, 10010000110, 10000101100, 10000100110, 10110010000, 10110000100, 10011010000, 10011000010, 10000110100, 10000110010, 11000010010, 11001010000, 11110111010, 11000010100, 10001111010, 10100111100, 10010111100, 10010011110, 10111100100, 10011110100, 10011110010, 11110100100, 11110010100, 11110010010, 11011011110, 11011110110, 11110110110, 10101111000, 10100011110, 10001011110, 10111101000, 10111100010, 11110101000, 11110100010, 10111011110, 10111101110, 11101011110, 11110101110, 11010000100, 11010010000, 11010011100, 1100011101011
    };

    /// <summary>
    /// 输入字节序列（含起始/切换/SHIFT 标记）。对应 JS <c>D.bytes</c>。
    /// </summary>
    private readonly List<int> _bytes;

    /// <summary>
    /// 直接以已包含模式标记的字符串构造（对应 JS <c>D</c> 类构造）。
    /// JS: <c>super(t.substring(1), e), this.bytes = t.split("").map(t=>t.charCodeAt(0))</c>。
    /// 第一个字符是起始码标记（208/209/210），其余是数据。
    /// </summary>
    public Code128Encoder(string rawInput, BarcodeEncodeOptions options) : base(rawInput.Length > 0 ? rawInput.Substring(1) : string.Empty, options)
    {
        _bytes = rawInput.Select(c => (int)c).ToList();
    }

    /// <summary>
    /// 校验数据是否在 Code128 可编码范围内。对应 JS <c>D.valid()</c>。
    /// 正则 <c>/^[\x00-\x7F\xC8-\xD3]+$/</c>：允许 0x00-0x7F 与 0xC8-0xD3 范围。
    /// </summary>
    public override bool Valid()
    {
        foreach (var ch in Data)
        {
            var code = (int)ch;
            if (code <= 0x7F) continue;
            if (code >= 0xC8 && code <= 0xD3) continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 编码为 Code128 模块序列。对应 JS <c>D.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var bytes = new List<int>(_bytes);
        var startByte = bytes.Count > 0 ? bytes[0] : 0;
        bytes.RemoveAt(0);
        var startCode = startByte - 105;
        if (!StartToMode.TryGetValue(startCode, out var mode))
            throw new ArgumentException("The encoding does not start with a start character.", nameof(_bytes));

        // EAN-128 (GS1-128) 模式：在起始码后插入 FNC1
        if (Options.Ean128) bytes.Insert(0, 207);

        var (result, checksum) = Next(bytes, 1, mode);
        var displayText = Text == Data ? StripNonPrintable(Text) : Text;
        var data = GetBar(startCode) + result + GetBar((checksum + startCode) % 103) + GetBar(106);

        return new BarcodeEncodeResult
        {
            Items = { new BarcodeItem(data, displayText) },
            Text = Text,
            Options = Options
        };
    }

    /// <summary>
    /// 是否按 EAN-128 (GS1-128) 编码。对应 JS <c>D.shouldEncodeAsEan128()</c>。
    /// </summary>
    private bool ShouldEncodeAsEan128() => Options.Ean128;

    /// <summary>
    /// 获取指定编码值的模块模式字符串。对应 JS <c>D.getBar(t)</c>。
    /// </summary>
    private static string GetBar(int code) =>
        code >= 0 && code < Patterns.Length ? Patterns[code].ToString() : string.Empty;

    /// <summary>
    /// 根据当前模式从字节流中取出一个字符的编码值。对应 JS <c>D.correctIndex(t, e)</c>。
    /// </summary>
    /// <param name="bytes">字节流（会被消费）。</param>
    /// <param name="mode">当前模式：0=Code A, 1=Code B, 2=Code C。</param>
    private static int CorrectIndex(List<int> bytes, int mode)
    {
        if (mode == 0)
        {
            // Code A：取一字节，<32 加 64（控制字符），否则减 32
            var b = bytes.Count > 0 ? bytes[0] : 0;
            bytes.RemoveAt(0);
            return b < 32 ? b + 64 : b - 32;
        }
        if (mode == 1)
        {
            // Code B：取一字节，减 32
            var b = bytes.Count > 0 ? bytes[0] : 0;
            bytes.RemoveAt(0);
            return b - 32;
        }
        // Code C：取两字节，作为两位数字组合
        var b1 = bytes.Count > 0 ? bytes[0] : 0;
        bytes.RemoveAt(0);
        var b2 = bytes.Count > 0 ? bytes[0] : 0;
        bytes.RemoveAt(0);
        return 10 * (b1 - 48) + (b2 - 48);
    }

    /// <summary>
    /// 递归处理字节流，生成模块序列与累加校验和。对应 JS <c>D.next(t, e, i)</c>。
    /// </summary>
    /// <param name="bytes">剩余字节流。</param>
    /// <param name="position">当前位置（用于校验和权重）。对应 JS <c>e</c>。</param>
    /// <param name="mode">当前模式。对应 JS <c>i</c>。</param>
    /// <returns>(result 模块字符串, checksum 累加和)。</returns>
    private static (string result, int checksum) Next(List<int> bytes, int position, int mode)
    {
        if (bytes.Count == 0) return (string.Empty, 0);

        int code;
        if (bytes[0] >= 200)
        {
            // 切换/SHIFT/FNC1 标记
            code = bytes[0] - 105;
            bytes.RemoveAt(0);
            if (SwitchToMode.TryGetValue(code, out var switchMode))
            {
                // CODE_A/B/C 显式切换
                var (r, c) = Next(bytes, position + 1, switchMode);
                return (GetBar(code) + r, code * position + c);
            }
            if (mode == 0 || mode == 1)
            {
                if (code == 98)
                {
                    // SHIFT：下一字符在相反模式中解释，但不改变 latch 模式
                    // JS: 0===i ? (t[0] = t[0]>95 ? t[0]-96 : t[0]) : (t[0] = t[0]<32 ? t[0]+96 : t[0])
                    // 即在 mode A 下，下一字符按 mode B 解释（值+96 调整）；mode B 下按 mode A 解释
                    if (bytes.Count > 0)
                    {
                        if (mode == 0)
                            bytes[0] = bytes[0] > 95 ? bytes[0] - 96 : bytes[0];
                        else
                            bytes[0] = bytes[0] < 32 ? bytes[0] + 96 : bytes[0];
                    }
                }
            }
            var (r2, c2) = Next(bytes, position + 1, mode);
            return (GetBar(code) + r2, code * position + c2);
        }
        // 普通数据字符
        code = CorrectIndex(bytes, mode);
        var (rr, cc) = Next(bytes, position + 1, mode);
        return (GetBar(code) + rr, code * position + cc);
    }

    /// <summary>
    /// 去除不可打印字符（保留 0x20-0x7E）。对应 JS <c>this.text.replace(/[^\x20-\x7E]/g, "")</c>。
    /// </summary>
    private static string StripNonPrintable(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text) if (ch >= 0x20 && ch <= 0x7E) sb.Append(ch);
        return sb.ToString();
    }

    // ===== S 类（自动模式选择）的静态入口 =====

    /// <summary>
    /// Code A 字符范围正则：[\x00-\x5F\xC8-\xCF]。对应 JS <c>v="[\0-_È-Ï]"</c>。
    /// </summary>
    private static readonly Regex CodeAPatternRegex = new(@"^[\u0000-\u005F\u00C8-\u00CF]*", RegexOptions.Compiled);

    /// <summary>
    /// Code B 字符范围正则：[\x20-\x7F\xC8-\xCF]。对应 JS <c>E="[ -\x7FÈ-Ï]"</c>。
    /// </summary>
    private static readonly Regex CodeBPatternRegex = new(@"^[\u0020-\u007F\u00C8-\u00CF]*", RegexOptions.Compiled);

    /// <summary>
    /// 前导数字对（含 FNC1 间隔）正则。对应 JS <c>O(t)</c>。
    /// 模式 <c>^(Ï*[0-9]{2}Ï*)*</c>：匹配若干 (FNC1* 数字对 FNC1*) 序列。
    /// Ï = chr(0xCF) = 207 = FNC1。
    /// </summary>
    private static readonly Regex LeadingDigitPairsRegex = new(@"^(?:\u00CF*[0-9]{2}\u00CF*)*", RegexOptions.Compiled);

    /// <summary>
    /// 计算 Code A 字符串前缀长度。对应 JS <c>w(t)</c>。
    /// </summary>
    private static int CountCodeA(string text)
    {
        var m = CodeAPatternRegex.Match(text);
        return m.Success ? m.Value.Length : 0;
    }

    /// <summary>
    /// 计算 Code B 字符串前缀长度。对应 JS <c>T(t)</c>。
    /// </summary>
    private static int CountCodeB(string text)
    {
        var m = CodeBPatternRegex.Match(text);
        return m.Success ? m.Value.Length : 0;
    }

    /// <summary>
    /// 提取前导数字对（含 FNC1 间隔）。对应 JS <c>O(t)</c>。
    /// </summary>
    private static string GetLeadingDigitPairs(string text)
    {
        var m = LeadingDigitPairsRegex.Match(text);
        return m.Success ? m.Value : string.Empty;
    }

    /// <summary>
    /// L 函数：Code A/B 模式下的递归切分。对应 JS <c>L(t, e)</c>。
    /// </summary>
    /// <param name="text">剩余文本。</param>
    /// <param name="inCodeA">当前是否在 Code A 模式。</param>
    private static string SplitAB(string text, bool inCodeA)
    {
        var rangeRegex = inCodeA ? CodeAPatternRegex : CodeBPatternRegex;
        // 查找 (current模式+?)(4位以上数字)(非数字或结尾)
        var pattern = inCodeA
            ? @"^([\u0000-\u005F\u00C8-\u00CF]+?)((?:[0-9]{2}){2,})([^0-9]|$)"
            : @"^([\u0020-\u007F\u00C8-\u00CF]+?)((?:[0-9]{2}){2,})([^0-9]|$)";
        var m = Regex.Match(text, pattern);
        if (m.Success)
        {
            // 当前模式字符 + CODE_C 切换 + x(剩余)
            return m.Groups[1].Value + CodeCMarker + SplitC(text.Substring(m.Groups[1].Value.Length));
        }
        // 无 4 位以上数字对：当前模式连续字符 + 切换到相反模式
        var cm = rangeRegex.Match(text);
        var run = cm.Success ? cm.Value : string.Empty;
        if (run.Length == text.Length) return text;
        // e=true (Code A) → 205 (CODE_B)；e=false (Code B) → 206 (CODE_A)
        return run + (inCodeA ? CodeBMarker : CodeAMarker) + SplitAB(text.Substring(run.Length), !inCodeA);
    }

    /// <summary>
    /// x 函数：Code C 数字对的递归切分。对应 JS <c>x(t)</c>。
    /// </summary>
    private static string SplitC(string text)
    {
        var leading = GetLeadingDigitPairs(text);
        var leadLen = leading.Length;
        if (leadLen == text.Length) return text;
        text = text.Substring(leadLen);
        // 切换到 A 或 B 取决于哪种范围更宽
        var useCodeA = CountCodeA(text) >= CountCodeB(text);
        return leading + (useCodeA ? CodeAMarker : CodeBMarker) + SplitAB(text, useCodeA);
    }

    /// <summary>
    /// 自动选择最佳模式生成 Code128 编码。对应 JS <c>S</c> 类构造逻辑。
    /// 输入应为纯文本（不含模式标记），输出构造完成的 <see cref="Code128Encoder"/>。
    /// </summary>
    public static Code128Encoder CreateAuto(string text, BarcodeEncodeOptions options)
    {
        if (IsCode128Compatible(text))
        {
            string marked;
            if (GetLeadingDigitPairs(text).Length >= 2)
            {
                // 有 ≥2 个前导数字对：START_C + SplitC
                marked = StartCMarker + SplitC(text);
            }
            else
            {
                // 比较 Code A 与 Code B 前缀长度
                var useCodeA = CountCodeA(text) > CountCodeB(text);
                marked = (useCodeA ? StartAMarker : StartBMarker) + SplitAB(text, useCodeA);
            }
            // 相邻 CODE_A/B 切换 + 单字符 + CODE_A/B 切换 → SHIFT + 字符
            // 对应 JS: i.replace(/[\xCD\xCE]([^])[\xCD\xCE]/, (t,e) => String.fromCharCode(203)+e)
            marked = Regex.Replace(marked, @"[\u00CD\u00CE]([\s\S])[\u00CD\u00CE]", m => ShiftMarker.ToString() + m.Groups[1].Value);
            return new Code128Encoder(marked, options);
        }
        return new Code128Encoder(text, options);
    }

    /// <summary>
    /// 校验文本是否可编码为 Code128。对应 JS <c>/^[\x00-\x7F\xC8-\xD3]+$/.test(t)</c>。
    /// </summary>
    private static bool IsCode128Compatible(string text)
    {
        foreach (var ch in text)
        {
            var code = (int)ch;
            if (code <= 0x7F) continue;
            if (code >= 0xC8 && code <= 0xD3) continue;
            return false;
        }
        return true;
    }
}

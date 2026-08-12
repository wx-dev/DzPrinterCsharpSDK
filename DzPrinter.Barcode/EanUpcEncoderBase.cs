using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// EAN/UPC 系列条码的位模式数据表与共享工具。对应 JS SDK 中 <c>M</c> 对象与 <c>B</c> 函数。
/// </summary>
internal static class EanUpcTables
{
    /// <summary>
    /// EAN/UPC 数字位模式表。对应 JS <c>M</c> 对象。
    /// L=奇校验（左侧），G=偶校验（左侧），R=右侧，O=L 别名，E=G 别名。
    /// 每个模式为 7 位 0/1 字符串。
    /// </summary>
    public static readonly Dictionary<char, string[]> Patterns = new()
    {
        ['L'] = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"],
        ['G'] = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"],
        ['R'] = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"],
        ['O'] = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"],
        ['E'] = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"]
    };

    /// <summary>
    /// EAN13 第一位数字 → 左侧 6 位数字的奇偶结构。对应 JS <c>j.EAN13_STRUCTURE</c>。
    /// </summary>
    public static readonly string[] Ean13Structure =
    {
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL",
        "LGLGLG", "LGLGGL", "LGGLGL"
    };

    /// <summary>
    /// EAN2 结构（按 2 位数字 mod 4 选择）。对应 JS <c>U.EAN2_STRUCTURE</c>。
    /// </summary>
    public static readonly string[] Ean2Structure = { "LL", "LG", "GL", "GG" };

    /// <summary>
    /// EAN5 结构（按校验和选择）。对应 JS <c>F.EAN5_STRUCTURE</c>。
    /// </summary>
    public static readonly string[] Ean5Structure =
    {
        "GGLLL", "GLGLL", "GLLGL", "GLLLG", "LGGLL", "LLGGL", "LLLGG",
        "LGLGL", "LGLLG", "LLGLG"
    };

    /// <summary>
    /// UPC-E 数字系统展开规则。对应 JS <c>H</c> 数组。
    /// 用于 UPC-E → UPC-A 的转换（按最后一位数字选择展开模式）。
    /// </summary>
    public static readonly string[] UpcENumberSystem =
    {
        "XX00000XXX", "XX10000XXX", "XX20000XXX", "XXX00000XX",
        "XXXX00000X", "XXXXX00005", "XXXXX00006", "XXXXX00007",
        "XXXXX00008", "XXXXX00009"
    };

    /// <summary>
    /// UPC-E 奇偶结构表。对应 JS <c>k</c> 二维数组。
    /// 第一维按 UPC-E 校验位（0-9）选择，第二维按数字系统（0 或 1）选择。
    /// </summary>
    public static readonly string[][] UpcEParityStructure =
    {
        ["EEEOOO", "OOOEEE"],
        ["EEOEOO", "OOEOEE"],
        ["EEOOEO", "OOEEOE"],
        ["EEOOOE", "OOEEEO"],
        ["EOEEOO", "OEOOEE"],
        ["EOOEEO", "OEEOOE"],
        ["EOOOEE", "OEEEOO"],
        ["EOEOEO", "OEOEOE"],
        ["EOEOOE", "OEOEEO"],
        ["EOOEOE", "OEEOEO"]
    };

    /// <summary>
    /// 编码数字串为模块序列。对应 JS <c>B(t, e, i)</c> 函数。
    /// </summary>
    /// <param name="digits">数字字符串（如 "012345"）。</param>
    /// <param name="structure">奇偶结构字符串（如 "LLLLLL"，每位对应 <see cref="Patterns"/> 中的键）。</param>
    /// <param name="separator">可选分隔符，添加到除最后一段外的每段末尾。</param>
    /// <returns>拼接后的模块序列。</returns>
    public static string EncodeDigits(string digits, string structure, string? separator = null)
    {
        if (string.IsNullOrEmpty(digits) || string.IsNullOrEmpty(structure)) return string.Empty;
        var sb = new StringBuilder(digits.Length * 8);
        var useSep = !string.IsNullOrEmpty(separator);
        var lastIdx = digits.Length - 1;
        for (var i = 0; i < digits.Length && i < structure.Length; i++)
        {
            var ch = digits[i];
            var digit = ch - '0';
            if (digit >= 0 && digit <= 9 && Patterns.TryGetValue(structure[i], out var arr))
            {
                sb.Append(arr[digit]);
                if (useSep && i < lastIdx) sb.Append(separator);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// EAN/UPC 系列条码基类。对应 JS SDK 中 <c>N</c> 类（<c>let N = class t extends m</c>）。
/// 提供 guarded / flat 两种编码布局、左右半部编码、静区与守护条。
/// 子类需重写 <see cref="LeftText"/>/<see cref="LeftEncode"/>/<see cref="RightText"/>/<see cref="RightEncode"/>。
/// </summary>
internal abstract class EanUpcEncoderBase : Barcode1DEncoder
{
    /// <summary>左侧守护条（START guard）。对应 JS <c>t.SIDE_BIN = "101"</c>。</summary>
    protected const string SideBin = "101";

    /// <summary>中间分隔符（CENTER guard）。对应 JS <c>t.MIDDLE_BIN = "01010"</c>。</summary>
    protected const string MiddleBin = "01010";

    /// <summary>两侧静区宽度。对应 JS <c>N.quietZones</c>。</summary>
    protected int QuietZones { get; set; }

    /// <summary>是否显示守护空白。对应 JS <c>N.guardWhitespace</c>。</summary>
    protected bool GuardWhitespace { get; set; }

    protected EanUpcEncoderBase(string data, BarcodeEncodeOptions options) : base(data, options)
    {
        QuietZones = options?.QuietZones ?? 0;
        GuardWhitespace = options?.GuardWhitespace ?? false;
    }

    /// <summary>
    /// 左侧可显示文本。对应 JS <c>N.leftText(t, e)</c>。
    /// 默认实现：返回整个 <see cref="Barcode1DEncoder.Text"/>。
    /// JS 中 <c>substr(0, undefined)</c> 返回整个字符串。
    /// </summary>
    protected virtual string LeftText() => Text;

    /// <summary>
    /// 左侧编码。对应 JS <c>N.leftEncode(t, e)</c>。
    /// 默认实现：编码空串（基类不实际使用，子类必须重写）。
    /// </summary>
    protected virtual string LeftEncode() => EanUpcTables.EncodeDigits(string.Empty, string.Empty);

    /// <summary>
    /// 右侧可显示文本。对应 JS <c>N.rightText(t, e)</c>。
    /// 默认实现：返回整个 <see cref="Barcode1DEncoder.Text"/>。
    /// </summary>
    protected virtual string RightText() => Text;

    /// <summary>
    /// 右侧编码。对应 JS <c>N.rightEncode(t, e)</c>。
    /// 默认实现：编码空串（基类不实际使用，子类必须重写）。
    /// </summary>
    protected virtual string RightEncode() => EanUpcTables.EncodeDigits(string.Empty, string.Empty);

    /// <summary>
    /// 守护式编码（带分隔符）。对应 JS <c>N.encodeGuarded()</c>。
    /// </summary>
    protected virtual List<BarcodeItem> EncodeGuarded()
    {
        var items = new List<BarcodeItem>
        {
            new(SideBin, string.Empty),
            new(LeftEncode(), LeftText()),
            new(MiddleBin, string.Empty),
            new(RightEncode(), RightText()),
            new(SideBin, string.Empty)
        };

        if (QuietZones > 0)
        {
            var quiet = new string('0', QuietZones);
            items.Insert(0, new BarcodeItem(quiet, GuardWhitespace ? "< " : string.Empty));
            items.Add(new BarcodeItem(quiet, GuardWhitespace ? " >" : string.Empty));
        }
        return items;
    }

    /// <summary>
    /// 扁平式编码（无分隔符，单段）。对应 JS <c>N.encodeFlat()</c>。
    /// </summary>
    protected virtual List<BarcodeItem> EncodeFlat()
    {
        var data = SideBin + LeftEncode() + MiddleBin + RightEncode() + SideBin;
        return new List<BarcodeItem> { new(data, Text) };
    }

    /// <summary>
    /// 编码入口：按 flat 选项选择布局。对应 JS <c>N.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var items = Options.Flat ? EncodeFlat() : EncodeGuarded();
        return new BarcodeEncodeResult
        {
            Options = Options,
            Items = items,
            Text = Text
        };
    }
}

using DzPrinter.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// Code39/Code93 共享字符表与位模式表。对应 JS SDK 中 <c>lt/gt/pt/mt/ft</c> 常量。
/// </summary>
internal static class Code39Tables
{
    /// <summary>
    /// Code39/Code93 字符集（47 字符）。对应 JS <c>lt</c>。
    /// 前 43 字符（0-9 A-Z -. 空格 $/+%）为 Code39 标准字符集；
    /// 后 4 字符（a b c d）为 Code93 扩展移位符。
    /// </summary>
    public const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%abcd";

    /// <summary>
    /// Code39 数字模式（43 项，每项 10 位 0/1）。对应 JS <c>gt</c> 数组。
    /// 索引对应 <see cref="Charset"/> 前 43 字符的位置。
    /// </summary>
    public static readonly string[] Patterns =
    {
        "1112212111", "2112111121", "1122111121", "2122111111", "1112211121",
        "2112211111", "1122211111", "1112112121", "2112112111", "1122112111",
        "2111121121", "1121121121", "2121121111", "1111221121", "2111221111",
        "1121221111", "1111122121", "2111122111", "1121122111", "1111222111",
        "2111111221", "1121111221", "2121111211", "1111211221", "2111211211",
        "1121211211", "1111112221", "2111112211", "1121112211", "1111212211",
        "2211111121", "1221111121", "2221111111", "1211211121", "2211211111",
        "1221211111", "1211112121", "2211112111", "1221112111", "1212121111",
        "1212111211", "1211121211", "1112121211"
    };

    /// <summary>
    /// ECODE39（扩展 Code39）ASCII → 2 字符转义表（128 项）。对应 JS <c>pt</c> 数组。
    /// 每个项是 <see cref="Charset"/> 前 43 字符的 2 字符组合，用于将非标准 ASCII 字符转义为 Code39 可编码序列。
    /// </summary>
    public static readonly string[] ExtendedEscape =
    {
        "%U", "$A", "$B", "$C", "$D", "$E", "$F", "$G", "$H", "$I", "$J", "$K", "$L", "$M", "$N", "$O",
        "$P", "$Q", "$R", "$S", "$T", "$U", "$V", "$W", "$X", "$Y", "$Z", "%A", "%B", "%C", "%D", "%E",
        " ",  "/A", "/B", "/C", "/D", "/E", "/F", "/G", "/H", "/I", "/J", "/K", "/L", "-",  ".",  "/O",
        "0",  "1",  "2",  "3",  "4",  "5",  "6",  "7",  "8",  "9",  "/Z", "%F", "%G", "%H", "%I", "%J",
        "%V", "A",  "B",  "C",  "D",  "E",  "F",  "G",  "H",  "I",  "J",  "K",  "L",  "M",  "N",  "O",
        "P",  "Q",  "R",  "S",  "T",  "U",  "V",  "W",  "X",  "Y",  "Z",  "%K", "%L", "%M", "%N", "%O",
        "%W", "+A", "+B", "+C", "+D", "+E", "+F", "+G", "+H", "+I", "+J", "+K", "+L", "+M", "+N", "+O",
        "+P", "+Q", "+R", "+S", "+T", "+U", "+V", "+W", "+X", "+Y", "+Z", "%P", "%Q", "%R", "%S", "%T"
    };

    /// <summary>
    /// Code93 ASCII → 1-2 字符映射表（128 项）。对应 JS <c>mt</c> 数组。
    /// 每个项是 <see cref="Charset"/> 字符的组合，Code93 编码器构造时用于将输入字符映射为 Code93 内部数据串。
    /// </summary>
    public static readonly string[] Code93Map =
    {
        "bU", "aA", "aB", "aC", "aD", "aE", "aF", "aG", "aH", "aI", "aJ", "aK", "aL", "aM", "aN", "aO",
        "aP", "aQ", "aR", "aS", "aT", "aU", "aV", "aW", "aX", "aY", "aZ", "bA", "bB", "bC", "bD", "bE",
        " ",  "cA", "cB", "cC", "$",  "%",  "cF", "cG", "cH", "cI", "cJ", "+",  "cL", "-",  ".",  "/",
        "0",  "1",  "2",  "3",  "4",  "5",  "6",  "7",  "8",  "9",  "cZ", "bF", "bG", "bH", "bI", "bJ",
        "bV", "A",  "B",  "C",  "D",  "E",  "F",  "G",  "H",  "I",  "J",  "K",  "L",  "M",  "N",  "O",
        "P",  "Q",  "R",  "S",  "T",  "U",  "V",  "W",  "X",  "Y",  "Z",  "bK", "bL", "bM", "bN", "bO",
        "bW", "dA", "dB", "dC", "dD", "dE", "dF", "dG", "dH", "dI", "dJ", "dK", "dL", "dM", "dN", "dO",
        "dP", "dQ", "dR", "dS", "dT", "dU", "dV", "dW", "dX", "dY", "dZ", "bP", "bQ", "bR", "bS", "bT"
    };

    /// <summary>
    /// Code93 数字模式（47 项，每项 6 位 0/1）。对应 JS <c>ft</c> 数组。
    /// 索引对应 <see cref="Charset"/> 47 字符的位置。
    /// </summary>
    public static readonly string[] Code93Patterns =
    {
        "131112", "111213", "111312", "111411", "121113", "121212", "121311", "111114", "131211", "141111",
        "211113", "211212", "211311", "221112", "221211", "231111", "112113", "112212", "112311", "122112",
        "132111", "111123", "111222", "111321", "121122", "131121", "212112", "212211", "211122", "211221",
        "221121", "222111", "112122", "112221", "122121", "123111", "121131", "311112", "311211", "321111",
        "112131", "113121", "211131", "121221", "312111", "311121", "122211"
    };
}

/// <summary>
/// Code39 编码器。对应 JS SDK 中 <c>Ct</c> 类。
/// </summary>
internal class Code39Encoder : Barcode1DEncoder
{
    /// <summary>
    /// 构造：转大写，可选追加 Mod43 校验位。对应 JS <c>Ct</c> 构造函数。
    /// </summary>
    public Code39Encoder(string data, BarcodeEncodeOptions options) : base(NormalizeInput(data, options), options) { }

    private static string NormalizeInput(string data, BarcodeEncodeOptions options)
    {
        data = data.ToUpperInvariant();
        if (options.Mod43)
        {
            var checkChar = Code39Tables.Charset[data.Length % 43];
            // JS: e.text = (e.text || t) + s —— 显示文本追加校验字符
            options.Text = (options.Text ?? data) + checkChar;
            data += checkChar;
        }
        return data;
    }

    /// <summary>
    /// 编码为 Code39 模块序列。对应 JS <c>Ct.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var indices = new List<int>(Data.Length);
        for (var i = 0; i < Data.Length; i++)
        {
            var idx = Code39Tables.Charset.IndexOf(Data[i]);
            if (idx >= 0) indices.Add(idx);
            else DzLogger.Warn($"---- 检测到无效字符[encode with code39]: '{Data[i]}'");
        }

        var sb = new StringBuilder(indices.Count * 10 + 20);
        sb.Append("1211212111");
        for (var i = 0; i < indices.Count; i++)
            sb.Append(Code39Tables.Patterns[indices[i]]);
        sb.Append("121121211");

        var displayText = Options.ShowStartEndChar ? $"*{Text}*" : Text;
        var item = new BarcodeItem(ItfExpander.Encode(sb.ToString()), displayText);
        return new BarcodeEncodeResult
        {
            Options = Options,
            Items = { item },
            Text = Text
        };
    }
}

/// <summary>
/// Code93 编码器。对应 JS SDK 中 <c>Pt</c> 类。
/// Code93 通过 <see cref="Code39Tables.Code93Map"/> 将 ASCII 字符映射为内部数据串，
/// 再使用 <see cref="Code39Tables.Code93Patterns"/> 编码并追加双校验字符（C/K）。
/// </summary>
internal sealed class Code93Encoder : Barcode1DEncoder
{
    public Code93Encoder(string data, BarcodeEncodeOptions options) : base(BuildAndSetText(data, options), options) { }

    /// <summary>
    /// 构造预处理：通过 <see cref="Code39Tables.Code93Map"/> 将输入映射为数据串，并将显示文本写入 options.Text。
    /// 对应 JS <c>Pt</c> 构造中的 IIFE + <c>e.text = i.text</c>。
    /// </summary>
    private static string BuildAndSetText(string input, BarcodeEncodeOptions options)
    {
        var (data, text) = BuildData(input);
        options.Text = text;
        return data;
    }

    /// <summary>
    /// 对应 JS <c>Pt</c> 构造中的 IIFE：遍历输入字符，通过 <see cref="Code39Tables.Code93Map"/> 映射为数据串。
    /// </summary>
    private static (string data, string text) BuildData(string input)
    {
        var dataSb = new StringBuilder();
        var textSb = new StringBuilder();
        var totalLen = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            var code = (int)ch;
            if (code > 127)
            {
                DzLogger.Warn($"---- [encode with code93] invalid char: '{ch}'[{code}]");
                continue;
            }
            var mapped = Code39Tables.Code93Map[code];
            if (totalLen + mapped.Length > 107)
            {
                DzLogger.Warn($"---- content too long, discarded content[{i} --> {input.Length}]: \"{input.Substring(i)}\"");
                break;
            }
            dataSb.Append(mapped);
            // JS: i.push(o > " " && 127 !== n ? o : " ")
            textSb.Append(ch > ' ' && code != 127 ? ch : ' ');
            totalLen += mapped.Length;
        }
        return (dataSb.ToString(), textSb.ToString());
    }

    /// <summary>
    /// 编码为 Code93 模块序列。对应 JS <c>Pt.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var indices = new List<int>(Data.Length + 2);
        for (var i = 0; i < Data.Length; i++)
            indices.Add(Code39Tables.Charset.IndexOf(Data[i]));

        // C 校验位：从右向左，权重 1..20 循环，mod 47
        var cSum = 0;
        var weight = 1;
        for (var i = indices.Count - 1; i >= 0; i--)
        {
            cSum += indices[i] * weight;
            weight++;
            if (weight == 21) weight = 1;
        }
        var cCheck = cSum % 47;
        indices.Add(cCheck);

        // K 校验位：从右向左（含 C），权重 1..15 循环，mod 47
        var kSum = 0;
        weight = 1;
        for (var i = indices.Count - 1; i >= 0; i--)
        {
            kSum += indices[i] * weight;
            weight++;
            if (weight == 16) weight = 1;
        }
        var kCheck = kSum % 47;
        indices.Add(kCheck);

        var sb = new StringBuilder(indices.Count * 6 + 14);
        sb.Append("111141");
        for (var i = 0; i < indices.Count; i++)
            sb.Append(Code39Tables.Code93Patterns[indices[i]]);
        sb.Append("1111411");

        // JS: text = this.options.mod43 ? `${this.text}${lt[i]}${lt[n]}` : this.text
        // 注意 JS 这里复用 mod43 选项来控制是否在显示文本追加 C/K 校验字符
        var displayText = Options.Mod43
            ? $"{Text}{Code39Tables.Charset[cCheck]}{Code39Tables.Charset[kCheck]}"
            : Text;

        return new BarcodeEncodeResult
        {
            Items = { new BarcodeItem(ItfExpander.Encode(sb.ToString()), displayText) },
            Text = Text,
            Options = Options
        };
    }

    /// <summary>
    /// 校验数据是否在 Code93 可编码字符集内。对应 JS <c>Pt.valid()</c>。
    /// </summary>
    public override bool Valid() => Regex.IsMatch(Data, @"^[0-9A-Z\-\.\ \$\/\+\%]+$");
}

/// <summary>
/// 扩展 Code39（ECODE39）编码器。对应 JS SDK 中 <c>At</c> 类。
/// 通过 <see cref="Code39Tables.ExtendedEscape"/> 将任意 ASCII 字符转义为 Code39 字符序列，
/// 然后委托给 <see cref="Code39Encoder"/> 编码。
/// </summary>
internal sealed class ECode39Encoder : Code39Encoder
{
    public ECode39Encoder(string data, BarcodeEncodeOptions options)
        : base(BuildAndSetText(data, options), options) { }

    /// <summary>
    /// 构造预处理：通过 <see cref="Code39Tables.ExtendedEscape"/> 转义输入，并将显示文本写入 options.Text。
    /// 对应 JS <c>At</c> 构造中的 IIFE + <c>e.text = i.text</c>。
    /// </summary>
    private static string BuildAndSetText(string input, BarcodeEncodeOptions options)
    {
        var (data, text) = BuildEscapedData(input);
        options.Text = text;
        return data;
    }

    /// <summary>
    /// 对应 JS <c>At</c> 构造中的 IIFE：遍历输入字符，通过 <see cref="Code39Tables.ExtendedEscape"/> 转义。
    /// </summary>
    private static (string data, string text) BuildEscapedData(string input)
    {
        var dataSb = new StringBuilder();
        var textSb = new StringBuilder();
        var totalLen = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            var code = (int)ch;
            if (code > 127)
            {
                DzLogger.Warn($"---- [ECode39] invalidate char: '{ch}'[{code}]");
                continue;
            }
            var escaped = Code39Tables.ExtendedEscape[code];
            if (totalLen + escaped.Length > 85)
            {
                DzLogger.Warn($"---- discarded content[{i} --> {input.Length}]: \"{input.Substring(i)}\"");
                break;
            }
            textSb.Append(ch);
            dataSb.Append(escaped);
            totalLen += escaped.Length;
        }
        return (dataSb.ToString(), textSb.ToString());
    }

    /// <summary>对应 JS <c>At.valid()</c>。</summary>
    public new bool Valid() => Regex.IsMatch(Data, @"^[0-9A-Z\-\.\ \$\/\+\%]+$");
}

using DzPrinter.Core;
using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// ISBN/EAN 附加码共享的奇偶结构与位模式表。对应 JS SDK 中 <c>K/G/z/q/Z/X/Y</c> 常量。
/// </summary>
internal static class IsbnTables
{
    /// <summary>
    /// UPC-E 奇偶结构表（数字系统 1）。对应 JS <c>K</c> 数组。
    /// 由 It/yt/bt 等 ISBN 变体编码器使用（A=PatternX，B=PatternY）。
    /// </summary>
    public static readonly string[] UpcEParitySystem1 =
    {
        "AAABBB", "AABABB", "AABBAB", "AABBBA", "ABAABB",
        "ABBAAB", "ABBBAA", "ABABAB", "ABABBA", "ABBABA"
    };

    /// <summary>
    /// UPC-E 奇偶结构表（数字系统 0）。对应 JS <c>G</c> 数组。
    /// </summary>
    public static readonly string[] UpcEParitySystem0 =
    {
        "BBBAAA", "BBABAA", "BBAABA", "BBAAAB", "BABBAA",
        "BAABBA", "BAAABB", "BABABA", "BABAAB", "BAABAB"
    };

    /// <summary>EAN-2 结构（按 2 位数字 mod 4 选择）。对应 JS <c>z</c> 数组。</summary>
    public static readonly string[] Ean2Structure = { "AA", "AB", "BA", "BB" };

    /// <summary>EAN-5 结构（按校验和 mod 10 选择）。对应 JS <c>q</c> 数组。</summary>
    public static readonly string[] Ean5Structure =
    {
        "BBAAA", "BABAA", "BAABA", "BAAAB", "ABBAA",
        "AABBA", "AAABB", "ABABA", "ABAAB", "AABAB"
    };

    /// <summary>UPC-E/ISBN-13 主码结构（按首位数字选择）。对应 JS <c>Z</c> 数组。</summary>
    public static readonly string[] UpcEStructure =
    {
        "AAAAA", "ABABB", "ABBAB", "ABBBA", "BAABB",
        "BBAAB", "BBBAA", "BABAB", "BABBA", "BBABA"
    };

    /// <summary>
    /// ISBN 数字宽度模式 A。对应 JS <c>X</c> 数组。每个数字对应 4 位宽度（和为 7）。
    /// 通过 <see cref="ItfExpander.Expand(string)"/> 展开为 0/1 模块序列。
    /// </summary>
    public static readonly string[] PatternX =
    {
        "3211", "2221", "2122", "1411", "1132",
        "1231", "1114", "1312", "1213", "3112"
    };

    /// <summary>ISBN 数字宽度模式 B。对应 JS <c>Y</c> 数组。</summary>
    public static readonly string[] PatternY =
    {
        "1123", "1222", "2212", "1141", "2311",
        "1321", "4111", "2131", "3121", "2113"
    };
}

/// <summary>
/// UPC-E 转换工具与（损坏的）编码器。对应 JS SDK 中 <c>It</c> 类。
/// 仅其静态方法 <see cref="ConvertUpcE2UpcA"/> 与 <see cref="GetUpcECheckCode"/> 被 _t.normalize 用于 UPC-E 规范化；
/// 实例 <see cref="Encode"/> 因 JS Bug 输出仅含起止符，且实际未被调用（被 bt 覆盖）。
/// </summary>
internal sealed class UpcEConverter : Barcode1DEncoder
{
    /// <summary>
    /// 将 UPC-E 7 位数字转换为 UPC-A 11 位数字。对应 JS <c>It.convertUPCE2UPCA(t)</c>。
    /// 算法：按第 7 位（编号位数）选择展开模板，将 UPC-E 压缩位展开为 UPC-A 形式。
    /// </summary>
    public static string ConvertUpcE2UpcA(string text)
    {
        text = ItfExpander.GetDigitText(text);
        if (text.Length > 7) text = text.Substring(0, 7);
        else if (text.Length < 7) text = text.PadLeft(7, '0');

        var e = text.Substring(1);          // 后 6 位
        var i = e[5];                        // 最后一位（决定展开模式）
        var s = new string[11];
        Array.Fill(s, "0");

        if (text[0] == '1') s[0] = "1";
        s[1] = e[0].ToString();
        s[2] = e[1].ToString();

        switch (i)
        {
            case '0':
            case '1':
            case '2':
                s[3] = i.ToString();
                s[8] = e[2].ToString();
                s[9] = e[3].ToString();
                s[10] = e[4].ToString();
                break;
            case '3':
                s[3] = e[2].ToString();
                s[9] = e[3].ToString();
                s[10] = e[4].ToString();
                // JS 原始警告逻辑保留（条件取反触发警告）
                if (e[2] == '0' || e[2] == '1' || e[2] == '2')
                    DzLogger.Warn("271: Invalid UPC-E data, X3 shall not be equal to 0, 1 or ");
                break;
            case '4':
                s[3] = e[2].ToString();
                s[4] = e[3].ToString();
                s[10] = e[4].ToString();
                if (e[3] == '0')
                    DzLogger.Warn("272: Invalid UPC-E data, X4 shall not be equal to 0");
                break;
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
                s[3] = e[2].ToString();
                s[4] = e[3].ToString();
                s[5] = e[4].ToString();
                s[10] = i.ToString();
                if (e[4] == '0')
                    DzLogger.Warn("273: Invalid UPC-E data X5 shall not be equal to 0");
                break;
        }
        return string.Join(string.Empty, s);
    }

    /// <summary>
    /// 计算 UPC-E 校验位（基于展开后的 UPC-A 前 11 位）。对应 JS <c>It.getUPCECheckCode(t)</c>。
    /// </summary>
    public static string GetUpcECheckCode(string text) =>
        ItfExpander.CheckDigit(ConvertUpcE2UpcA(text), 11);

    /// <summary>
    /// 构造：截/补到 7 位数字。对应 JS <c>It</c> 构造函数。
    /// </summary>
    public UpcEConverter(string data, BarcodeEncodeOptions options) : base(Normalize(data), options) { }

    private static string Normalize(string data)
    {
        data = ItfExpander.GetDigitText(data);
        if (data.Length > 7) data = data.Substring(0, 7);
        else if (data.Length < 7) data = data.PadLeft(7, '0');
        return data;
    }

    /// <summary>
    /// 编码 UPC-E。对应 JS <c>It.encode()</c>。
    /// </summary>
    /// <remarks>
    /// JS Bug：原代码 <c>for (let e = 0; e &lt; length; e++)</c> 中 <c>length</c> 未定义，
    /// 循环条件 <c>e &lt; undefined</c> 恒为 false，循环体永不执行。
    /// 因此 <c>patterns</c> 列表始终为空，输出仅含起止符 "111" + "" + "111111"。
    /// <para>该 encode 方法在实际调用链中未被使用（_t 走 _t.create1DBarcode → Et.encode → J.encode），
    /// 仅静态方法 ConvertUpcE2UpcA/GetUpcECheckCode 被 _t.normalize 调用。bug 行为保留以保真。</para>
    /// </remarks>
    public override BarcodeEncodeResult Encode()
    {
        var t = Text;
        var upcA = ConvertUpcE2UpcA(Text);
        var checkDigit = ItfExpander.CheckDigit(upcA, 11);
        t += checkDigit;

        // 原始算法根据 upcA[0] 选择奇偶结构表 K（系统 1）或 G（系统 0），
        // 然后遍历 structure 逐位追加 X/Y 模式。但 JS Bug 致循环不执行，structure 实际未被使用。
        // var structure = upcA[0] == '1'
        //     ? IsbnTables.UpcEParitySystem1[CharUtils.Ctoi(checkDigit)]
        //     : IsbnTables.UpcEParitySystem0[CharUtils.Ctoi(checkDigit)];
        // for (var e = 0; e < ???; e++) {
        //     switch (structure[e]) {
        //         case 'A': patterns.Add(IsbnTables.PatternX[t[e] - '0']); break;
        //         case 'B': patterns.Add(IsbnTables.PatternY[t[e] - '0']); break;
        //     }
        // }

        var patterns = new List<string>();  // 始终为空（JS Bug）
        var expander = new ItfExpander();
        var items = new List<BarcodeItem>
        {
            new(expander.Expand("111"), string.Empty),
            new(expander.Expand(string.Join(string.Empty, patterns)), string.Empty),
            new(expander.Expand("111111"), string.Empty)
        };
        return new BarcodeEncodeResult
        {
            Options = Options,
            Items = items,
            Text = t
        };
    }
}

/// <summary>
/// ISBN EAN-2/EAN-5 附加码编码器。对应 JS SDK 中 <c>yt</c> 类。
/// 同时作为 ISBN-13 主码编码器 <see cref="IsbnEncoder"/> 的基类。
/// </summary>
internal class IsbnAddonEncoder : Barcode1DEncoder
{
    public IsbnAddonEncoder(string data, BarcodeEncodeOptions options)
        : base(ItfExpander.GetDigitText(data), options) { }

    /// <summary>
    /// 编码 EAN-2 或 EAN-5 附加码。对应 JS <c>yt.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var t = Text;
        string structure;
        if (t.Length < 2) t = t.PadLeft(2, '0');
        if (t.Length <= 2)
        {
            var i = 10 * CharUtils.Ctoi(t[0]) + CharUtils.Ctoi(t[1]);
            structure = IsbnTables.Ean2Structure[i % 4];
        }
        else
        {
            if (t.Length < 5) t = t.PadLeft(5, '0');
            var digits = new int[5];
            for (var j = 0; j < 5; j++) digits[j] = CharUtils.Ctoi(t[j]);
            var sum = 3 * (digits[0] + digits[2] + digits[4]);
            sum += 9 * (digits[1] + digits[3]);
            structure = IsbnTables.Ean5Structure[sum % 10];
        }

        var sb = new StringBuilder();
        sb.Append("112");
        for (var s = 0; s < t.Length; s++)
        {
            switch (structure[s])
            {
                case 'A': sb.Append(IsbnTables.PatternX[t[s] - '0']); break;
                case 'B': sb.Append(IsbnTables.PatternY[t[s] - '0']); break;
            }
            // JS Bug: 原代码 s != length - 1 中 length 未定义，永远为 true，
            // 导致每个数字后都追加 "11"（包括最后一个）。此行为保留以保真。
            // 注意：yt.encode 实际被 bt.encode 覆盖，不会被外部调用。
            sb.Append("11");
        }

        return new BarcodeEncodeResult
        {
            Options = Options,
            Items = { new BarcodeItem(ItfExpander.Encode(sb.ToString()), t) },
            Text = t
        };
    }
}

/// <summary>
/// ISBN-13 主码编码器。对应 JS SDK 中 <c>bt</c> 类。
/// 继承自 <see cref="IsbnAddonEncoder"/>（yt）但完全重写 <see cref="Encode"/>。
/// 使用 UPC-E 结构表 Z 与位模式表 X/Y，通过 ItfExpander 实例保持 latch 跨段延续。
/// </summary>
internal class IsbnEncoder : IsbnAddonEncoder
{
    public IsbnEncoder(string data, BarcodeEncodeOptions options)
        : base(ItfExpander.GetDigitText(data), options) { }

    /// <summary>
    /// 编码 ISBN-13 主码。对应 JS <c>bt.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var t = Text;
        if (t.Length > 13) t = t.Substring(0, 13);
        else if (t.Length < 12) t = t.PadLeft(12, '0');

        var checkDigit = ItfExpander.CheckDigit(t, 12);
        if (t.Length == 12) t += checkDigit;
        else if (checkDigit != t[12].ToString()) t = t.Substring(0, 12) + checkDigit;

        var structure = IsbnTables.UpcEStructure[t[0] - '0'];
        var leftPatterns = new List<string>();
        var rightPatterns = new List<string>();
        for (var e = 1; e < t.Length; e++)
        {
            string digitPattern;
            if (e > 1 && e < 7 && structure[e - 2] == 'B')
                digitPattern = IsbnTables.PatternY[t[e] - '0'];
            else
                digitPattern = IsbnTables.PatternX[t[e] - '0'];

            if (e < 7) leftPatterns.Add(digitPattern);
            else rightPatterns.Add(digitPattern);
        }

        var quietZones = Options.QuietZones;
        var quietWidth = Math.Max(quietZones, 2);
        var quiet = new string('0', quietWidth);
        var expander = new ItfExpander();  // 新实例：latch 跨多次 Expand 延续
        var items = new List<BarcodeItem>
        {
            new(quiet, t[0].ToString()),
            new(expander.Expand("111"), string.Empty),
            new(expander.Expand(string.Join(string.Empty, leftPatterns)), t.Substring(1, 6)),
            new(expander.Expand("11111"), string.Empty),
            new(expander.Expand(string.Join(string.Empty, rightPatterns)), t.Substring(7)),
            new(expander.Expand("111"), string.Empty),
            new(quiet, Options.GuardWhitespace ? " >" : string.Empty)
        };
        return new BarcodeEncodeResult
        {
            Items = items,
            Text = t,
            Options = Options
        };
    }
}

/// <summary>
/// ISBN 编码器（带输入规范化）。对应 JS SDK 中 <c>vt</c> 类。
/// 将任意 ISBN 输入（可能含分隔符、X 校验位）规范化为 978/979 前缀的 13 位数字串。
/// </summary>
internal sealed class IsbnNormalizedEncoder : IsbnEncoder
{
    /// <summary>
    /// 计算 ISBN-10 校验位。对应 JS <c>vt.getCheckCode(t, e)</c>。
    /// 算法：权重 1..n 从左到右累加，mod 11，10 → 'X'。
    /// </summary>
    public static string GetCheckCode(string text, int? length = null)
    {
        var sum = 0;
        var weight = 1;
        var n = length ?? text.Length;
        for (var i = 0; i < n; i++)
        {
            sum += CharUtils.Ctoi(text[i]) * weight;
            weight++;
        }
        var r = sum % 11;
        return r == 10 ? "X" : CharUtils.Itoc(r);
    }

    /// <summary>
    /// 从输入中过滤出 ISBN 有效字符（数字 + 末尾 X）。对应 JS <c>vt.filterText(t)</c>。
    /// </summary>
    public static string FilterText(string text)
    {
        var result = new List<char>();
        text = text.ToUpperInvariant();
        for (var i = 0; i < text.Length && result.Count <= 13; i++)
        {
            if (CharUtils.IsDigit(text[i]))
            {
                result.Add(text[i]);
            }
            else if (text[i] == 'X' && result.Count == 9 && i == text.Length - 1)
            {
                result.Add(text[i]);
                break;
            }
        }
        return new string(result.ToArray());
    }

    public IsbnNormalizedEncoder(string data, BarcodeEncodeOptions options) : base(data, options)
    {
        // vt 构造函数主体：规范化 ISBN 输入为 978/979 前缀的 13 位数字串
        var t = FilterText(data);
        if (t.Length > 13) t = t.Substring(0, 13);
        if (t.Length == 13)
        {
            var prefix = t.Substring(0, 3);
            if (prefix != "978" && prefix != "979")
                t = "978" + t.Substring(3);
            var checkDigit = ItfExpander.CheckDigit(t, 12);
            if (t[12].ToString() != checkDigit)
                t = t.Substring(0, 12) + checkDigit;
        }
        else
        {
            if (t.Length > 10) t = t.Substring(0, 10);
            if (t.Length < 9) t = t.PadLeft(9, '0');
            t = "978" + t.Substring(0, 9);
        }
        Data = t;
        Text = t;
    }
}

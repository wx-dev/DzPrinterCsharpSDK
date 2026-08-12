using System.Text;
using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// CODABAR（库德巴码）编码器。对应 JS SDK 中 <c>ut</c> 类。
/// 字符集：0-9、- $ : . / + 与起始/终止符 A B C D。
/// 若输入不含起始/终止符（仅数字与符号），自动以 A...A 包围。
/// </summary>
internal sealed class CodabarEncoder : Barcode1DEncoder
{
    /// <summary>
    /// CODABAR 字符 → 9 位（数字/-$）或 10 位（:+. 与 A-D）模块模式表。
    /// 对应 JS <c>ut.getEncodings()</c> 返回的对象。
    /// </summary>
    private static readonly Dictionary<char, string> Encodings = new()
    {
        ['0'] = "101010011",
        ['1'] = "101011001",
        ['2'] = "101001011",
        ['3'] = "110010101",
        ['4'] = "101101001",
        ['5'] = "110101001",
        ['6'] = "100101011",
        ['7'] = "100101101",
        ['8'] = "100110101",
        ['9'] = "110100101",
        ['-'] = "101001101",
        ['$'] = "101100101",
        [':'] = "1101011011",
        ['/'] = "1101101011",
        ['.'] = "1101101101",
        ['+'] = "1011011011",
        ['A'] = "1011001001",
        ['B'] = "1001001011",
        ['C'] = "1010010011",
        ['D'] = "1010011001"
    };

    /// <summary>
    /// 构造：若输入仅含数字与符号则自动加 A...A 包围；转大写。
    /// 对应 JS <c>ut</c> 构造函数。
    /// </summary>
    public CodabarEncoder(string data, BarcodeEncodeOptions options) : base(Normalize(data), options)
    {
        // JS: this.text = this.options.text || this.text.replace(/[A-D]/g, "")
        if (string.IsNullOrEmpty(Options.Text))
            Text = Regex.Replace(Text, @"[A-D]", string.Empty);
    }

    private static string Normalize(string data)
    {
        // JS: 0 === t.search(/^[0-9\-\$\:\.\+\/]+$/) && (t = "A" + t + "A")
        if (Regex.IsMatch(data, @"^[0-9\-\$\:\.\+\/]+$")) data = "A" + data + "A";
        return data.ToUpperInvariant();
    }

    /// <summary>对应 JS <c>ut.valid()</c>。</summary>
    public override bool Valid() => Regex.IsMatch(Data, @"^[A-D][0-9\-\$\:\.\+\/]+[A-D]$");

    /// <summary>
    /// 编码为 CODABAR 模块序列。对应 JS <c>ut.encode()</c>。
    /// 每个字符之间插入一个 "0" 间隔位。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var sb = new StringBuilder(Data.Length * 11);
        for (var i = 0; i < Data.Length; i++)
        {
            sb.Append(Encodings[Data[i]]);
            if (i != Data.Length - 1) sb.Append('0');
        }
        return new BarcodeEncodeResult
        {
            Items = { new BarcodeItem(sb.ToString(), Text) },
            Text = Text,
            Options = Options
        };
    }
}

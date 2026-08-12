using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// Code25 系列条码共享的数字位模式表与起止符。对应 JS SDK 中 <c>Q/tt/et/it/st/nt/rt</c> 常量。
/// </summary>
internal static class Code25Tables
{
    /// <summary>
    /// Code25 Industrial25 数字模式（2 of 5 工业码）。对应 JS <c>Q</c> 数组。
    /// 每个数字对应 6 位 0/1 模块（2 宽 3 窄）。
    /// JS: <c>Q=["112211","211121","121121","221111","112121","212111","122111","111221","211211","121211"]</c>。
    /// </summary>
    public static readonly string[] IndustrialPatterns =
    {
        "112211", "211121", "121121", "221111", "112121",
        "212111", "122111", "111221", "211211", "121211"
    };

    /// <summary>
    /// Code25 Matrix25/ChinaPost 数字模式。对应 JS <c>tt</c> 数组。
    /// 每个数字对应 10 位 0/1 模块。
    /// JS: <c>tt=["1111212111","2111111121","1121111121","2121111111","1111211121","2111211111","1121211111","1111112121","2111112111","1121112111"]</c>。
    /// </summary>
    public static readonly string[] MatrixPatterns =
    {
        "1111212111", "2111111121", "1121111121", "2121111111", "1111211121",
        "2111211111", "1121211111", "1111112121", "2111112111", "1121112111"
    };

    /// <summary>
    /// Code25 ITF25 交叉 25 数字模式。对应 JS <c>et</c> 数组。
    /// 每个数字对应 5 位 0/1 模块（2 宽 3 窄）。
    /// JS: <c>et=["11221","21112","12112","22111","11212","21211","12211","11122","21121","12121"]</c>。
    /// </summary>
    public static readonly string[] ItfPatterns =
    {
        "11221", "21112", "12112", "22111", "11212",
        "21211", "12211", "11122", "21121", "12121"
    };

    /// <summary>ITF25 起止符。对应 JS <c>it=["1111","211"]</c>。</summary>
    public static readonly string[] ItfStartStop = { "1111", "211" };

    /// <summary>Matrix25 起止符。对应 JS <c>st=["411111","41111"]</c>。</summary>
    public static readonly string[] MatrixStartStop = { "411111", "41111" };

    /// <summary>Industrial25 起止符。对应 JS <c>nt=["212111","21112"]</c>。</summary>
    public static readonly string[] IndustrialStartStop = { "212111", "21112" };

    /// <summary>ChinaPost 起止符。对应 JS <c>rt=["1111","211"]</c>（与 it 相同）。</summary>
    public static readonly string[] ChinaPostStartStop = { "1111", "211" };
}

/// <summary>
/// ITF25（交叉 25 码）编码器，亦是 Code25 系列的基类。对应 JS SDK 中 <c>at</c> 类。
/// </summary>
/// <remarks>
/// JS 中 <c>at</c> 继承自 <c>m</c>，构造时通过 <c>V.getDigitText(t)</c> 抽取数字部分。
/// 子类 <c>ot/ct/ht/dt</c> 复用其静态方法 <c>gs1_check_digit</c>/<c>c25_common</c>/<c>c25_inter_common</c>。
/// </remarks>
internal class Code25ItfEncoder : Barcode1DEncoder
{
    /// <summary>
    /// 构造并抽取输入中的数字部分。对应 JS <c>at</c> 构造函数 <c>super(t=V.getDigitText(t), e)</c>。
    /// </summary>
    public Code25ItfEncoder(string data, BarcodeEncodeOptions options)
        : base(ItfExpander.GetDigitText(data), options) { }

    /// <summary>
    /// Code25 系列校验位（GS1/ITF 风格）。对应 JS <c>at.gs1_check_digit(t, e)</c>。
    /// 算法：从左到右交替权重 3/1（起始权重取决于长度奇偶），加和后用 10*ceil(sum/10) - sum。
    /// </summary>
    /// <param name="text">输入数字串。</param>
    /// <param name="length">可选目标长度；超过则截断，不足则前置补 0。</param>
    /// <returns>校验位字符。</returns>
    public static string Gs1CheckDigit(string text, int? length = null)
    {
        if (length.HasValue)
        {
            var len = length.Value;
            if (len > text.Length) text = text.PadLeft(len, '0');
            else if (len < text.Length) text = text.Substring(0, len);
        }
        var weight = (text.Length & 1) == 1 ? 3 : 1;
        var sum = 0;
        for (var i = 0; i < text.Length; i++)
        {
            sum += weight * (text[i] - '0');
            weight ^= 2;  // 1 ↔ 3
        }
        return (10 * (int)Math.Ceiling(sum / 10.0) - sum).ToString();
    }

    /// <summary>
    /// Code25 非交叉式通用编码。对应 JS <c>at.c25_common(t, e, i, s, n)</c>。
    /// </summary>
    /// <param name="data">原始数据字符串。</param>
    /// <param name="startStop">起止符数组 [start, end]。</param>
    /// <param name="useIndustrialTable">true 使用 <see cref="Code25Tables.IndustrialPatterns"/>（Q 表），false 使用 <see cref="Code25Tables.MatrixPatterns"/>（tt 表）。</param>
    /// <param name="maxLength">最大长度截断（0 或 null 不截断）。</param>
    /// <param name="options">编码选项。</param>
    /// <returns>编码结果。</returns>
    public static BarcodeEncodeResult C25Common(string data, string[] startStop, bool useIndustrialTable, int? maxLength, BarcodeEncodeOptions options)
    {
        data = ItfExpander.GetDigitText(data);
        if (maxLength.HasValue && maxLength.Value > 0 && data.Length > maxLength.Value)
            data = data.Substring(0, maxLength.Value);
        if (options.CheckDigit) data += Gs1CheckDigit(data);

        var patterns = useIndustrialTable ? Code25Tables.IndustrialPatterns : Code25Tables.MatrixPatterns;
        var sb = new StringBuilder(data.Length * 10 + 20);
        sb.Append(startStop[0]);
        for (var i = 0; i < data.Length; i++)
            sb.Append(patterns[data[i] - CharConstants.Num0]);
        sb.Append(startStop[1]);

        return new BarcodeEncodeResult
        {
            Options = options,
            Items = { new BarcodeItem(ItfExpander.Encode(sb.ToString()), data) },
            Text = data
        };
    }

    /// <summary>
    /// ITF25 交叉式通用编码。对应 JS <c>at.c25_inter_common(t, e)</c>。
    /// 算法：将数字两两一组，按位交错（每组 5 个 bit 中第 i 位来自两个数字的第 i 位）。
    /// </summary>
    /// <param name="data">原始数据字符串。</param>
    /// <param name="options">编码选项。</param>
    /// <returns>编码结果。</returns>
    public static BarcodeEncodeResult C25InterCommon(string data, BarcodeEncodeOptions options)
    {
        data = ItfExpander.GetDigitText(data);
        if (data.Length > 125) data = data.Substring(0, 125);
        // JS Bug-ish: 条件 (len%2==1 && !checkDigit) || (len%2==1 && checkDigit) 等价于 len%2==1
        // 但保留 JS 原始结构以体现保真度
        if ((data.Length % 2 == 1 && !options.CheckDigit) || (data.Length % 2 == 1 && options.CheckDigit))
            data = "0" + data;
        if (options.CheckDigit) data += Gs1CheckDigit(data);

        var sb = new StringBuilder(data.Length * 5 + 10);
        sb.Append(Code25Tables.ItfStartStop[0]);
        for (var i = 0; i < data.Length; i += 2)
        {
            var a = Code25Tables.ItfPatterns[data[i] - '0'];
            var b = Code25Tables.ItfPatterns[data[i + 1] - '0'];
            for (var j = 0; j < 5; j++) sb.Append(a[j]).Append(b[j]);
        }
        sb.Append(Code25Tables.ItfStartStop[1]);

        return new BarcodeEncodeResult
        {
            Options = options,
            Items = { new BarcodeItem(ItfExpander.Encode(sb.ToString()), data) },
            Text = data
        };
    }

    /// <summary>
    /// ITF25 编码入口。对应 JS <c>at.encode()</c>。
    /// </summary>
    public override BarcodeEncodeResult Encode() => C25InterCommon(Data, Options);
}

/// <summary>
/// Matrix25（矩阵 25 码）编码器。对应 JS SDK 中 <c>ot</c> 类。
/// 使用 Q 表（IndustrialPatterns）与 st 起止符，最大 80 位。
/// </summary>
internal sealed class Code25MatrixEncoder : Code25ItfEncoder
{
    public Code25MatrixEncoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    /// <summary>对应 JS <c>ot.encode()</c>：c25_common(data, st, true, 80, options)。</summary>
    public override BarcodeEncodeResult Encode() =>
        C25Common(Data, Code25Tables.MatrixStartStop, true, 80, Options);
}

/// <summary>
/// Industrial25（工业 25 码）编码器。对应 JS SDK 中 <c>ct</c> 类。
/// 使用 tt 表（MatrixPatterns）与 nt 起止符，最大 45 位。
/// </summary>
/// <remarks>
/// JS 中 <c>ct</c> 调用 <c>c25_common(this.data, nt, !1, 45, this.options)</c>，
/// 第三个参数为 false，即使用 tt 表而非 Q 表。此为 JS 原始设计，忠实保留。
/// </remarks>
internal sealed class Code25IndustrialEncoder : Code25ItfEncoder
{
    public Code25IndustrialEncoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    /// <summary>对应 JS <c>ct.encode()</c>：c25_common(data, nt, false, 45, options)。</summary>
    public override BarcodeEncodeResult Encode() =>
        C25Common(Data, Code25Tables.IndustrialStartStop, false, 45, Options);
}

/// <summary>
/// ChinaPost（中国邮政码）编码器。对应 JS SDK 中 <c>ht</c> 类。
/// 使用 Q 表（IndustrialPatterns）与 rt 起止符，最大 80 位。
/// </summary>
internal sealed class Code25ChinaPostEncoder : Code25ItfEncoder
{
    public Code25ChinaPostEncoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    /// <summary>对应 JS <c>ht.encode()</c>：c25_common(data, rt, true, 80, options)。</summary>
    public override BarcodeEncodeResult Encode() =>
        C25Common(Data, Code25Tables.ChinaPostStartStop, true, 80, Options);
}

/// <summary>
/// ITF14（14 位交叉 25 码）编码器。对应 JS SDK 中 <c>dt</c> 类。
/// 固定 13 位数据 + 1 位校验位，使用 ITF25 交叉式编码。
/// </summary>
internal sealed class Code25Itf14Encoder : Code25ItfEncoder
{
    public Code25Itf14Encoder(string data, BarcodeEncodeOptions options) : base(data, options) { }

    /// <summary>
    /// 对应 JS <c>dt.encode()</c>：
    /// 截/补到 13 位，追加 gs1_check_digit，然后 c25_inter_common。
    /// 同时将处理后的数据回写到 <see cref="Barcode1DEncoder"/> 字段。
    /// </summary>
    public override BarcodeEncodeResult Encode()
    {
        var t = Data;
        if (t.Length > 13) t = t.Substring(0, 13);
        else if (t.Length < 13) t = t.PadLeft(13, '0');
        t += Gs1CheckDigit(t);
        Data = t;
        Text = t;
        return C25InterCommon(t, Options);
    }
}

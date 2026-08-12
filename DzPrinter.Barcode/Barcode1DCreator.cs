using DzPrinter.Core;
using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// 1D 条码生成请求。对应 JS SDK 中传入 <c>_t.create1DBarcode(e)</c> 的 <c>e</c> 对象。
/// 字段与 JS 中读取的属性保持一致。
/// </summary>
public sealed class Barcode1DRequest
{
    /// <summary>
    /// 文本内容。可为字符串或数字（数字将转为字符串）。对应 JS <c>e.text</c>。
    /// JS 中 <c>typeof e.text === "number"</c> 时通过模板字符串转换。
    /// </summary>
    public object? Text { get; set; }

    /// <summary>
    /// 备用内容（当 <see cref="Text"/> 为空时使用）。对应 JS <c>e.content</c>。
    /// JS 中若 content 为 undefined/null 则使用空字符串。
    /// </summary>
    public object? Content { get; set; }

    /// <summary>
    /// 条码类型（优先级高于 <see cref="Type"/>）。对应 JS <c>e.barcodeType</c>。
    /// </summary>
    public BarcodeType? BarcodeType { get; set; }

    /// <summary>
    /// 条码类型别名（与 <see cref="BarcodeType"/> 互为别名，<see cref="BarcodeType"/> 优先）。
    /// 对应 JS <c>e.type</c>。
    /// </summary>
    public BarcodeType? Type { get; set; }

    /// <summary>
    /// 是否显示首尾字符（如 Code39 的 *）。对应 JS <c>e.showStartEnd</c>。
    /// 该值会被复制到 <see cref="BarcodeEncodeOptions.ShowStartEndChar"/>。
    /// </summary>
    public bool ShowStartEnd { get; set; }
}

/// <summary>
/// 自定义条码编码器接口。对应 JS 中 <c>Et.registerEncoder</c> 接受的带 <c>encode(t, e)</c> 方法的对象。
/// 实现类需为无状态（每次 <see cref="Encode"/> 调用均基于传入参数构造）。
/// </summary>
public interface IBarcodeEncoder
{
    /// <summary>
    /// 编码生成条码。对应 JS <c>encoder.encode(text, options)</c>。
    /// </summary>
    /// <param name="text">已规范化的文本。</param>
    /// <param name="options">编码选项。</param>
    BarcodeEncodeResult Encode(string text, BarcodeEncodeOptions options);
}

/// <summary>
/// 1D 条码编码器注册表与调度入口。对应 JS SDK 中 <c>Et</c> 类。
/// 根据 <see cref="BarcodeType"/> 查找已注册的自定义编码器或回退到内置实现。
/// </summary>
internal static class BarcodeEncoderRegistry
{
    /// <summary>已注册的自定义编码器映射。对应 JS <c>Et.registedEncoderMap = new Map</c>。</summary>
    private static readonly Dictionary<BarcodeType, IBarcodeEncoder> s_registeredEncoders = new();

    /// <summary>
    /// 注册自定义编码器。对应 JS <c>Et.registerEncoder(t, e)</c>。
    /// </summary>
    public static void RegisterEncoder(BarcodeType type, IBarcodeEncoder encoder)
        => s_registeredEncoders[type] = encoder;

    /// <summary>
    /// 编码生成条码。对应 JS <c>Et.encode(t)</c>。
    /// 流程：
    /// <list type="number">
    ///   <item>默认 displayValue = true（C# 中由 <see cref="BarcodeEncodeOptions.DisplayValue"/> 默认值实现）。</item>
    ///   <item>获取编码器并调用其 Encode。</item>
    ///   <item>后处理：若 result.data 为空则拼接 items.data；若 result.options 为空则用传入 options。</item>
    /// </list>
    /// </summary>
    public static BarcodeEncodeResult Encode(BarcodeType type, string text, BarcodeEncodeOptions options)
    {
        var result = InvokeEncoder(type, text, options);

        // JS: i.data || (i.data = i.items.map(t=>t.data).join(""))
        if (string.IsNullOrEmpty(result.Data))
            result.Data = string.Concat(result.Items.Select(i => i.Data));

        // JS: i.options || (i.options = e.options)
        result.Options ??= options;

        return result;
    }

    /// <summary>
    /// 根据 type 查找已注册编码器或回退到内置 switch。对应 JS <c>Et.getEncoder(e)</c>。
    /// JS 中 <c>case t.BarcodeType.CODE128: default</c> 合并，CODE128 与 AUTO（及任何未知类型）均走 S 类。
    /// </summary>
    private static BarcodeEncodeResult InvokeEncoder(BarcodeType type, string text, BarcodeEncodeOptions options)
    {
        // JS: const n = this.registedEncoderMap.get(i); if (n && typeof n.encode === 'function') return n;
        if (s_registeredEncoders.TryGetValue(type, out var registered))
            return registered.Encode(text, options);

        // JS: switch (i) { case ...: return new X(s, e); ... }
        // 注意：CODE128 case 在 JS 中返回 new S(s, e)，S 为自动模式选择类；
        // C# 中 S 类逻辑由 Code128Encoder.CreateAuto 静态方法承载。
        return type switch
        {
            BarcodeType.UPC_A => new UpcAEncoder(text, options).Encode(),
            BarcodeType.UPC_E => new UpcEEncoder(text, options).Encode(),
            BarcodeType.EAN13 => new Ean13Encoder(text, options).Encode(),
            BarcodeType.EAN8 => new Ean8Encoder(text, options).Encode(),
            BarcodeType.CODE39 => new Code39Encoder(text, options).Encode(),
            BarcodeType.ITF25 => new Code25ItfEncoder(text, options).Encode(),
            BarcodeType.CODABAR => new CodabarEncoder(text, options).Encode(),
            BarcodeType.CODE93 => new Code93Encoder(text, options).Encode(),
            BarcodeType.ISBN => new IsbnNormalizedEncoder(text, options).Encode(),
            BarcodeType.ECODE39 => new ECode39Encoder(text, options).Encode(),
            BarcodeType.ITF14 => new Code25Itf14Encoder(text, options).Encode(),
            BarcodeType.ChinaPost => new Code25ChinaPostEncoder(text, options).Encode(),
            BarcodeType.Matrix25 => new Code25MatrixEncoder(text, options).Encode(),
            BarcodeType.Industrial25 => new Code25IndustrialEncoder(text, options).Encode(),
            // CODE128 与 AUTO（默认）走 S 类自动模式选择
            BarcodeType.CODE128 => Code128Encoder.CreateAuto(text, options).Encode(),
            _ => Code128Encoder.CreateAuto(text, options).Encode()
        };
    }
}

/// <summary>
/// 1D 条码创建器（统一入口）。对应 JS SDK 中 <c>_t</c> 类。
/// 提供 <see cref="Create1DBarcode"/> 公共 API 与 <see cref="Normalize"/> 输入规范化。
/// </summary>
public static class Barcode1DCreator
{
    /// <summary>
    /// 产品类条码（EAN13/EAN8/UPC_A/UPC_E/ISBN）的水平静区边距。
    /// 对应 JS <c>_t.DockHorMargin = 7</c>。
    /// </summary>
    public const int DockHorMargin = 7;

    /// <summary>
    /// 注册自定义条码创建器。对应 JS <c>_t.registerBarcodeCreator(t, e)</c>。
    /// 内部转调 <see cref="BarcodeEncoderRegistry.RegisterEncoder"/>。
    /// </summary>
    public static void RegisterBarcodeCreator(BarcodeType type, IBarcodeEncoder encoder)
        => BarcodeEncoderRegistry.RegisterEncoder(type, encoder);

    /// <summary>
    /// 判断指定类型是否为产品类条码。对应 JS <c>_t.IsProductType(e)</c>。
    /// 产品类条码（EAN13/EAN8/UPC_A/UPC_E/ISBN）使用 <see cref="DockHorMargin"/> 作为水平静区。
    /// </summary>
    public static bool IsProductType(BarcodeType type) => type switch
    {
        BarcodeType.EAN13 => true,
        BarcodeType.EAN8 => true,
        BarcodeType.UPC_A => true,
        BarcodeType.UPC_E => true,
        BarcodeType.ISBN => true,
        _ => false
    };

    /// <summary>
    /// 创建 1D 条码。对应 JS <c>_t.create1DBarcode(e)</c>。
    /// 流程：
    /// <list type="number">
    ///   <item>解析类型：<see cref="Barcode1DRequest.BarcodeType"/> 优先于 <see cref="Barcode1DRequest.Type"/>，缺省为 <see cref="BarcodeType.AUTO"/>。</item>
    ///   <item>解析文本：数字 → 字符串；字符串 → trim；空则回退到 <see cref="Barcode1DRequest.Content"/>。</item>
    ///   <item>规范化文本：<see cref="Normalize"/>。</item>
    ///   <item>设置静区：产品类使用 <see cref="DockHorMargin"/>。</item>
    ///   <item>调用 <see cref="BarcodeEncoderRegistry.Encode"/>。</item>
    /// </list>
    /// </summary>
    /// <returns>编码结果；若文本为空或编码抛异常则返回 null（JS 中返回 undefined）。</returns>
    public static BarcodeEncodeResult? Create1DBarcode(Barcode1DRequest request)
    {
        // JS: let s = t.BarcodeType.AUTO; "number" == typeof e.barcodeType ? s = e.barcodeType : "number" == typeof i.type && (s = i.type);
        var type = request.BarcodeType ?? request.Type ?? BarcodeType.AUTO;

        // JS: let n = "number" == typeof e.text ? `${e.text}` : (e.text||"").trim();
        string text;
        if (request.Text == null)
            text = string.Empty;
        else if (request.Text is string s)
            text = s.Trim();
        else
            text = request.Text.ToString() ?? string.Empty;

        // JS: if (n || (n = void 0 === e.content || null === e.content ? "" : String(e.content), n)) { ... }
        if (string.IsNullOrEmpty(text))
            text = request.Content?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(text))
            return null;

        text = Normalize(text, type);

        // JS: let i = 0; switch (s) { case EAN13/UPC_A/UPC_E/ISBN: i = _t.DockHorMargin; }
        var quietZones = IsProductType(type) ? DockHorMargin : 0;

        var options = new BarcodeEncodeOptions
        {
            Text = text,
            QuietZones = quietZones,
            ShowStartEndChar = request.ShowStartEnd
        };

        try
        {
            return BarcodeEncoderRegistry.Encode(type, text, options);
        }
        catch (Exception ex)
        {
            // JS: catch (e) { return void a.warn(e); }
            DzLogger.Warn(ex.ToString());
            return null;
        }
    }

    /// <summary>
    /// 按类型规范化输入文本。对应 JS <c>_t.normalize(e, i)</c>。
    /// 不同类型的规范化规则：
    /// <list type="bullet">
    ///   <item>CODE39/CODE93：截 107 字符，非 ASCII 替换为 '?'。</item>
    ///   <item>CODABAR：处理首尾包围符（A/B/C/D 或 T/N/*/E），中间非合法字符替换为 '0'。</item>
    ///   <item>EAN13：补/截 12 位数字 + 校验位。</item>
    ///   <item>EAN8：补/截 7 位数字 + 校验位。</item>
    ///   <item>UPC_A：补/截 11 位数字 + 校验位。</item>
    ///   <item>UPC_E：补/截 7 位数字，首位非 0/1 时改为 0，+ 校验位（用 <see cref="UpcEConverter.GetUpcECheckCode"/>）。</item>
    ///   <item>ITF14：补/截 13 位数字 + 校验位。</item>
    ///   <item>ITF25：截 80 位数字，奇数长度前补 '0'。</item>
    ///   <item>GS1_128：处理 AI 括号（() 或 []），无括号时按长度前置 (10)/(90)/(91)。</item>
    ///   <item>其他：截 80 字符，非可打印 ASCII 替换为 '?'。</item>
    /// </list>
    /// </summary>
    public static string Normalize(string text, BarcodeType type)
    {
        if (type == BarcodeType.CODE39 || type == BarcodeType.CODE93)
        {
            // JS: e = this.normalizeLength(e, 107, t => t >= 0 && t < 128, "?");
            text = NormalizeLength(text, 107, c => c >= 0 && c < 128, '?');
        }
        else
        {
            if (type == BarcodeType.CODABAR)
            {
                // JS: const t = "0123456789-$:/.+ABCD";
                const string validChars = "0123456789-$:/.+ABCD";
                char first, last;
                if (text.Length >= 2)
                {
                    // JS: const t = "ABCD", n = "TN*E";
                    const string abcd = "ABCD";
                    const string tnstare = "TN*E";
                    first = char.ToUpperInvariant(text[0]);
                    last = char.ToUpperInvariant(text[text.Length - 1]);
                    var r = abcd.IndexOf(first) >= 0 && abcd.IndexOf(last) >= 0;
                    var a = tnstare.IndexOf(first) >= 0 && tnstare.IndexOf(last) >= 0;
                    if (r || a)
                        text = text.Substring(1, text.Length - 2);
                    else
                    {
                        first = 'A';
                        last = 'A';
                    }
                }
                else
                {
                    first = 'A';
                    last = 'A';
                }

                // JS: e = this.normalizeLength(e, 0, e => t.indexOf(String.fromCharCode(e)) >= 0, "0")
                text = NormalizeLength(text, 0, c => validChars.IndexOf((char)c) >= 0, '0');
                return first.ToString() + text + last.ToString();
            }

            if (type == BarcodeType.EAN13)
            {
                text = NormalizeDigitLength(text, 12);
                text += GetEan13CheckCode(text);
            }
            else if (type == BarcodeType.EAN8)
            {
                text = NormalizeDigitLength(text, 7);
                text += GetEan8CheckCode(text);
            }
            else if (type == BarcodeType.UPC_A)
            {
                text = NormalizeDigitLength(text, 11);
                text += ItfExpander.CheckDigit(text);
            }
            else if (type == BarcodeType.UPC_E)
            {
                text = NormalizeDigitLength(text, 7);
                var first = text[0];
                if (first != '0' && first != '1') text = "0" + text.Substring(1);
                text += UpcEConverter.GetUpcECheckCode(text);
            }
            else if (type == BarcodeType.ITF14)
            {
                text = NormalizeDigitLength(text, 13);
                text += GetItd14CheckCode(text);
            }
            else
            {
                if (type == BarcodeType.ITF25)
                {
                    // JS: return (e = this.normalizeLength(e, 80, t => g.isDigit(t), "0")).length % 2 != 0 ? "0" + e : e;
                    text = NormalizeLength(text, 80, c => CharUtils.IsDigit(c), '0');
                    return text.Length % 2 != 0 ? "0" + text : text;
                }

                if (type == BarcodeType.GS1_128)
                {
                    // JS: 处理 AI 括号配对。若首字符为 '(' 但无 ')'，将首个 ']' 改为 ')'；
                    //     若首字符为 '[' 但无 ']'，将首个 ')' 改为 ']'；
                    //     否则按长度前置 AI 标识 (10)/(90)/(91)。
                    if (text.Length > 0 && text[0] == '(')
                    {
                        if (text.IndexOf(')') < 0)
                        {
                            var idx = text.IndexOf(']');
                            if (idx > 0) text = text.Substring(0, idx) + ")" + text.Substring(idx + 1);
                        }
                    }
                    else if (text.Length > 0 && text[0] == '[')
                    {
                        if (text.IndexOf(']') < 0)
                        {
                            var idx = text.IndexOf(')');
                            if (idx > 0) text = text.Substring(0, idx) + "]" + text.Substring(idx + 1);
                        }
                    }
                    else
                    {
                        // JS: e.length < 2 && (e = "0" + e), e = e.length <= 20 ? "(10)" + e : e.length <= 30 ? "(90)" + e : "(91)" + e
                        if (text.Length < 2) text = "0" + text;
                        text = text.Length <= 20 ? "(10)" + text
                             : text.Length <= 30 ? "(90)" + text
                             : "(91)" + text;
                    }
                    // 注意：GS1_128 不应用 normalizeLength，直接 fall through 到 return e
                }
                else
                {
                    // JS: e = this.normalizeLength(e, 80, t => t >= 32 && t <= 126, "?");
                    text = NormalizeLength(text, 80, c => c >= 32 && c <= 126, '?');
                }
            }
        }
        return text;
    }

    /// <summary>
    /// 规范化数字串长度。对应 JS <c>_t.normalizeDigitLength(t, e)</c>。
    /// 先用 <see cref="NormalizeLength"/> 截断到指定长度并将非数字替换为 '0'，
    /// 若仍短于指定长度则前置补 '0'。
    /// </summary>
    private static string NormalizeDigitLength(string text, int targetLength)
    {
        text = NormalizeLength(text, targetLength, c => CharUtils.IsDigit(c), '0');
        if (text.Length < targetLength)
            text = CharUtils.PreFillChar(text, targetLength, '0');
        return text;
    }

    /// <summary>
    /// 通用长度规范化。对应 JS <c>_t.normalizeLength(t, e, i, s)</c>。
    /// 若超过最大长度则截断；对每个字符调用 <paramref name="isValid"/>，不通过的替换为 <paramref name="replacement"/>。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="maxLength">最大长度（&lt;= 0 表示不截断）。</param>
    /// <param name="isValid">字符码点合法性判定谓词；为 null 则不替换。</param>
    /// <param name="replacement">非法字符的替换字符。</param>
    private static string NormalizeLength(string text, int maxLength, Func<int, bool>? isValid, char replacement)
    {
        if (maxLength > 0 && text.Length > maxLength)
            text = text.Substring(0, maxLength);

        if (isValid == null) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(isValid((int)ch) ? ch : replacement);
        return sb.ToString();
    }

    /// <summary>
    /// 计算 EAN-13 校验位。对应 JS <c>_t.getEAN13CheckCode(t)</c>。
    /// 算法：12 位数字，偶数位 ×1 + 奇数位 ×3（索引从 0 开始），加和取模 10 后用 10 减。
    /// </summary>
    /// <remarks>
    /// JS 原始异常消息为 <c>"检测到非发字符"</c>（疑似"非数字字符"或"非法字符"的笔误），此处保留以保真。
    /// </remarks>
    public static string GetEan13CheckCode(string text)
    {
        var sum = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var d = text[i] - CharConstants.Num0;
            if (d < 0 || d > 9)
                throw new InvalidOperationException("检测到非发字符");
            // JS: e += (1 & i ? 3 : 1) * s —— 索引为奇数时权重 3，偶数时权重 1
            sum += ((i & 1) == 1 ? 3 : 1) * d;
        }

        // JS: e = 10 - e % 10; if (10 == e) e = 0;
        var check = 10 - sum % 10;
        if (check == 10) check = 0;
        return check.ToString();
    }

    /// <summary>
    /// 计算 EAN-8 校验位。对应 JS <c>_t.getEAN8CheckCode(t)</c>。
    /// 直接转调 <see cref="ItfExpander.CheckDigit(string)"/>（JS 中 <c>V.checkDigit(t)</c>）。
    /// </summary>
    public static string GetEan8CheckCode(string text) => ItfExpander.CheckDigit(text);

    /// <summary>
    /// 计算 ITF-14 校验位。对应 JS <c>_t.getITD14CheckCode(t)</c>。
    /// 直接转调 <see cref="ItfExpander.CheckDigit(string)"/>（JS 中 <c>V.checkDigit(t)</c>）。
    /// </summary>
    public static string GetItd14CheckCode(string text) => ItfExpander.CheckDigit(text);
}

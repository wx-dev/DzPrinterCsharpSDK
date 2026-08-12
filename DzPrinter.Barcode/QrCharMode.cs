using System.Text.RegularExpressions;

namespace DzPrinter.Barcode;

/// <summary>
/// QR 码字符模式判定与分段正则。对应 JS SDK 中 <c>kt</c> 类与相关常量。
/// </summary>
internal static class QrCharMode
{
    /// <summary>
    /// 数字模式正则源。对应 JS <c>const Ut = "[0-9]+"</c>。
    /// </summary>
    private const string NumericPattern = "[0-9]+";

    /// <summary>
    /// 汉字模式正则源（含日文假名、CJK 标点等）。
    /// 对应 JS <c>Ft</c> 在 <c>replace(/u/g, "\\u")</c> 之后的字符串。
    /// JS 原始字符串为 <c>"(?:[u3000-u303F]|...|u203B|[u2010u2015...])+"</c>，
    /// 通过 replace 将所有 u 替换为 \u 得到正确的 Unicode 转义。
    /// </summary>
    public const string KanjiPattern =
        @"(?:[\u3000-\u303F]|[\u3040-\u309F]|[\u30A0-\u30FF]|[\uFF00-\uFFEF]|[\u4E00-\u9FAF]|[\u2605-\u2606]|[\u2190-\u2195]|\u203B|[\u2010\u2015\u2018\u2019\u2025\u2026\u201C\u201D\u2225\u2260]|[\u0391-\u0451]|[\u00A7\u00A8\u00B1\u00B4\u00D7\u00F7])+";

    /// <summary>
    /// 字节模式正则源（非字母数字、非汉字的任意字符）。对应 JS <c>const $t</c>。
    /// 使用负向先行断言排除字母数字与汉字，然后匹配任意字符（含换行）。
    /// </summary>
    public const string BytePattern =
        @"(?:(?![A-Z0-9 $%*+\-./:]|" + KanjiPattern + @")(?:.|[\r\n]))+";

    /// <summary>汉字模式测试正则（^...$）。对应 JS <c>jt</c>。</summary>
    private static readonly Regex KanjiTestRegex = new("^" + KanjiPattern + "$", RegexOptions.Compiled);

    /// <summary>数字模式测试正则。对应 JS <c>Wt</c>。</summary>
    private static readonly Regex NumericTestRegex = new("^" + NumericPattern + "$", RegexOptions.Compiled);

    /// <summary>字母数字模式测试正则。对应 JS <c>Ht</c>。</summary>
    private static readonly Regex AlphanumericTestRegex = new(@"^[A-Z0-9 $%*+\-./:]+$", RegexOptions.Compiled);

    /// <summary>汉字分段正则（全局匹配）。对应 JS <c>kt.KANJI = new RegExp(Ft, "g")</c>。</summary>
    public static readonly Regex KanjiSegmentsRegex = new(KanjiPattern, RegexOptions.Compiled);

    /// <summary>字节+汉字分段正则（含汉字）。对应 JS <c>kt.BYTE_KANJI = new RegExp("[^A-Z0-9 $%*+\-./:]+", "g")</c>。</summary>
    public static readonly Regex ByteKanjiSegmentsRegex = new(@"[^A-Z0-9 $%*+\-./:]+", RegexOptions.Compiled);

    /// <summary>字节分段正则。对应 JS <c>kt.BYTE = new RegExp($t, "g")</c>。</summary>
    public static readonly Regex ByteSegmentsRegex = new(BytePattern, RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>数字分段正则。对应 JS <c>kt.NUMERIC = new RegExp(Ut, "g")</c>。</summary>
    public static readonly Regex NumericSegmentsRegex = new(NumericPattern, RegexOptions.Compiled);

    /// <summary>字母数字分段正则。对应 JS <c>kt.ALPHANUMERIC = new RegExp("[A-Z $%*+\-./:]+", "g")</c>。</summary>
    public static readonly Regex AlphanumericSegmentsRegex = new(@"[A-Z $%*+\-./:]+", RegexOptions.Compiled);

    /// <summary>测试字符串是否全部为汉字。对应 JS <c>kt.testKanji(t)</c>。</summary>
    public static bool TestKanji(string text) => KanjiTestRegex.IsMatch(text);

    /// <summary>测试字符串是否全部为数字。对应 JS <c>kt.testNumeric(t)</c>。</summary>
    public static bool TestNumeric(string text) => NumericTestRegex.IsMatch(text);

    /// <summary>测试字符串是否全部为字母数字。对应 JS <c>kt.testAlphanumeric(t)</c>。</summary>
    public static bool TestAlphanumeric(string text) => AlphanumericTestRegex.IsMatch(text);
}

/// <summary>
/// QR 码模式定义与工具方法。对应 JS SDK 中 <c>Vt</c> 类与模式常量。
/// </summary>
internal sealed class QrMode
{
    public string Id { get; }
    public int Bit { get; }
    public int[] CcBits { get; }

    private QrMode(string id, int bit, int[] ccBits)
    {
        Id = id;
        Bit = bit;
        CcBits = ccBits;
    }

    /// <summary>数字模式。对应 JS <c>Vt.NUMERIC = {id:"Numeric", bit:1, ccBits:[10,12,14]}</c>。</summary>
    public static readonly QrMode Numeric = new("Numeric", 1, new[] { 10, 12, 14 });

    /// <summary>字母数字模式。对应 JS <c>Vt.ALPHANUMERIC = {id:"Alphanumeric", bit:2, ccBits:[9,11,13]}</c>。</summary>
    public static readonly QrMode Alphanumeric = new("Alphanumeric", 2, new[] { 9, 11, 13 });

    /// <summary>字节模式。对应 JS <c>Vt.BYTE = {id:"Byte", bit:4, ccBits:[8,16,16]}</c>。</summary>
    public static readonly QrMode Byte = new("Byte", 4, new[] { 8, 16, 16 });

    /// <summary>汉字模式。对应 JS <c>Vt.KANJI = {id:"Kanji", bit:8, ccBits:[8,10,12]}</c>。</summary>
    public static readonly QrMode Kanji = new("Kanji", 8, new[] { 8, 10, 12 });

    /// <summary>混合模式（用于容量计算）。对应 JS <c>Vt.MIXED = {id:"", bit:-1, ccBits:[]}</c>。</summary>
    public static readonly QrMode Mixed = new("", -1, System.Array.Empty<int>());

    /// <summary>结构化追加模式。对应 JS <c>Vt.STRUCTURED = {id:"Structured", bit:3, ccBits:[0,0,0]}</c>。</summary>
    public static readonly QrMode Structured = new("Structured", 3, new[] { 0, 0, 0 });
}

/// <summary>
/// QR 模式工具方法。对应 JS SDK 中 <c>Vt</c> 类的静态方法。
/// </summary>
internal static class QrModeUtils
{
    /// <summary>
    /// 获取字符计数指示符位数。对应 JS <c>Vt.getCharCountIndicator(t, e)</c>。
    /// 按版本区间返回不同位数：1-9 → ccBits[0], 10-26 → ccBits[1], 27-40 → ccBits[2]。
    /// </summary>
    public static int GetCharCountIndicator(QrMode mode, int version)
    {
        if (mode.CcBits == null || mode.CcBits.Length == 0)
            throw new ArgumentException("Invalid mode: " + mode.Id);
        if (!QrVersion.IsValid(version))
            throw new ArgumentException("Invalid version: " + version);
        if (version >= 1 && version < 10) return mode.CcBits[0];
        if (version < 27) return mode.CcBits[1];
        return mode.CcBits[2];
    }

    /// <summary>
    /// 根据数据内容选择最佳模式。对应 JS <c>Vt.getBestModeForData(t)</c>。
    /// 优先级：数字 > 字母数字 > 汉字 > 字节。
    /// </summary>
    public static QrMode GetBestModeForData(string text)
    {
        if (QrCharMode.TestNumeric(text)) return QrMode.Numeric;
        if (QrCharMode.TestAlphanumeric(text)) return QrMode.Alphanumeric;
        if (QrCharMode.TestKanji(text)) return QrMode.Kanji;
        return QrMode.Byte;
    }

    /// <summary>模式转字符串。对应 JS <c>Vt.toString(t)</c>。</summary>
    public static string ToString(QrMode? mode)
    {
        if (mode != null && mode.Id != null) return mode.Id;
        throw new ArgumentException("Invalid mode");
    }

    /// <summary>
    /// 模式合法性校验。对应 JS <c>Vt.isValid(t)</c>。
    /// JS 检查 <c>t.bit</c>（truthy）与 <c>t.ccBits</c>（truthy）。
    /// </summary>
    public static bool IsValid(QrMode? mode) => mode != null && mode.Bit != 0 && mode.CcBits != null;

    /// <summary>
    /// 从模式名称解析。对应 JS <c>Vt.fromString(t)</c>。
    /// 支持 "numeric"/"alphanumeric"/"kanji"/"byte"（不区分大小写）。
    /// </summary>
    public static QrMode FromString(string name)
    {
        if (name == null) throw new ArgumentException("Param is not a string");
        return name.ToLowerInvariant() switch
        {
            "numeric" => QrMode.Numeric,
            "alphanumeric" => QrMode.Alphanumeric,
            "kanji" => QrMode.Kanji,
            "byte" => QrMode.Byte,
            _ => throw new ArgumentException("Unknown mode: " + name)
        };
    }

    /// <summary>
    /// 从任意值解析模式。对应 JS <c>Vt.from(t, e)</c>。
    /// 若 t 为合法模式对象则直接返回；否则尝试作为字符串解析；解析失败返回 fallback。
    /// </summary>
    public static QrMode From(object? value, QrMode fallback)
    {
        if (value is QrMode m && IsValid(m)) return m;
        if (value is string s)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { return FromString(s); }
            catch { return fallback; }
        }
        return fallback;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace DzPrinter.Drawing;

/// <summary>
/// 行距模式工具类。对应 JS SDK 中 <c>o</c> 类。
/// 支持的行距模式：1.0 / 1.2 / 1.5 / 2.0 / 自定义（数字）。
/// 模式字符串格式为 <c>"1_0"</c>、<c>"1_2"</c>、<c>"1_5"</c>、<c>"2_0"</c> 或纯数字。
/// </summary>
public static class LineSpaceMode
{
    /// <summary>行距 1.0 模式字符串。JS: <c>o.LineSpace_1_0 = "1_0"</c>。</summary>
    public const string LineSpace_1_0 = "1_0";

    /// <summary>行距 1.2 模式字符串。JS: <c>o.LineSpace_1_2 = "1_2"</c>。</summary>
    public const string LineSpace_1_2 = "1_2";

    /// <summary>行距 1.5 模式字符串。JS: <c>o.LineSpace_1_5 = "1_5"</c>。</summary>
    public const string LineSpace_1_5 = "1_5";

    /// <summary>行距 2.0 模式字符串。JS: <c>o.LineSpace_2_0 = "2_0"</c>。</summary>
    public const string LineSpace_2_0 = "2_0";

    /// <summary>行距 1.0 模式值。JS: <c>o.LineSpaceMode_1_0 = 1</c>。</summary>
    public const double Mode_1_0 = 1.0;

    /// <summary>行距 1.2 模式值。JS: <c>o.LineSpaceMode_1_2 = 1.2</c>。</summary>
    public const double Mode_1_2 = 1.2;

    /// <summary>行距 1.5 模式值。JS: <c>o.LineSpaceMode_1_5 = 1.5</c>。</summary>
    public const double Mode_1_5 = 1.5;

    /// <summary>行距 2.0 模式值。JS: <c>o.LineSpaceMode_2_0 = 2</c>。</summary>
    public const double Mode_2_0 = 2.0;

    /// <summary>自定义行距模式值。JS: <c>o.LineSpaceMode_Custom = 0</c>。</summary>
    public const double Mode_Custom = 0.0;

    // JS: /^(\d+)_(\d+)$/ —— 匹配 "1_0" / "1_2" / "2_0" 等
    private static readonly Regex UnderscoreModeRegex =
        new(@"^(\d+)_(\d+)$", RegexOptions.Compiled);

    // JS: /^\d*\.?\d*$/ —— 匹配纯数字（含小数）
    private static readonly Regex NumberRegex =
        new(@"^\d*\.?\d*$", RegexOptions.Compiled);

    /// <summary>
    /// 解析行距模式字符串为模式值。对应 JS <c>o.getLineMode(t)</c>。
    /// </summary>
    /// <param name="text">行距字符串（如 "1_0"、"1.5"、"2"）。</param>
    /// <returns>模式值（&gt;1 为具体倍数，1 为 1.0 模式，0 为自定义）。</returns>
    public static double GetLineMode(string? text)
    {
        // JS: const e = /^(\d+)_(\d+)$/.exec(t);
        var match = UnderscoreModeRegex.Match(text ?? string.Empty);
        if (match.Success)
        {
            // JS: const t = parseInt(e[1]) + parseFloat(`0.${e[2]}`);
            var intPart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var fracPart = double.Parse("0." + match.Groups[2].Value, CultureInfo.InvariantCulture);
            var v = intPart + fracPart;
            return v > 1 ? v : Mode_1_0;
        }

        // JS: (t && /^\d*\.?\d*$/.test(t) ? parseFloat(t) : 0) > 0 ? LineSpaceMode_Custom : LineSpaceMode_1_0
        var num = (!string.IsNullOrEmpty(text) && NumberRegex.IsMatch(text))
            ? double.Parse(text, CultureInfo.InvariantCulture)
            : 0.0;
        return num > 0 ? Mode_Custom : Mode_1_0;
    }

    /// <summary>
    /// 模式值转字符串。对应 JS <c>o.toString(t, e)</c>。
    /// </summary>
    public static string ToString(double mode, double? fallback = null)
    {
        return mode switch
        {
            Mode_1_0 => LineSpace_1_0,
            Mode_1_2 => LineSpace_1_2,
            Mode_1_5 => LineSpace_1_5,
            Mode_2_0 => LineSpace_2_0,
            _ => fallback.HasValue ? fallback.Value.ToString(CultureInfo.InvariantCulture) : string.Empty
        };
    }

    /// <summary>
    /// 计算模式对应的额外行间距。对应 JS <c>o.getModeValue(t, e)</c>。
    /// </summary>
    /// <param name="mode">模式值。</param>
    /// <param name="fontHeight">字体高度。</param>
    /// <returns>mode &gt; 1 时返回 (mode-1)*fontHeight；否则返回 0。</returns>
    public static double GetModeValue(double mode, double fontHeight)
        => mode > 1 ? (mode - 1) * fontHeight : 0;

    /// <summary>
    /// 解析行距字符串/数字为像素行间距值。对应 JS <c>o.valueOf(t, e)</c>。
    /// </summary>
    /// <param name="value">行距值（字符串或数字）。</param>
    /// <param name="fontHeight">字体高度。</param>
    public static double ValueOf(object? value, double fontHeight)
    {
        if (value is string s)
        {
            var match = UnderscoreModeRegex.Match(s);
            if (match.Success)
            {
                var intPart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var fracPart = double.Parse("0." + match.Groups[2].Value, CultureInfo.InvariantCulture);
                return GetModeValue(intPart + fracPart, fontHeight);
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                return num;
            return 0;
        }

        if (value is double d) return GetModeValue(d, fontHeight);
        if (value is int i) return GetModeValue(i, fontHeight);
        return 0;
    }
}

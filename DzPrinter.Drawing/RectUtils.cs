using System.Globalization;
using System.Text.RegularExpressions;

namespace DzPrinter.Drawing;

/// <summary>
/// 矩形工具类。对应 JS SDK 中 <c>r</c> 类的矩形相关方法。
/// JS 中 <c>r</c> 是一个多用途工具类（含日期/UUID/矩形等），
/// 此处仅保留 Drawing 模块用到的矩形部分：<see cref="CreateRect"/>/<see cref="ParseRect"/>/<see cref="Rotate90"/>。
/// </summary>
public static class RectUtils
{
    /// <summary>
    /// 创建矩形。对应 JS <c>r.createRect(t, e, i, s)</c>。
    /// </summary>
    public static Rect CreateRect(double x, double y, double width, double height)
        => new(x, y, width, height);

    // JS: /^\[?\(?\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*\)?]?$/
    private static readonly Regex ParseRectRegex =
        new(@"^\[?\(?\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*,\s*([0-9.+-]+)\s*\)?]?$",
            RegexOptions.Compiled);

    /// <summary>
    /// 解析矩形字符串。对应 JS <c>r.parseRect(t)</c>。
    /// 支持 <c>"x,y,w,h"</c>、<c>"(x,y,w,h)"</c>、<c>"[x,y,w,h]"</c> 格式。
    /// </summary>
    /// <returns>解析成功返回 <see cref="Rect"/>；否则 null。</returns>
    public static Rect? ParseRect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = text!.Trim();
        var m = ParseRectRegex.Match(t);
        if (!m.Success) return null;
        return new Rect(
            double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 将矩形顺时针旋转 90°（就地修改并返回）。对应 JS <c>r.rotate90(t)</c>。
    /// 以矩形中心为旋转中心，宽高互换。
    /// </summary>
    public static Rect Rotate90(Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return rect;
        var cx = rect.X + 0.5 * rect.Width;
        var cy = rect.Y + 0.5 * rect.Height;
        var oldW = rect.Width;
        rect.Width = rect.Height;
        rect.Height = oldW;
        rect.X = cx - 0.5 * rect.Width;
        rect.Y = cy - 0.5 * rect.Height;
        return rect;
    }
}

/// <summary>
/// 矩形结构。对应 JS 中 <c>{x, y, width, height}</c> 对象。
/// </summary>
public sealed class Rect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public Rect() { }

    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double Right => X + Width;
    public double Bottom => Y + Height;

    public override string ToString() =>
        $"Rect({X}, {Y}, {Width}x{Height})";
}

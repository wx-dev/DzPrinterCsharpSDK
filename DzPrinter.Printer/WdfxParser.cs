using DzPrinter.Drawing;
using System.Xml.Linq;

namespace DzPrinter.Printer;

// =====================================================================
//  WdfxParser（WDFX 标签模板解析器）。对应 JS SDK 中 <c>Pi</c> 类。
//  WDFX 是德佟打印机的标签模板 XML 格式，包含：
//    - 根元素 <label>：定义标签尺寸（width/height，单位 mm）与背景色
//    - 子元素：text/barcode/qrcode/image/rect/line/ellipse/circle/arcText 等
//    - 每个元素携带 x/y/width/height/rotation/color/fontSize/fontName 等属性
//
//  解析产物为 <see cref="LabelTemplate"/>，由 <see cref="LabelContext"/> 渲染到画布。
// =====================================================================

/// <summary>
/// WDFX 标签模板解析器。对应 JS SDK 中的 <c>Pi</c>（WdfxParser）类。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>Pi</c> 使用浏览器 DOMParser 解析 XML，
/// C# 使用 <see cref="XDocument"/> 解析。</para>
/// <para><b>WDFX 结构示例</b>：</para>
/// <code>
/// &lt;label width="40" height="30" background="#fff"&gt;
///   &lt;text x="2" y="2" width="36" height="6" fontSize="4" text="Hello"/&gt;
///   &lt;qrcode x="2" y="10" width="10" height="10" text="https://example.com"/&gt;
/// &lt;/label&gt;
/// </code>
/// </remarks>
public static class WdfxParser
{
    /// <summary>WDFX 根元素名。JS: <c>"label"</c>。</summary>
    public const string RootElementName = "label";

    /// <summary>
    /// 解析 WDFX XML 字符串为 <see cref="LabelTemplate"/>。对应 JS <c>Pi.parse(xml)</c>。
    /// </summary>
    /// <param name="xml">WDFX XML 字符串。</param>
    /// <returns>解析后的标签模板。</returns>
    /// <exception cref="ArgumentException">XML 格式无效。</exception>
    public static LabelTemplate Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("XML 内容不能为空。", nameof(xml));

        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new ArgumentException("XML 缺少根元素。", nameof(xml));

        if (root.Name.LocalName != RootElementName)
            throw new ArgumentException(
                $"根元素名应为 '{RootElementName}'，实际为 '{root.Name.LocalName}'。", nameof(xml));

        var template = new LabelTemplate
        {
            WidthMm = GetDoubleAttr(root, "width", 0),
            HeightMm = GetDoubleAttr(root, "height", 0),
            Background = GetStringAttr(root, "background", PrinterCanvas.ColorBgDefault) ?? PrinterCanvas.ColorBgDefault,
            Orientation = GetIntAttr(root, "orientation", 0),
        };

        // 遍历子元素，按类型解析为绘制项
        foreach (var elem in root.Elements())
        {
            var item = ParseElement(elem);
            if (item != null) template.Items.Add(item);
        }

        return template;
    }

    /// <summary>
    /// 解析单个绘制元素。对应 JS <c>Pi.parseElement(elem)</c>。
    /// </summary>
    private static LabelItem? ParseElement(XElement elem)
    {
        var type = elem.Name.LocalName;
        var item = new LabelItem { Type = type };

        // 通用属性
        item.X = GetDoubleAttr(elem, "x", 0);
        item.Y = GetDoubleAttr(elem, "y", 0);
        item.Width = GetDoubleAttr(elem, "width", 0);
        item.Height = GetDoubleAttr(elem, "height", 0);
        item.Rotation = GetIntAttr(elem, "rotation", 0);
        item.Color = GetStringAttr(elem, "color", null);
        item.BackgroundColor = GetStringAttr(elem, "backgroundColor", null);

        // 文本相关属性
        item.Text = GetStringAttr(elem, "text", null) ?? elem.Value;
        item.FontName = GetStringAttr(elem, "fontName", null) ?? GetStringAttr(elem, "fontFamily", null);
        item.FontSize = GetDoubleAttr(elem, "fontSize", 0);
        item.FontStyle = GetIntAttr(elem, "fontStyle", 0);
        item.CharSpace = GetDoubleAttr(elem, "charSpace", 0);
        item.LineSpace = GetDoubleAttr(elem, "lineSpace", 0);
        item.HorizontalAlignment = GetStringAttr(elem, "horizontalAlign", null);
        item.VerticalAlignment = GetStringAttr(elem, "verticalAlign", null);
        item.AutoReturn = GetStringAttr(elem, "autoReturn", null);

        // 条码相关属性
        item.BarcodeType = GetIntAttr(elem, "barcodeType", 0);
        item.BarcodeTextPos = GetStringAttr(elem, "textPosition", null);
        item.ModuleWidth = GetIntAttr(elem, "moduleWidth", 0);
        item.EccLevel = GetStringAttr(elem, "eccLevel", null);

        // 图形相关属性
        item.LineWidth = GetDoubleAttr(elem, "lineWidth", 0);
        item.DashLen = GetStringAttr(elem, "dashLength", null);
        item.CornerRadius = GetDoubleAttr(elem, "cornerRadius", 0);

        // 图像相关属性
        item.ImageData = GetStringAttr(elem, "data", null) ?? GetStringAttr(elem, "imageData", null);
        item.ImageSrc = GetStringAttr(elem, "src", null) ?? GetStringAttr(elem, "url", null);

        // 透明度
        item.Opacity = GetDoubleAttr(elem, "opacity", 1.0);

        return item;
    }

    // ============ XML 属性读取辅助 ============

    private static double GetDoubleAttr(XElement elem, string name, double defaultValue)
    {
        var attr = elem.Attribute(name);
        if (attr == null) return defaultValue;
        return double.TryParse(attr.Value, out var v) ? v : defaultValue;
    }

    private static int GetIntAttr(XElement elem, string name, int defaultValue)
    {
        var attr = elem.Attribute(name);
        if (attr == null) return defaultValue;
        return int.TryParse(attr.Value, out var v) ? v : defaultValue;
    }

    private static string? GetStringAttr(XElement elem, string name, string? defaultValue)
    {
        var attr = elem.Attribute(name);
        return attr?.Value ?? defaultValue;
    }
}

/// <summary>
/// 标签模板。对应 JS <c>Pi.parse()</c> 的返回值。
/// </summary>
public sealed class LabelTemplate
{
    /// <summary>标签宽度（毫米）。</summary>
    public double WidthMm { get; set; }

    /// <summary>标签高度（毫米）。</summary>
    public double HeightMm { get; set; }

    /// <summary>背景色。</summary>
    public string Background { get; set; } = PrinterCanvas.ColorBgDefault;

    /// <summary>旋转方向：0=横向, 1=纵向。</summary>
    public int Orientation { get; set; }

    /// <summary>绘制项列表。</summary>
    public List<LabelItem> Items { get; } = new();

    /// <inheritdoc />
    public override string ToString() =>
        $"LabelTemplate({WidthMm}x{HeightMm}mm, {Items.Count} items)";
}

/// <summary>
/// 标签绘制项。对应 JS 中 WDFX 每个子元素的解析结果。
/// </summary>
public sealed class LabelItem
{
    /// <summary>绘制类型。对应 <see cref="DrawType"/> 常量。</summary>
    public string Type { get; set; } = string.Empty;

    // 通用几何
    /// <summary>X 坐标（毫米）。</summary>
    public double X { get; set; }
    /// <summary>Y 坐标（毫米）。</summary>
    public double Y { get; set; }
    /// <summary>宽度（毫米）。</summary>
    public double Width { get; set; }
    /// <summary>高度（毫米）。</summary>
    public double Height { get; set; }
    /// <summary>旋转角度。</summary>
    public int Rotation { get; set; }
    /// <summary>前景色。</summary>
    public string? Color { get; set; }
    /// <summary>背景色。</summary>
    public string? BackgroundColor { get; set; }
    /// <summary>透明度（0-1）。</summary>
    public double Opacity { get; set; } = 1.0;

    // 文本属性
    /// <summary>文本内容。</summary>
    public string? Text { get; set; }
    /// <summary>字体名。</summary>
    public string? FontName { get; set; }
    /// <summary>字号。</summary>
    public double FontSize { get; set; }
    /// <summary>字体样式。</summary>
    public int FontStyle { get; set; }
    /// <summary>字符间距。</summary>
    public double CharSpace { get; set; }
    /// <summary>行间距。</summary>
    public double LineSpace { get; set; }
    /// <summary>水平对齐。</summary>
    public string? HorizontalAlignment { get; set; }
    /// <summary>垂直对齐。</summary>
    public string? VerticalAlignment { get; set; }
    /// <summary>自动换行模式。</summary>
    public string? AutoReturn { get; set; }

    // 条码属性
    /// <summary>1D 条码类型。对应 <see cref="BarcodeType"/> 枚举值。</summary>
    public int BarcodeType { get; set; }
    /// <summary>条码文本位置。</summary>
    public string? BarcodeTextPos { get; set; }
    /// <summary>条码模块宽度。</summary>
    public int ModuleWidth { get; set; }
    /// <summary>二维码纠错等级。</summary>
    public string? EccLevel { get; set; }

    // 图形属性
    /// <summary>线宽。</summary>
    public double LineWidth { get; set; }
    /// <summary>虚线段长度。</summary>
    public string? DashLen { get; set; }
    /// <summary>圆角半径。</summary>
    public double CornerRadius { get; set; }

    // 图像属性
    /// <summary>Base64 图像数据。</summary>
    public string? ImageData { get; set; }
    /// <summary>图像 URL/路径。</summary>
    public string? ImageSrc { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"LabelItem[{Type}]({X},{Y},{Width}x{Height})";
}

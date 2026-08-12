using DzPrinter.Barcode;
using DzPrinter.Core;
using DzPrinter.Drawing;

namespace DzPrinter.Printer;

// =====================================================================
//  LabelContext（标签渲染上下文）。对应 JS SDK 中 <c>Te</c> 类。
//  JS 中 <c>Te</c> 是标签渲染引擎：
//    - 接收 WDFX 解析结果（LabelTemplate）
//    - 在 PrinterCanvasMm 上逐项渲染（drawText/drawBarcode/drawImage/...）
//    - 处理坐标系映射、对齐、自动换行等
//    - 输出最终位图供打印
//
//  C# 实现：
//   - 遍历 <see cref="LabelTemplate.Items"/>
//   - 根据 <see cref="LabelItem.Type"/> 分发到对应的 DrawXxx 方法
//   - 1D/2D 条码先通过 BarcodeEncoder 编码，再传给画布渲染
//   - 通过 <see cref="DrawOptions"/> 传递参数给 <see cref="PrinterCanvasMm"/>
// =====================================================================

/// <summary>
/// 标签渲染上下文。对应 JS SDK 中的 <c>Te</c>（LabelContext）类。
/// 将 <see cref="LabelTemplate"/> 渲染到 <see cref="PrinterCanvasMm"/>。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>Te</c> 在构造时接收画布与配置，
/// 通过 <c>drawLabel(t)</c> 方法渲染标签模板。</para>
/// <para><b>渲染流程</b>：</para>
/// <list type="number">
///   <item>创建/复用 <see cref="PrinterCanvasMm"/></item>
///   <item>调用 <see cref="DrawLabel"/> 初始化画布尺寸与背景</item>
///   <item>遍历 <see cref="LabelTemplate.Items"/>，按类型分发渲染</item>
///   <item>提交作业</item>
/// </list>
/// </remarks>
public sealed class LabelContext
{
    private static ILogger Log => DzLogger.Current;

    /// <summary>渲染目标画布。</summary>
    public PrinterCanvasMm Canvas { get; }

    /// <summary>当前标签模板。</summary>
    public LabelTemplate? Template { get; private set; }

    /// <summary>
    /// 构造 LabelContext。对应 JS <c>Te.constructor(options)</c>。
    /// </summary>
    /// <param name="printerWidth">打印机像素宽度。</param>
    /// <param name="dpi">打印机 DPI（默认 203）。</param>
    public LabelContext(double printerWidth = 384, int dpi = 203)
    {
        Canvas = new PrinterCanvasMm();
        Canvas.Dpi = dpi;
        Log.Info($"【LabelContext】constructor() —— printerWidth={printerWidth}, dpi={dpi}");
    }

    /// <summary>
    /// 渲染标签模板。对应 JS <c>Te.drawLabel(template)</c>。
    /// </summary>
    /// <param name="template">标签模板。</param>
    /// <param name="printerWidth">打印机像素宽度（可选，覆盖构造时的值）。</param>
    /// <returns>渲染是否成功。</returns>
    public bool DrawLabel(LabelTemplate template, double printerWidth = 384)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));
        Template = template;

        Log.Info($"【LabelContext】DrawLabel() —— {template.WidthMm}x{template.HeightMm}mm, " +
                 $"{template.Items.Count} items");

        // 初始化画布
        if (!StartLabel(template, printerWidth)) return false;

        // 逐项渲染
        foreach (var item in template.Items)
        {
            try
            {
                DrawItem(item);
            }
            catch (Exception ex)
            {
                Log.Warn($"【LabelContext】DrawItem() 失败 [{item.Type}]: {ex.Message}");
            }
        }

        // 提交
        Canvas.CommitJob();
        return true;
    }

    /// <summary>
    /// 从 WDFX XML 字符串渲染标签。便捷方法。
    /// </summary>
    public bool DrawLabelFromXml(string xml, double printerWidth = 384)
    {
        var template = WdfxParser.Parse(xml);
        return DrawLabel(template, printerWidth);
    }

    // ============ 内部方法 ============

    /// <summary>初始化画布。对应 JS <c>Te.startLabel(t)</c>。</summary>
    private bool StartLabel(LabelTemplate template, double printerWidth)
    {
        var widthMm = template.WidthMm;
        var heightMm = template.HeightMm;
        if (widthMm <= 0 && printerWidth > 0)
        {
            // 未指定宽度时使用打印机宽度（像素 → mm）
            widthMm = LpaUtils.Pix2MM(printerWidth, Canvas.Dpi);
        }
        if (widthMm <= 0 || heightMm <= 0)
        {
            Log.Warn("【LabelContext】StartLabel() —— 标签尺寸无效");
            return false;
        }

        Canvas.StartJob(new DrawOptions
        {
            Width = widthMm,
            Height = heightMm,
            Orientation = template.Orientation,
            PrinterWidth = printerWidth,
            Dpi = (int)Canvas.Dpi,
            BackgroundColor = template.Background,
        });
        return true;
    }

    /// <summary>
    /// 渲染单个绘制项。对应 JS <c>Te.drawItem(item)</c>。
    /// 根据类型分发到对应的 DrawXxx 方法。
    /// </summary>
    private void DrawItem(LabelItem item)
    {
        var opt = BuildDrawOptions(item);
        var drawn = item.Type switch
        {
            DrawType.Text => Canvas.DrawText(opt),
            DrawType.Barcode => Draw1DBarcode(item, opt),
            DrawType.QRCode => Draw2DBarcode(item, opt, Barcode2DType.QRCode),
            DrawType.PDF417 => Draw2DBarcode(item, opt, Barcode2DType.PDF417),
            DrawType.DataMatrix => Draw2DBarcode(item, opt, Barcode2DType.DMCode),
            DrawType.DataMatrixAlt => Draw2DBarcode(item, opt, Barcode2DType.DMCode),
            DrawType.GridMatrix => Draw2DBarcode(item, opt, Barcode2DType.GMCode),
            DrawType.GridMatrixAlt => Draw2DBarcode(item, opt, Barcode2DType.GMCode),
            DrawType.Image => Canvas.DrawImage(opt),
            DrawType.Rect => Canvas.DrawRect(opt),
            DrawType.Rectangle => Canvas.DrawRect(opt),
            DrawType.Ellipse => Canvas.DrawEllipse(opt),
            DrawType.Circle => Canvas.DrawCircle(opt),
            DrawType.Line => Canvas.DrawLine(opt),
            DrawType.ArcText => Canvas.DrawArcText(opt),
            DrawType.ArcTextAlt => Canvas.DrawArcText(opt),
            _ => LogUnknownType(item.Type),
        };

        if (!drawn)
            Log.Warn($"【LabelContext】DrawItem() —— 渲染失败 [{item.Type}]");
    }

    private bool LogUnknownType(string type)
    {
        Log.Warn($"【LabelContext】未知绘制类型: {type}");
        return false;
    }

    /// <summary>
    /// 渲染 1D 条码。对应 JS <c>Te.draw1DBarcode(item)</c>。
    /// 先通过 <see cref="BarcodeEncoder.Create1D"/> 编码，再传给画布。
    /// </summary>
    private bool Draw1DBarcode(LabelItem item, DrawOptions opt)
    {
        if (string.IsNullOrEmpty(item.Text)) return false;

        // 编码条码
        // 注意：LabelItem.BarcodeType 与 DzPrinter.Printer.BarcodeType 同值，
        // 但 Barcode1DRequest.BarcodeType 期望 DzPrinter.Barcode.BarcodeType，需要显式转换。
        // 使用 global:: 前缀避免与 DzPrinter.Printer.DzPrinter 静态类名称冲突。
        var barcodeType = item.BarcodeType > 0
            ? (global::DzPrinter.Barcode.BarcodeType)item.BarcodeType
            : global::DzPrinter.Barcode.BarcodeType.AUTO;

        var request = new Barcode1DRequest
        {
            Text = item.Text,
            BarcodeType = barcodeType,
        };

        var result = BarcodeEncoder.Create1D(request);
        if (result == null || result.Items.Count == 0)
        {
            Log.Warn($"【LabelContext】Draw1DBarcode() —— 编码失败: {item.Text}");
            return false;
        }

        opt.Datas = result.Items;
        return Canvas.Draw1DBarcode(opt);
    }

    /// <summary>
    /// 渲染 2D 条码。对应 JS <c>Te.draw2DBarcode(item)</c>。
    /// 先通过 <see cref="BarcodeEncoder.Create2D"/> 编码为 BitMatrix，再传给画布。
    /// </summary>
    private bool Draw2DBarcode(LabelItem item, DrawOptions opt, string barcodeType)
    {
        if (string.IsNullOrEmpty(item.Text)) return false;

        var request = new Barcode2DRequest
        {
            Text = item.Text,
            BarcodeType = barcodeType,
        };

        // 解析纠错等级
        if (!string.IsNullOrEmpty(item.EccLevel))
            request.EccLevel = ParseEccLevel(item.EccLevel);

        var matrix = BarcodeEncoder.Create2D(request);
        if (matrix == null)
        {
            Log.Warn($"【LabelContext】Draw2DBarcode() —— 编码失败: {item.Text}");
            return false;
        }

        opt.Data = matrix;
        return Canvas.Draw2DBarcode(opt);
    }

    /// <summary>
    /// 从 <see cref="LabelItem"/> 构建 <see cref="DrawOptions"/>。
    /// 对应 JS <c>Te.buildDrawOptions(item)</c>。
    /// </summary>
    private static DrawOptions BuildDrawOptions(LabelItem item)
    {
        var opt = new DrawOptions
        {
            X = item.X,
            Y = item.Y,
            Width = item.Width,
            Height = item.Height,
            Rotation = item.Rotation,
            Color = item.Color,
            BgColor = item.BackgroundColor,
            Text = item.Text,
            FontName = item.FontName,
            FontHeight = item.FontSize > 0 ? item.FontSize : null,
            FontStyle = item.FontStyle > 0 ? (FontStyle)item.FontStyle : null,
            CharSpace = item.CharSpace,
            LineSpace = item.LineSpace > 0 ? item.LineSpace : null,
            LineWidth = item.LineWidth > 0 ? item.LineWidth : null,
            CornerWidth = item.CornerRadius > 0 ? item.CornerRadius : null,
            CornerHeight = item.CornerRadius > 0 ? item.CornerRadius : null,
            Image = item.ImageData ?? item.ImageSrc,
        };

        // 对齐
        if (!string.IsNullOrEmpty(item.HorizontalAlignment))
            opt.HorizontalAlignment = ParseAlignment(item.HorizontalAlignment);
        if (!string.IsNullOrEmpty(item.VerticalAlignment))
            opt.VerticalAlignment = ParseAlignment(item.VerticalAlignment);

        // 自动换行
        if (!string.IsNullOrEmpty(item.AutoReturn))
            opt.AutoReturn = ParseWrapMode(item.AutoReturn);

        return opt;
    }

    /// <summary>解析对齐字符串为枚举。对应 JS 中的字符串→枚举映射。</summary>
    private static Alignment ParseAlignment(string? value) => value?.ToLowerInvariant() switch
    {
        "left" => Alignment.Start,
        "center" => Alignment.Center,
        "right" => Alignment.End,
        "top" => Alignment.Start,
        "bottom" => Alignment.End,
        "stretch" => Alignment.Stretch,
        _ => Alignment.Unset,
    };

    /// <summary>解析换行模式字符串为枚举。</summary>
    private static WrapMode ParseWrapMode(string? value) => value?.ToLowerInvariant() switch
    {
        "char" => WrapMode.Char,
        "word" => WrapMode.Word,
        "none" => WrapMode.None,
        _ => WrapMode.Char,
    };

    /// <summary>解析纠错等级字符串为枚举。</summary>
    private static EccLevel? ParseEccLevel(string? value) => value?.ToUpperInvariant() switch
    {
        "L" => EccLevel.Low,
        "M" => EccLevel.Middle,
        "Q" => EccLevel.Quality,
        "H" => EccLevel.High,
        "LOW" => EccLevel.Low,
        "MIDDLE" => EccLevel.Middle,
        "QUALITY" => EccLevel.Quality,
        "HIGH" => EccLevel.High,
        _ => null,
    };
}

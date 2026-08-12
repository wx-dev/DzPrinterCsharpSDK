using DzPrinter.Barcode;
using DzPrinter.Imaging;

namespace DzPrinter.Drawing;

// =====================================================================
//  绘图选项。对应 JS SDK 中各绘制方法（drawLine/drawRect/drawText/...）
//  接受的 <c>e</c> 对象。JS 中这些对象是属性包（duck-typing），
//  C# 端用一个统一的可变类承载所有可能的字段，避免类爆炸。
//  不同方法只读取自己关心的字段。
//
//  字段命名与 JS 完全对应（驼峰转帕斯卡），便于翻译对照。
//  所有字段均可空或带默认值，对应 JS 中 <c>"number" != typeof e.x && (e.x = 0)</c> 的容错。
// =====================================================================

/// <summary>
/// 绘图选项（统一属性包）。对应 JS 各 <c>drawXxx(t)</c> 方法接受的 <c>t</c> 对象。
/// </summary>
/// <remarks>
/// JS 中绘制方法是 duck-typing 的：每个方法只读取自己需要的字段，缺省字段用默认值。
/// C# 端用一个类承载所有字段以保持与 JS 灵活性一致。
/// 调用方只需设置当前方法需要的字段。
/// </remarks>
public sealed class DrawOptions
{
    // ---------- 通用几何字段（多数绘制方法共用） ----------

    /// <summary>左上角 X 坐标。JS: <c>e.x</c>。</summary>
    public double? X { get; set; }

    /// <summary>左上角 Y 坐标。JS: <c>e.y</c>。</summary>
    public double? Y { get; set; }

    /// <summary>宽度。JS: <c>e.width</c>。</summary>
    public double? Width { get; set; }

    /// <summary>高度。JS: <c>e.height</c>。</summary>
    public double? Height { get; set; }

    /// <summary>旋转角度（度）。JS: <c>e.rotation</c> 或 <c>e.orientation</c>。</summary>
    public double? Rotation { get; set; }

    /// <summary>旋转模式。JS: <c>e.rotateMode</c>。</summary>
    public RotateMode? RotateMode { get; set; }

    /// <summary>内边距（数组 [top, right, bottom, left] 或单值）。JS: <c>e.padding</c>。</summary>
    public double[]? Padding { get; set; }

    /// <summary>前景色。JS: <c>e.color</c>。</summary>
    public string? Color { get; set; }

    /// <summary>背景色。JS: <c>e.bgColor</c>。</summary>
    public string? BgColor { get; set; }

    /// <summary>反色模式。JS: <c>e.antiColor</c>（可为 bool 或 AntiColorMode）。</summary>
    public object? AntiColor { get; set; }

    /// <summary>水平对齐。JS: <c>e.horizontalAlignment</c> 或 <c>e.alignment</c>。</summary>
    public Alignment? HorizontalAlignment { get; set; }

    /// <summary>垂直对齐。JS: <c>e.verticalAlignment</c>。</summary>
    public Alignment? VerticalAlignment { get; set; }

    // ---------- drawLine 专用 ----------

    /// <summary>起点 X。JS: <c>e.x1</c>。</summary>
    public double? X1 { get; set; }

    /// <summary>起点 Y。JS: <c>e.y1</c>。</summary>
    public double? Y1 { get; set; }

    /// <summary>终点 X。JS: <c>e.x2</c>。</summary>
    public double? X2 { get; set; }

    /// <summary>终点 Y。JS: <c>e.y2</c>。</summary>
    public double? Y2 { get; set; }

    /// <summary>线宽。JS: <c>e.lineWidth</c>。</summary>
    public double? LineWidth { get; set; }

    /// <summary>虚线段长度数组。JS: <c>e.dashLens</c>。</summary>
    public double[]? DashLens { get; set; }

    /// <summary>虚线段长度字符串（逗号分隔）。JS: <c>e.dashLen</c>。</summary>
    public string? DashLen { get; set; }

    // ---------- drawRect / drawRoundRect 专用 ----------

    /// <summary>是否填充。JS: <c>e.fill</c>。</summary>
    public bool Fill { get; set; }

    /// <summary>线条连接样式。JS: <c>e.lineJoin</c>（"miter"/"round"/"bevel"）。</summary>
    public string? LineJoin { get; set; }

    /// <summary>圆角宽度。JS: <c>e.cornerWidth</c>。</summary>
    public double? CornerWidth { get; set; }

    /// <summary>圆角高度。JS: <c>e.cornerHeight</c>。</summary>
    public double? CornerHeight { get; set; }

    /// <summary>圆角半径。JS: <c>e.radius</c>。</summary>
    public double? Radius { get; set; }

    /// <summary>边框对齐。JS: <c>e.borderAlign</c>（覆盖画布默认）。</summary>
    public BorderAlign? BorderAlign { get; set; }

    // ---------- drawText / drawArcText 专用 ----------

    /// <summary>文本内容。JS: <c>e.text</c>（可为字符串或字符串数组）。</summary>
    public object? Text { get; set; }

    /// <summary>备用文本内容。JS: <c>e.content</c>。</summary>
    public object? Content { get; set; }

    /// <summary>已拆分的文本行数组（内部使用）。JS: <c>e.texts</c>。</summary>
    public string[]? Texts { get; set; }

    /// <summary>字体高度。JS: <c>e.fontHeight</c>。</summary>
    public double? FontHeight { get; set; }

    /// <summary>字体样式。JS: <c>e.fontStyle</c>。</summary>
    public FontStyle? FontStyle { get; set; }

    /// <summary>字体名称。JS: <c>e.fontName</c>。</summary>
    public string? FontName { get; set; }

    /// <summary>最小字体高度（自动缩放下限）。JS: <c>e.minFontHeight</c>。</summary>
    public double? MinFontHeight { get; set; }

    /// <summary>自动换行模式。JS: <c>e.autoReturn</c>。</summary>
    public WrapMode? AutoReturn { get; set; }

    /// <summary>是否自动缩小字体以适应高度。JS: <c>e.autoShrink</c>。</summary>
    public bool? AutoShrink { get; set; }

    /// <summary>字符间距。JS: <c>e.charSpace</c>。</summary>
    public double? CharSpace { get; set; }

    /// <summary>行间距。JS: <c>e.lineSpace</c>（可为字符串如 "1_5" 或数字）。</summary>
    public object? LineSpace { get; set; }

    /// <summary>文本标志位（BarcodeTextPos 组合）。JS: <c>e.textFlag</c>。</summary>
    public int? TextFlag { get; set; }

    /// <summary>旧版文本标志位别名。JS: <c>e.flag</c>。</summary>
    public int? Flag { get; set; }

    /// <summary>是否在顶部显示文本。JS: <c>e.topText</c>。</summary>
    public bool TopText { get; set; }

    /// <summary>文本对齐（1D 条码专用）。JS: <c>e.textAlign</c>。</summary>
    public Alignment? TextAlign { get; set; }

    /// <summary>文本对齐别名（1D 条码专用）。JS: <c>e.textAlignment</c>。</summary>
    public Alignment? TextAlignment { get; set; }

    /// <summary>度量优化步长。JS: <c>e.measureOptimizeStep</c>。</summary>
    public double? MeasureOptimizeStep { get; set; }

    // ---------- draw1DBarcode 专用 ----------

    /// <summary>1D 条码分段数据。JS: <c>e.datas</c>（数组 of <c>{data, text}</c>）。</summary>
    public List<BarcodeItem>? Datas { get; set; }

    /// <summary>文本高度（1D 条码）。JS: <c>e.textHeight</c>。</summary>
    public double? TextHeight { get; set; }

    /// <summary>自动缩放级别。JS: <c>e.autoScaleLevel</c>。</summary>
    public int? AutoScaleLevel { get; set; }

    // ---------- draw2DBarcode 专用 ----------

    /// <summary>2D 条码位矩阵。JS: <c>e.data</c>。</summary>
    public BitMatrix? Data { get; set; }

    /// <summary>2D 条码静区大小（模块数）。JS: <c>e.zoneSize</c>。</summary>
    public int? ZoneSize { get; set; }

    /// <summary>2D 条码每模块像素数。JS: <c>e.barPixels</c>。</summary>
    public int? BarPixels { get; set; }

    // ---------- drawImage 专用 ----------

    /// <summary>图像对象。JS: <c>e.image</c> 或 <c>e.img</c>（HTMLImageElement 或类似）。
    /// C# 端使用 <see cref="SKImage"/> 包装。</summary>
    public object? Image { get; set; }

    /// <summary>源图像裁剪起始 X。JS: <c>e.sx</c>。</summary>
    public double? Sx { get; set; }

    /// <summary>源图像裁剪起始 Y。JS: <c>e.sy</c>。</summary>
    public double? Sy { get; set; }

    /// <summary>源图像裁剪宽度。JS: <c>e.swidth</c>。</summary>
    public double? Swidth { get; set; }

    /// <summary>源图像裁剪高度。JS: <c>e.sheight</c>。</summary>
    public double? Sheight { get; set; }

    /// <summary>对齐别名（drawImage 中映射到水平/垂直对齐）。JS: <c>e.alignment</c>。</summary>
    public Alignment? Alignment { get; set; }

    // ---------- drawImageResizeLabel 专用 ----------

    /// <summary>原图像宽度。JS: <c>e.imageWidth</c>。</summary>
    public double? ImageWidth { get; set; }

    /// <summary>原图像高度。JS: <c>e.imageHeight</c>。</summary>
    public double? ImageHeight { get; set; }

    /// <summary>九宫格左边距。JS: <c>e.left</c>。</summary>
    public double? Left { get; set; }

    /// <summary>九宫格上边距。JS: <c>e.top</c>。</summary>
    public double? Top { get; set; }

    /// <summary>九宫格右边距。JS: <c>e.right</c>。</summary>
    public double? Right { get; set; }

    /// <summary>九宫格下边距。JS: <c>e.bottom</c>。</summary>
    public double? Bottom { get; set; }

    /// <summary>是否铺满标签。JS: <c>e.fullOfLabel</c>。</summary>
    public bool FullOfLabel { get; set; }

    /// <summary>相对缩放比例。JS: <c>e.relativeScale</c>。</summary>
    public double? RelativeScale { get; set; }

    /// <summary>是否启用平铺模式。JS: <c>e.tileMode</c>。</summary>
    public bool TileMode { get; set; }

    // ---------- putImageData 专用 ----------

    /// <summary>像素数据（putImageData）。JS: <c>e.data</c>（ImageData 对象）。
    /// C# 端使用 <see cref="DzImageData"/>。</summary>
    public DzImageData? PixelData { get; set; }

    // ---------- startJob 专用 ----------

    /// <summary>打印机宽度（startJob 时 width 缺省回退到此值）。JS: <c>e.printerWidth</c>。</summary>
    public double? PrinterWidth { get; set; }

    /// <summary>画布对象（startJob 时传入）。JS: <c>e.canvas</c>。</summary>
    public object? Canvas { get; set; }

    /// <summary>朝向。JS: <c>e.orientation</c>。</summary>
    public int? Orientation { get; set; }

    /// <summary>作业名。JS: <c>e.jobName</c>。</summary>
    public string? JobName { get; set; }

    /// <summary>是否预览模式。JS: <c>e.isPreview</c>。</summary>
    public bool? IsPreview { get; set; }

    /// <summary>背景色（startJob 时设置）。JS: <c>e.backgroundColor</c>。</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>背景图像（预览模式）。JS: <c>e.backgroundImage</c>。</summary>
    public object? BackgroundImage { get; set; }

    /// <summary>尺度单位（PrinterCanvasMm 使用）。JS: <c>e.scaleUnit</c>。</summary>
    public ScaleUnit? ScaleUnit { get; set; }

    /// <summary>DPI（PrinterCanvasMm 使用）。JS: <c>e.dpi</c>。</summary>
    public int? Dpi { get; set; }

    // ---------- 便捷工厂 ----------

    /// <summary>浅克隆本对象。对应 JS <c>Object.assign({}, e)</c>。</summary>
    public DrawOptions Clone()
    {
        var copy = (DrawOptions)MemberwiseClone();
        // 数组/列表类字段需要浅拷贝一份，避免共享引用导致 cvtDrawOptions 修改原对象
        if (Padding != null) copy.Padding = (double[])Padding.Clone();
        if (DashLens != null) copy.DashLens = (double[])DashLens.Clone();
        if (Datas != null) copy.Datas = new List<BarcodeItem>(Datas);
        if (Texts != null) copy.Texts = (string[])Texts.Clone();
        return copy;
    }
}

using DzPrinter.Imaging;
using SkiaSharp;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvasMm。对应 JS SDK 中 <c>h</c> 类。
//  毫米单位包装器：在 PrinterCanvas 基础上自动将 mm → pixel 转换。
//  默认 DPI=203（典型热敏打印机），DPM = DPI/25.4 ≈ 8 dots/mm。
// =====================================================================

/// <summary>
/// 毫米单位画布。对应 JS SDK 中 <c>h</c> 类。
/// 包装 <see cref="PrinterCanvas"/>，所有传入的几何参数（x/y/width/height/fontHeight/...）
/// 视为毫米，自动通过 <see cref="Cvt"/> 转换为像素后委托给内部画布。
/// </summary>
/// <remarks>
/// 通过 <see cref="ScaleUnit"/> = <see cref="ScaleUnit.Pix"/> 可禁用转换（直接像素模式）。
/// 默认 DPI = 203；可通过 <see cref="StartJob"/> 的 <c>dpi</c> 参数修改。
/// </remarks>
public sealed class PrinterCanvasMm
{
    private double _dpi = 203;
    private double _dpm = 203 / 25.4;
    private double _offsetX = 0;
    private double _offsetY = 0;
    private ScaleUnit? _scaleUnit;
    private readonly PrinterCanvas _cvs;

    /// <summary>
    /// 默认边框 DPM。JS: <c>h.BORDER_DPM_DEFAULT = 8</c>。
    /// </summary>
    public const double BorderDpmDefault = 8;

    /// <summary>
    /// 构造。对应 JS <c>h.constructor(t)</c>。
    /// </summary>
    public PrinterCanvasMm(DrawOptions? options = null)
    {
        _cvs = new PrinterCanvas(options);
    }

    /// <summary>内部画布。对应 JS <c>h.Base</c>。</summary>
    public PrinterCanvas Base => _cvs;

    /// <summary>尺度单位。对应 JS <c>h.ScaleUnit</c>。缺省为 <see cref="ScaleUnit.Auto"/>。</summary>
    public ScaleUnit ScaleUnit => _scaleUnit ?? ScaleUnit.Auto;

    /// <summary>DPI。对应 JS <c>h.Dpi</c> getter/setter。</summary>
    public double Dpi
    {
        get => _dpi;
        set { if (value > 0) { _dpi = value; _dpm = value / 25.4; } }
    }

    /// <summary>DPM（dots per millimeter）。对应 JS <c>h.DPM</c> getter/setter。</summary>
    public double DPM
    {
        get => _dpm;
        set { if (value > 0) { _dpm = value; _dpi = 25.4 * value; } }
    }

    /// <summary>X 偏移（mm）。对应 JS <c>h.OffsetX</c>。</summary>
    public double OffsetX
    {
        get => _offsetX;
        set => _offsetX = value;
    }

    /// <summary>Y 偏移（mm）。对应 JS <c>h.OffsetY</c>。</summary>
    public double OffsetY
    {
        get => _offsetY;
        set => _offsetY = value;
    }

    /// <summary>画布宽度（mm）。对应 JS <c>h.Width</c>。</summary>
    public double Width => _cvs.Width / _dpm;

    /// <summary>画布高度（mm）。对应 JS <c>h.Height</c>。</summary>
    public double Height => _cvs.Height / _dpm;

    /// <summary>画布像素宽度。对应 JS <c>h.CanvasWidth</c>。</summary>
    public int CanvasWidth => _cvs.Canvas.Width;

    /// <summary>画布像素高度。对应 JS <c>h.CanvasHeight</c>。</summary>
    public int CanvasHeight => _cvs.Canvas.Height;

    /// <summary>前景色。对应 JS <c>h.Foreground</c>。</summary>
    public string Foreground
    {
        get => _cvs.Foreground;
        set => _cvs.Foreground = value;
    }

    // ============ 字体设置（mm → px） ============

    /// <summary>设置字体名。对应 JS <c>h.setFontName(t)</c>。</summary>
    public void SetFontName(string? name) => _cvs.FontName = name ?? PrinterCanvas.FontNameDefault;

    /// <summary>获取字体高度（mm）。对应 JS <c>h.getFontHeight()</c>。</summary>
    public double GetFontHeight() => InvertCvt(_cvs.FontHeight);

    /// <summary>设置字体高度（mm）。对应 JS <c>h.setFontHeight(t)</c>。</summary>
    public void SetFontHeight(double fontHeight) => _cvs.FontHeight = Cvt(fontHeight);

    /// <summary>设置行间距（mm）。对应 JS <c>h.setLineSpace(t)</c>。</summary>
    public void SetLineSpace(double lineSpace) => _cvs.LineSpace = Cvt(lineSpace);

    /// <summary>
    /// 解析行间距。对应 JS <c>h.parseLineSpace(t, e)</c>。
    /// 数字原样返回；字符串走画布的 getLineSpace 解析。
    /// </summary>
    public double ParseLineSpace(object? lineSpace, double fontHeight)
    {
        if (lineSpace is double d) return d;
        if (lineSpace is int i) return i;
        if (lineSpace is string s) return _cvs.GetLineSpace(s, fontHeight);
        return 0;
    }

    /// <summary>设置字符间距（mm）。对应 JS <c>h.setCharSpace(t)</c>。</summary>
    public void SetCharSpace(double charSpace) => _cvs.CharSpace = Cvt(charSpace);

    /// <summary>获取线宽（mm）。对应 JS <c>h.getLineWidth()</c>。</summary>
    public double GetLineWidth() => InvertCvt(_cvs.LineWidth);

    /// <summary>设置线宽（mm）。对应 JS <c>h.setLineWidth(t)</c>。</summary>
    public void SetLineWidth(double lineWidth) => _cvs.LineWidth = Cvt(lineWidth);

    /// <summary>
    /// 应用旋转。对应 JS <c>h.setRotation(t, e, i)</c>。
    /// center/size 视为 mm，先转换为像素再委托。
    /// </summary>
    public void SetRotation(int rotation, SKPoint? center, SKSize? size)
    {
        SKPoint? c = null;
        SKSize? s = null;
        if (center.HasValue)
            c = new SKPoint((float)Cvt(center.Value.X), (float)Cvt(center.Value.Y));
        if (size.HasValue)
            s = new SKSize((float)Cvt(size.Value.Width), (float)Cvt(size.Value.Height));
        _cvs.SetRotation(rotation, c, s);
    }

    /// <summary>特性查询。对应 JS <c>h.supports(t)</c>。</summary>
    public bool Supports(string feature) => _cvs.Supports(feature);

    // ============ Job 生命周期 ============

    /// <summary>
    /// 开始作业。对应 JS <c>h.startJob(t)</c>。
    /// </summary>
    public PrinterCanvas? StartJob(DrawOptions options)
    {
        _scaleUnit = options.ScaleUnit;
        if (options.Dpi.HasValue) Dpi = options.Dpi.Value;
        var converted = CvtDrawOptions(options);
        return _cvs.StartJob(converted) != null ? _cvs : null;
    }

    /// <summary>提交作业。对应 JS <c>h.commitJob()</c>。</summary>
    public PrinterCanvas? CommitJob() => _cvs.CommitJob() != null ? _cvs : null;

    // ============ 绘制方法（mm → px 委托） ============

    /// <summary>绘制直线。对应 JS <c>h.drawLine(t)</c>。</summary>
    public bool DrawLine(DrawOptions opt) => _cvs.DrawLine(CvtDrawOptions(opt));

    /// <summary>绘制矩形。对应 JS <c>h.drawRect(t)</c>。</summary>
    public bool DrawRect(DrawOptions opt) => _cvs.DrawRect(CvtDrawOptions(opt));

    /// <summary>绘制圆角矩形。对应 JS <c>h.drawRoundRect(t)</c>。</summary>
    public bool DrawRoundRect(DrawOptions opt) => _cvs.DrawRoundRect(CvtDrawOptions(opt));

    /// <summary>绘制椭圆。对应 JS <c>h.drawEllipse(t)</c>。</summary>
    public bool DrawEllipse(DrawOptions opt) => _cvs.DrawEllipse(CvtDrawOptions(opt));

    /// <summary>绘制圆。对应 JS <c>h.drawCircle(t)</c>。</summary>
    public bool DrawCircle(DrawOptions opt) => _cvs.DrawCircle(CvtDrawOptions(opt));

    /// <summary>绘制文本。对应 JS <c>h.drawText(t)</c>。</summary>
    public bool DrawText(DrawOptions opt) => _cvs.DrawText(CvtDrawOptions(opt));

    /// <summary>绘制弧形文本。对应 JS <c>h.drawArcText(t)</c>。</summary>
    public bool DrawArcText(DrawOptions opt) => _cvs.DrawArcText(CvtDrawOptions(opt));

    /// <summary>绘制 1D 条码。对应 JS <c>h.draw1DBarcode(t)</c>。</summary>
    public bool Draw1DBarcode(DrawOptions opt)
    {
        var converted = CvtDrawOptions(opt);
        // JS: (t = this.cvtDrawOptions(t)).textHeight = this.cvt(t.textHeight)
        if (converted.TextHeight.HasValue)
            converted.TextHeight = Cvt(converted.TextHeight.Value);
        return _cvs.Draw1DBarcode(converted);
    }

    /// <summary>绘制 2D 条码。对应 JS <c>h.draw2DBarcode(t)</c>。</summary>
    public bool Draw2DBarcode(DrawOptions opt) => _cvs.Draw2DBarcode(CvtDrawOptions(opt));

    /// <summary>绘制图像。对应 JS <c>h.drawImage(t)</c>。</summary>
    public bool DrawImage(DrawOptions opt) => _cvs.DrawImage(CvtDrawOptions(opt));

    /// <summary>九宫格缩放绘制图像。对应 JS <c>h.drawImageResizeLabel(t)</c>。</summary>
    /// <remarks>JS 内部传 <c>this.DPM/20</c> 作为 extraScale。</remarks>
    public bool DrawImageResizeLabel(DrawOptions opt)
        => _cvs.DrawImageResizeLabel(opt, _dpm / 20);

    /// <summary>写入像素数据。对应 JS <c>h.putImageData(t)</c>。</summary>
    public bool PutImageData(DrawOptions opt) => _cvs.PutImageData(CvtDrawOptions(opt));

    /// <summary>拆分文本。对应 JS <c>h.splitText(t)</c>。</summary>
    public List<string> SplitText(DrawOptions opt) => _cvs.SplitText(CvtDrawOptions(opt));

    /// <summary>度量文本。对应 JS <c>h.measureText(t)</c>。</summary>
    public PrinterCanvas.TextMetrics MeasureText(DrawOptions opt)
    {
        if (opt.FontHeight.HasValue) opt.FontHeight = Cvt(opt.FontHeight.Value);
        return _cvs.MeasureText(opt);
    }

    /// <summary>度量适合字号（mm）。对应 JS <c>h.measureFontSize(t)</c>。</summary>
    public double MeasureFontSize(DrawOptions opt)
    {
        var converted = CvtDrawOptions(opt);
        var e = _cvs.MeasureFontSize(converted);
        return InvertCvt(e);
    }

    /// <summary>反色。对应 JS <c>h.inverseColors()</c>。</summary>
    public bool InverseColors() => _cvs.InverseColors();

    /// <summary>水平翻转。对应 JS <c>h.horizontalFlip()</c>。</summary>
    public bool HorizontalFlip() => _cvs.HorizontalFlip();

    /// <summary>获取像素数据。对应 JS <c>h.getImageData()</c>。</summary>
    public DzImageData GetImageData() => _cvs.GetImageData();

    // ============ 单位转换 ============

    /// <summary>
    /// 毫米 → 像素。对应 JS <c>h.cvt(t)</c>。
    /// </summary>
    public double Cvt(double mm) => mm * _dpm;

    /// <summary>
    /// 像素 → 毫米。对应 JS <c>h.invertCvt(t)</c>。
    /// </summary>
    public double InvertCvt(double px) => px / _dpm;

    /// <summary>
    /// 数组就地 mm → px。对应 JS <c>h.cvtArray(t)</c>。
    /// </summary>
    public double[]? CvtArray(double[]? arr)
    {
        if (arr != null)
            for (var e = 0; e < arr.Length; e++)
                arr[e] = Cvt(arr[e]);
        return arr;
    }

    /// <summary>
    /// 转换绘图选项中所有几何字段（mm → px）。对应 JS <c>h.cvtDrawOptions(e)</c>。
    /// 浅克隆后修改，不污染调用方传入的对象。
    /// </summary>
    public DrawOptions CvtDrawOptions(DrawOptions opt)
    {
        // JS: if (this.ScaleUnit === t.ScaleUnit.Pix) return e;
        if (ScaleUnit == ScaleUnit.Pix) return opt;

        var e = opt.Clone();

        // JS: "number" == typeof e.x && (e.x = this.cvt(e.x + this.OffsetX))
        if (e.X.HasValue) e.X = Cvt(e.X.Value + _offsetX);
        if (e.Y.HasValue) e.Y = Cvt(e.Y.Value + _offsetY);
        if (e.Width.HasValue) e.Width = Cvt(e.Width.Value);
        if (e.Height.HasValue) e.Height = Cvt(e.Height.Value);

        // JS: e.margin 已废弃（DrawOptions 中无此字段）

        if (e.LineWidth.HasValue) e.LineWidth = Cvt(e.LineWidth.Value);
        if (e.Radius.HasValue) e.Radius = Cvt(e.Radius.Value);
        if (e.CornerWidth.HasValue) e.CornerWidth = Cvt(e.CornerWidth.Value);
        if (e.CornerHeight.HasValue) e.CornerHeight = Cvt(e.CornerHeight.Value);

        // drawLine 坐标
        // JS: n.x1 && (n.x1 = this.cvt(n.x1 + this.OffsetX))  —— truthy 检查（0 视为 falsy）
        if (e.X1.HasValue && e.X1.Value != 0) e.X1 = Cvt(e.X1.Value + _offsetX);
        if (e.Y1.HasValue && e.Y1.Value != 0) e.Y1 = Cvt(e.Y1.Value + _offsetY);
        if (e.X2.HasValue && e.X2.Value != 0) e.X2 = Cvt(e.X2.Value + _offsetX);
        if (e.Y2.HasValue && e.Y2.Value != 0) e.Y2 = Cvt(e.Y2.Value + _offsetY);

        if (e.DashLens != null)
            e.DashLens = e.DashLens.Select(x => Cvt(x)).ToArray();
        if (!string.IsNullOrEmpty(e.DashLen))
            e.DashLen = string.Join(",", e.DashLen!.Split(',').Select(x =>
                double.TryParse(x, out var v) ? Cvt(v).ToString(System.Globalization.CultureInfo.InvariantCulture) : x));

        // padding
        if (e.Padding != null) e.Padding = CvtArray((double[])e.Padding.Clone());

        // 字体相关
        if (e.FontHeight.HasValue) e.FontHeight = Cvt(e.FontHeight.Value);
        if (e.MinFontHeight.HasValue) e.MinFontHeight = Cvt(e.MinFontHeight.Value);
        // JS: a.lineSpace && "number" == typeof a.lineSpace && (a.lineSpace = this.cvt(a.lineSpace))
        if (e.LineSpace is double ls) e.LineSpace = Cvt(ls);
        if (e.CharSpace.HasValue) e.CharSpace = Cvt(e.CharSpace.Value);

        return e;
    }
}

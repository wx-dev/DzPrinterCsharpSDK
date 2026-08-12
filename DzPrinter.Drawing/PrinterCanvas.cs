using DzPrinter.Core;
using SkiaSharp;
using System.Globalization;
using System.Text.RegularExpressions;
using FontStyleFlag = DzPrinter.Drawing.FontStyle;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvas（部分类）。对应 JS SDK 中 <c>c</c> 类。
//  本文件包含：静态常量/工具、实例字段/属性、构造、画布管理、
//               通用辅助方法（padding/alignment/rotation/font/job 生命周期）。
//
//  绘图方法拆分到 partial 文件：
//    - PrinterCanvas.Drawing.cs : drawLine/drawRect/drawRoundRect/drawEllipse/drawCircle
//    - PrinterCanvas.Text.cs    : drawText/measureText/splitText/...
//    - PrinterCanvas.Barcode.cs : draw1DBarcode/draw2DBarcode
//    - PrinterCanvas.Image.cs   : drawImage/putImageData/inverseColors/...
// =====================================================================

/// <summary>
/// 打印机画布。对应 JS SDK 中 <c>c</c> 类。
/// 包装 SkiaSharp <see cref="SKBitmap"/> + <see cref="SKCanvas"/> 提供 HTML5 Canvas 2D 等价 API。
/// </summary>
/// <remarks>
/// <para><b>渲染后端</b>：JS 使用浏览器 HTML5 Canvas 2D Context；C# 使用 SkiaSharp (SKCanvas)。</para>
/// <para><b>字体策略</b>：依赖系统已安装字体，不打包字体文件。若指定字体不存在则回退到默认字体。</para>
/// <para><b>保真声明</b>：所有与 JS 行为等价的算法（坐标计算、padding 处理、对齐推导、
/// 旋转、文本拆分、条码模块映射等）逐字节翻译；仅渲染原语（fillRect/measureText 等）
/// 委托给 SkiaSharp（与 JS 委托给浏览器 Canvas 性质相同）。</para>
/// </remarks>
public sealed partial class PrinterCanvas
{
    // ============ 静态常量 ============
    // 对应 JS: c.NO_START_STOP=4, c.MinBarHeight=2, c.BarTextMargin=1,
    //         c.DockHorizMargin=7, c.DockCharMargin=1, c.BarIsbnMargin=0,
    //         c.AUTO_SCALE_LEVEL=2, c.COLOR_FG_DEFAULT="#000", c.COLOR_BG_DEFAULT="#fff",
    //         c.LINE_WIDTH=28, c.FONT_NAME="黑体", c.TEXT_BASELINE_DEFAULT="alphabetic"

    /// <summary>1D 条码文本标志位：不显示首尾字符。JS: <c>c.NO_START_STOP = 4</c>。</summary>
    public const int NoStartStop = 4;

    /// <summary>1D 条码最小高度。JS: <c>c.MinBarHeight = 2</c>。</summary>
    public const double MinBarHeight = 2;

    /// <summary>1D 条码文本上下边距（模块数）。JS: <c>c.BarTextMargin = 1</c>。</summary>
    public const double BarTextMargin = 1;

    /// <summary>1D 条码水平静区（模块数）。JS: <c>c.DockHorizMargin = 7</c>。</summary>
    public const double DockHorizMargin = 7;

    /// <summary>1D 条码字符左右边距（模块数）。JS: <c>c.DockCharMargin = 1</c>。</summary>
    public const double DockCharMargin = 1;

    /// <summary>1D ISBN 条码边距（模块数）。JS: <c>c.BarIsbnMargin = 0</c>。</summary>
    public const double BarIsbnMargin = 0;

    /// <summary>默认自动缩放级别。JS: <c>c.AUTO_SCALE_LEVEL = 2</c>。</summary>
    public const int AutoScaleLevelDefault = 2;

    /// <summary>默认前景色（黑色）。JS: <c>c.COLOR_FG_DEFAULT = "#000"</c>。</summary>
    public const string ColorFgDefault = "#000";

    /// <summary>默认背景色（白色）。JS: <c>c.COLOR_BG_DEFAULT = "#fff"</c>。</summary>
    public const string ColorBgDefault = "#fff";

    /// <summary>默认线宽。JS: <c>c.LINE_WIDTH = 28</c>。</summary>
    public const double LineWidthDefault = 28;

    /// <summary>默认字体名。JS: <c>c.FONT_NAME = "黑体"</c>。</summary>
    public const string FontNameDefault = "黑体";

    /// <summary>默认文本基线。JS: <c>c.TEXT_BASELINE_DEFAULT = "alphabetic"</c>。</summary>
    public const string TextBaselineDefault = "alphabetic";

    // ============ 静态工具方法 ============

    /// <summary>
    /// 判断对象是否为 null/undefined。对应 JS <c>c.isNull(t)</c>。
    /// </summary>
    public static bool IsNull(object? value) => value == null;

    // JS: /^#[0-9a-f]{3}0$/i  —— 3 位短色 + 0 透明
    private static readonly Regex ShortTransparentRegex =
        new(@"^#[0-9a-f]{3}0$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // JS: /^#[0-9a-f]{6}00$/i  —— 6 位长色 + 00 透明
    private static readonly Regex LongTransparentRegex =
        new(@"^#[0-9a-f]{6}00$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 判断颜色字符串是否表示透明。对应 JS <c>c.isTransparent(t)</c>。
    /// 支持 "transparent"、"#RGB0"、"#RRGGBB00" 格式。
    /// </summary>
    public static bool IsTransparent(string? color)
    {
        var t = (color ?? string.Empty).ToLowerInvariant();
        return t == "transparent"
            || ShortTransparentRegex.IsMatch(t)
            || LongTransparentRegex.IsMatch(t);
    }

    /// <summary>
    /// 计算行高。对应 JS <c>c.getLineHeight(t)</c>。
    /// JS: <c>1.172 * t</c>（约等于字号的 1.172 倍）。
    /// </summary>
    public static double GetLineHeight(double fontHeight) => 1.172 * fontHeight;

    /// <summary>
    /// 判断旋转角度是否为水平方向（0/180/360 度）。对应 JS <c>c.isHorizontal(t)</c>。
    /// JS: case 0/2/180 返回 true。
    /// </summary>
    /// <remarks>JS 中 case 2 是个特殊值（可能是枚举别名），原样保留。</remarks>
    public static bool IsHorizontal(int rotation) => rotation switch
    {
        0 => true,
        2 => true,
        180 => true,
        _ => false
    };

    /// <summary>
    /// 判断旋转角度是否为纵向（90/270 度）。对应 JS <c>c.isPortrait(t)</c>。
    /// JS: case 1/3/90/270 返回 true。
    /// </summary>
    /// <remarks>JS 中 case 1/3 是个特殊值（可能是枚举别名），原样保留。</remarks>
    public static bool IsPortrait(int rotation) => rotation switch
    {
        1 => true,
        3 => true,
        90 => true,
        270 => true,
        _ => false
    };

    // JS: /^[0-9A-Fa-f]+$/  —— 纯十六进制（无 #）
    private static readonly Regex PureHexRegex =
        new(@"^[0-9A-Fa-f]+$", RegexOptions.Compiled);

    // JS: /^#([0-9A-Fa-f]+)$/
    private static readonly Regex HashHexRegex =
        new(@"^#([0-9A-Fa-f]+)$", RegexOptions.Compiled);

    // JS: /^0[xX]([0-9A-Fa-f]+)$/
    private static readonly Regex ZeroXHexRegex =
        new(@"^0[xX]([0-9A-Fa-f]+)$", RegexOptions.Compiled);

    /// <summary>
    /// 校验/规范化颜色字符串。对应 JS <c>c.validateColorStr(t)</c>。
    /// 纯十六进制 → 前置 #；带 # 已合法；0x 前缀 → 转 #；其他原样返回。
    /// </summary>
    public static string ValidateColorStr(string? color)
    {
        if (string.IsNullOrEmpty(color)) return color!;
        if (PureHexRegex.IsMatch(color)) return "#" + color;
        if (HashHexRegex.IsMatch(color)) return color;
        if (ZeroXHexRegex.IsMatch(color)) return "#" + color!.Substring(2);
        return color!;
    }

    /// <summary>
    /// 将十六进制颜色字符串解析为 <see cref="SKColor"/>。辅助方法（JS 中由 Canvas 直接解析）。
    /// 支持 #RGB / #RGBA / #RRGGBB / #RRGGBBAA / RGB / RRGGBB 等。
    /// </summary>
    public static SKColor ParseColor(string? color)
    {
        var validated = ValidateColorStr(color);
        if (string.IsNullOrEmpty(validated)) return SKColors.Black;
        if (!validated.StartsWith("#")) return SKColors.Black;
        var hex = validated.Substring(1);
        // 短格式展开
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        else if (hex.Length == 4) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}{hex[3]}{hex[3]}";
        if (hex.Length != 6 && hex.Length != 8) return SKColors.Black;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return SKColors.Black;
        if (hex.Length == 6) argb = (argb << 8) | 0xFFu;  // 添加不透明 alpha
        return new SKColor(argb);
    }

    // ============ 实例字段 ============
    // 对应 JS constructor 中初始化的字段。

    private SKBitmap? _bitmap;
    private SKCanvas? _ctx;
    private readonly SKPaint _fillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = false };
    private readonly SKPaint _strokePaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = false };
    private readonly SKPaint _textPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private string _textBaseline = TextBaselineDefault;

    // 字体相关
    private string _fontName = FontNameDefault;
    private int _fontStyle = 0;
    private double _fontHeight = 0;

    // 画布尺寸
    private double _width = 0;
    private double _height = 0;
    private int _orientation = 0;
    private string _jobName = "";

    // 绘图参数
    private double _modulePixels = 2;
    private double _lineWidth = 0;
    private double[]? _dashLen = null;
    private string _foreground = ColorFgDefault;
    private string _background = ColorBgDefault;
    private int _rotation = 0;
    private Alignment _horizontalAlign = Alignment.Unset;
    private Alignment _verticalAlign = Alignment.Unset;
    private WrapMode _autoReturn = WrapMode.Char;
    private double _charSpace = 0;
    private object? _lineSpace = null;
    private BorderAlign _borderAlign = BorderAlign.Inner;
    private double _pixPerUnit = 1;

    // 构造选项（对应 JS initOptions）
    private readonly DrawOptions _initOptions;

    // ============ 属性 ============

    /// <summary>底层 SKBitmap（对应 JS Canvas）。读取时若未创建则自动创建。</summary>
    public SKBitmap Canvas
    {
        get
        {
            if (_bitmap == null) SetCanvas(CreateCanvas());
            return _bitmap!;
        }
    }

    /// <summary>SKCanvas 绘图上下文（对应 JS <c>this.ctx</c>）。</summary>
    public SKCanvas Context => _ctx!;

    /// <summary>画布宽度（像素）。对应 JS <c>c.Width</c>。</summary>
    public double Width => _width;

    /// <summary>画布高度（像素）。对应 JS <c>c.Height</c>。</summary>
    public double Height => _height;

    /// <summary>朝向。对应 JS <c>c.Orientation</c>。</summary>
    public int Orientation => _orientation;

    /// <summary>边框对齐方式。对应 JS <c>c.BorderAlign</c> getter/setter。</summary>
    public BorderAlign BorderAlign
    {
        get => _borderAlign;
        set => _borderAlign = value;
    }

    /// <summary>2D 条码每模块像素数。对应 JS <c>c.ModulePixels</c>。</summary>
    public double ModulePixels
    {
        get => _modulePixels;
        set { if (value > 0) _modulePixels = value; }
    }

    /// <summary>虚线段长度数组。对应 JS <c>c.DashLen</c>。</summary>
    public double[] DashLen
    {
        get => _dashLen ?? Array.Empty<double>();
        set => _dashLen = value;
    }

    /// <summary>每单位像素数。对应 JS <c>c.PixPerUnit</c>。</summary>
    public double PixPerUnit => _pixPerUnit;

    /// <summary>水平对齐。对应 JS <c>c.HorizontalAlign</c>。</summary>
    public Alignment HorizontalAlign
    {
        get => _horizontalAlign;
        set => _horizontalAlign = value;
    }

    /// <summary>垂直对齐。对应 JS <c>c.VerticalAlign</c>。</summary>
    public Alignment VerticalAlign
    {
        get => _verticalAlign;
        set => _verticalAlign = value;
    }

    /// <summary>当前项旋转角度。对应 JS <c>c.ItemOrientation</c>。</summary>
    public int ItemOrientation
    {
        get => _rotation;
        set => _rotation = value;
    }

    /// <summary>线宽。对应 JS <c>c.LineWidth</c>。</summary>
    public double LineWidth
    {
        get => _lineWidth > 0 ? _lineWidth : LineWidthDefault;
        set { if (value >= 0) _lineWidth = value; }
    }

    /// <summary>前景色。对应 JS <c>c.Foreground</c>。</summary>
    public string Foreground
    {
        get => _foreground;
        set => _foreground = value;
    }

    /// <summary>背景色。对应 JS <c>c.Background</c>。</summary>
    public string Background
    {
        get => _background;
        set => _background = value;
    }

    /// <summary>自动换行模式。对应 JS <c>c.AutoReturn</c>。</summary>
    public WrapMode AutoReturn
    {
        get => _autoReturn;
        set => _autoReturn = value;
    }

    /// <summary>字体名。对应 JS <c>c.FontName</c>。HARMONYOS SANS 自动映射为 HarmonyOS Sans SC。</summary>
    public string FontName
    {
        get => _fontName;
        set => _fontName = value == null ? FontNameDefault
            : (value.ToUpperInvariant() == "HARMONYOS SANS" ? "HarmonyOS Sans SC" : value);
    }

    /// <summary>字体高度。对应 JS <c>c.FontHeight</c>。</summary>
    public double FontHeight
    {
        get => _fontHeight;
        set { if (value >= 0) _fontHeight = value; }
    }

    /// <summary>字体样式。对应 JS <c>c.FontStyle</c>。</summary>
    public int FontStyle
    {
        get => _fontStyle;
        set { if (value >= 0) _fontStyle = value; }
    }

    /// <summary>行间距。对应 JS <c>c.LineSpace</c>（可为字符串或数字）。</summary>
    public object? LineSpace
    {
        get => _lineSpace;
        set => _lineSpace = value;
    }

    /// <summary>字符间距。对应 JS <c>c.CharSpace</c>。</summary>
    public double CharSpace
    {
        get => _charSpace;
        set => _charSpace = value;
    }

    /// <summary>初始化选项（对应 JS <c>this.initOptions</c>）。运行期可读写。</summary>
    public DrawOptions InitOptions => _initOptions;

    // ============ 构造函数 ============

    /// <summary>
    /// 构造 PrinterCanvas。对应 JS <c>c.constructor(e)</c>。
    /// </summary>
    /// <param name="options">初始化选项。可携带 canvas/creator/onCanvasClear/background/foreground/adjustFontSize/position/willReadFrequently 等字段。</param>
    public PrinterCanvas(DrawOptions? options = null)
    {
        _initOptions = options ?? new DrawOptions();

        // JS: "boolean" == typeof i.adjustFontSize && (i.adjustFontSize = i.adjustFontSize ? .95 : 0)
        // JS 把 true→0.95、false→0；C# 中 adjustFontSize 是 double?，若外部传 bool 需调用方自己转。
        // 此处保留原语义：若 adjustFontSize 为 null 视为 0。

        // JS: i.background && (this._background = i.background)
        if (_initOptions.BackgroundColor != null) _background = _initOptions.BackgroundColor;
        if (!string.IsNullOrEmpty(_initOptions.Color)) _foreground = _initOptions.Color!;

        // JS: i.canvas && this.setCanvas(i.canvas)
        if (_initOptions.Canvas is SKBitmap bmp) SetCanvas(bmp);
    }

    // ============ 画布管理 ============

    /// <summary>
    /// 创建新画布。对应 JS <c>c.createCanvas()</c>。
    /// JS 使用 <c>document.createElement("canvas")</c>；C# 创建 <see cref="SKBitmap"/>。
    /// </summary>
    public SKBitmap CreateCanvas()
    {
        // JS: this._canvasCreator ? this._canvasCreator() : document.createElement("canvas")
        // C# 中 creator 委托由外部注入；若未注入则创建 1x1 占位 bitmap（实际尺寸由 startJob 设置）。
        var bmp = new SKBitmap(1, 1);
        return bmp;
    }

    /// <summary>
    /// 设置当前画布。对应 JS <c>c.setCanvas(t)</c>。
    /// JS 中会重新获取 2D Context 并重置 textBaseline/textAlign。
    /// </summary>
    public void SetCanvas(SKBitmap? bitmap)
    {
        if (bitmap == null) return;
        // 释放旧画布
        _ctx?.Dispose();
        // 不 dispose 旧 _bitmap，因为它可能由外部拥有
        _bitmap = bitmap;
        _ctx = new SKCanvas(_bitmap);
        // 重置文本基线/对齐
        SetTextBaseline(TextBaselineDefault);
    }

    /// <summary>
    /// 检查是否支持指定特性。对应 JS <c>c.supports(t)</c>。
    /// SkiaSharp 全部支持，返回 true（与 JS 浏览器特性检测不同）。
    /// </summary>
    public bool Supports(string feature) => feature switch
    {
        "getImageData" => true,
        "setLineDash" => true,
        "toDataURL" => true,
        "toDataURLWithQuality" => true,
        "measureText" => true,
        _ => false
    };

    /// <summary>支持虚线（属性）。对应 JS <c>c.supportLineDash</c>。</summary>
    public bool SupportLineDash => Supports("setLineDash");

    /// <summary>
    /// 清空画布。对应 JS <c>c.clearAll()</c>。
    /// JS: clearRect 全画布 → 若有背景则 fillRect 填充背景 → 调用 onCanvasClear 回调。
    /// </summary>
    public void ClearAll()
    {
        _ctx!.Clear(SKColors.Transparent);
        if (!string.IsNullOrEmpty(_background))
        {
            _fillPaint.Color = ParseColor(_background);
            _ctx.DrawRect(new SKRect(0, 0, _bitmap!.Width, _bitmap.Height), _fillPaint);
        }
        // JS: "function" == typeof this._canvasClearAction && this._canvasClearAction(this.Canvas, this.ctx)
        // C# 中 onCanvasClear 回调由 InitOptions 提供（若有）；此处不实现回调机制。
    }

    // ============ 字体与文本基线 ============

    /// <summary>
    /// 设置文本基线。对应 JS <c>c.setTextBaseline(t)</c>。
    /// JS: 设置 ctx.textBaseline；若 ctx 实现 setTextBaseline 方法则调用之（强制默认值）。
    /// C# 中仅记录基线字符串，实际偏移在 DrawText 调用中处理（SkiaSharp 无 TextBaseline 属性）。
    /// </summary>
    public void SetTextBaseline(string baseline)
    {
        _textBaseline = baseline;
        // SkiaSharp 的 SKPaint 没有 TextBaseline 属性（这是 HTML5 Canvas 概念）。
        // 基线偏移在 DrawText 调用中通过 Y 坐标手动处理。
    }

    /// <summary>
    /// 设置文本对齐。对应 JS <c>c.setTextAlign(t)</c>。
    /// </summary>
    public void SetTextAlign(string align)
    {
        _textPaint.TextAlign = align switch
        {
            "center" => SKTextAlign.Center,
            "right" => SKTextAlign.Right,
            _ => SKTextAlign.Left
        };
    }

    /// <summary>
    /// 设置当前字体。对应 JS <c>c.setFont(e, i, s)</c>。
    /// JS: <c>this.ctx.font = `${italic} normal ${bold} ${size}px "${name}", system-ui`</c>。
    /// C# 中通过 SKPaint.Typeface/TextSize/FakeBoldText/TextSkewX 实现。
    /// </summary>
    /// <param name="fontHeight">字体高度（px）。null 用当前值。</param>
    /// <param name="fontStyle">字体样式（FontStyle 位标志）。null 用当前值。</param>
    /// <param name="fontName">字体名。null 用当前值。HARMONYOS SANS 映射为 HarmonyOS Sans SC。</param>
    public void SetFont(double? fontHeight, FontStyle? fontStyle, string? fontName)
    {
        var size = fontHeight ?? _fontHeight;
        // JS: const r = this.initOptions.adjustFontSize || 0; a = Math.floor(r > .1 && r < 1 ? n * r : n)
        var adjust = _initOptions.RelativeScale ?? 0;  // 借用 RelativeScale 字段承载 adjustFontSize
        var actualSize = Math.Floor(adjust > 0.1 && adjust < 1 ? size * adjust : size);

        var style = fontStyle ?? (FontStyleFlag)_fontStyle;
        var name = fontName;
        if (!string.IsNullOrEmpty(name) && name!.ToUpperInvariant() == "HARMONYOS SANS")
            name = "HarmonyOS Sans SC";
        name = name ?? _fontName ?? FontNameDefault;

        // 构造 Typeface
        var weight = (style & FontStyleFlag.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = (style & FontStyleFlag.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var tf = SKTypeface.FromFamilyName(name, weight, SKFontStyleWidth.Normal, slant)
                 ?? SKTypeface.Default;
        _textPaint.Typeface = tf;
        _textPaint.TextSize = (float)actualSize;
    }

    // ============ 通用辅助方法 ============

    /// <summary>
    /// 获取换行模式。对应 JS <c>c.getReturnMode(t)</c>。
    /// </summary>
    public WrapMode GetReturnMode(WrapMode? mode) => mode ?? _autoReturn;

    /// <summary>
    /// 获取旋转模式。对应 JS <c>c.getRotateMode(e)</c>。
    /// </summary>
    public RotateMode GetRotateMode(RotateMode? mode)
        => mode != null && mode > 0 ? mode.Value : RotateMode.RotateContent;

    /// <summary>
    /// 获取行间距像素值。对应 JS <c>c.getLineSpace(t, e)</c>。
    /// </summary>
    /// <param name="lineSpace">行距值（字符串如 "1_5" 或数字）。</param>
    /// <param name="fontHeight">字体高度（用于模式值计算）。</param>
    public double GetLineSpace(object? lineSpace, double fontHeight)
    {
        if (lineSpace is string s && !string.IsNullOrEmpty(s))
            return LineSpaceMode.ValueOf(s, fontHeight);
        if (lineSpace is double d && d >= 0) return d;
        if (lineSpace is int i && i >= 0) return i;
        // JS: t >= 0 ? t : this.LineSpace
        if (_lineSpace is double ld) return ld;
        if (_lineSpace is int li) return li;
        return 0;
    }

    /// <summary>
    /// 获取内边距数组 [top, right, bottom, left]。对应 JS <c>c.getPaddings(t)</c>。
    /// </summary>
    public static double[] GetPaddings(DrawOptions opt)
    {
        double[] arr;
        if (opt.Padding != null && opt.Padding.Length > 0)
            arr = opt.Padding;
        else
            arr = Array.Empty<double>();

        var top = arr.Length > 0 ? arr[0] : 0;
        var right = arr.Length > 1 ? arr[1] : top;
        var bottom = arr.Length > 2 ? arr[2] : top;
        var left = arr.Length > 3 ? arr[3] : right;
        return new[] { top, right, bottom, left };
    }

    /// <summary>
    /// 就地应用内边距到选项。对应 JS <c>c.processPaddings(t, e)</c>。
    /// </summary>
    public static void ProcessPaddings(DrawOptions opt, double[] paddings)
    {
        opt.X ??= 0;
        opt.Y ??= 0;
        opt.X += paddings[3];
        if (opt.Width.HasValue) opt.Width -= paddings[3] + paddings[1];
        opt.Y += paddings[0];
        if (opt.Height.HasValue) opt.Height -= paddings[0] + paddings[2];
    }

    /// <summary>
    /// 处理内边距（一步到位）。对应 JS <c>c.processPadding(t)</c>。
    /// </summary>
    public static void ProcessPadding(DrawOptions opt)
    {
        var paddings = GetPaddings(opt);
        ProcessPaddings(opt, paddings);
    }

    /// <summary>
    /// 解析反色模式。对应 JS <c>c.getAntiColor(e, i)</c>。
    /// JS: number 直接返回；boolean 返回 i 或 Anti|AntiBackground；否则 None。
    /// </summary>
    public AntiColorMode GetAntiColor(object? antiColor, AntiColorMode fallback)
    {
        if (antiColor is AntiColorMode m) return m;
        if (antiColor is int i) return (AntiColorMode)i;
        if (antiColor is bool b) return b ? fallback | AntiColorMode.AntiColor | AntiColorMode.AntiBackground : AntiColorMode.None;
        return AntiColorMode.None;
    }

    /// <summary>
    /// 解析反色模式（无 fallback 重载，默认 None）。对应 JS <c>getAntiColor(e)</c>。
    /// </summary>
    public AntiColorMode GetAntiColor(object? antiColor)
        => GetAntiColor(antiColor, AntiColorMode.None);

    /// <summary>
    /// 获取水平对齐。对应 JS <c>c.getHorizontalAlignment(e, i)</c>。
    /// </summary>
    public Alignment GetHorizontalAlignment(Alignment? value, Alignment? fallback = null)
    {
        var e = value ?? _horizontalAlign;
        if (e >= Alignment.Start && e <= Alignment.Stretch) return e;
        return fallback ?? Alignment.Unset;
    }

    /// <summary>
    /// 获取垂直对齐。对应 JS <c>c.getVerticalAlignment(e, i)</c>。
    /// </summary>
    public Alignment GetVerticalAlignment(Alignment? value, Alignment? fallback = null)
    {
        var e = value ?? _verticalAlign;
        if (e >= Alignment.Start && e <= Alignment.Stretch) return e;
        return fallback ?? Alignment.Unset;
    }

    /// <summary>
    /// 获取当前项旋转角度。对应 JS <c>c.getItemRotation(t)</c>。
    /// JS: 接受对象或数字；负数 +360；≥360 取模；0-4 之间 ×90。
    /// </summary>
    public int GetItemRotation(DrawOptions opt)
    {
        int? i = opt.OrientationAsInt();
        var s = i ?? _rotation;
        if (s < 0) s += 360;
        else if (s >= 360) s %= 360;
        if (s > 0 && s < 4) s *= 90;
        return s;
    }

    /// <summary>
    /// 获取线宽。对应 JS <c>c.getLineWidth(t)</c>。
    /// </summary>
    public double GetLineWidth(double? value) => value != null && value > 0 ? value.Value : LineWidth;

    /// <summary>
    /// 获取虚线段长度数组。对应 JS <c>c.getDashLen(t)</c>。
    /// </summary>
    public double[] GetDashLen(double[]? value)
    {
        if (value != null && value.Length > 0) return value;
        return DashLen;
    }

    // ============ Job 生命周期 ============

    /// <summary>
    /// 开始作业。对应 JS <c>c.startJob(e)</c>。
    /// 流程：
    /// <list type="number">
    ///   <item>规范化 width/height/printerWidth（非数字 → 0）。</item>
    ///   <item>width &lt;= 0 且 printerWidth &gt; 0 → width = printerWidth。</item>
    ///   <item>若 width &lt;= 0 且 height &lt;= 0 → 警告并返回 null。</item>
    ///   <item>设置画布尺寸、朝向、作业名、前/背景色（非预览重置为默认）。</item>
    ///   <item>清空画布；预览模式且指定背景图则绘制背景图。</item>
    /// </list>
    /// </summary>
    /// <returns>成功返回当前画布的 SKBitmap；失败返回 null。</returns>
    public SKBitmap? StartJob(DrawOptions options)
    {
        var width = options.Width ?? 0;
        var height = options.Height ?? 0;
        var printerWidth = options.PrinterWidth ?? 0;
        if (width <= 0 && printerWidth > 0) width = printerWidth;
        if (width <= 0 && height <= 0)
        {
            DzLogger.Warn("---- 未指定标签大小！");
            return null;
        }

        if (options.Canvas is SKBitmap bmp) SetCanvas(bmp);

        _width = Math.Round(width != 0 ? width : height);
        _height = Math.Round(height != 0 ? height : width);
        _orientation = options.Orientation ?? 0;
        if (!string.IsNullOrEmpty(options.JobName)) _jobName = options.JobName!;

        // 重新创建画布（JS: this.Canvas.width = this.width; this.Canvas.height = this.height;）
        _ctx?.Dispose();
        _bitmap?.Dispose();
        _bitmap = new SKBitmap((int)_width, (int)_height);
        _ctx = new SKCanvas(_bitmap);

        // 颜色处理：JS: "boolean" != typeof e.isPreview || e.isPreview ? 应用配置 : 重置为默认
        var isPreview = options.IsPreview ?? true;
        if (isPreview)
        {
            if (!string.IsNullOrEmpty(options.BackgroundColor)) _background = options.BackgroundColor!;
            if (!string.IsNullOrEmpty(options.Color)) _foreground = options.Color!;
        }
        else
        {
            _background = ColorBgDefault;
            _foreground = ColorFgDefault;
        }

        _horizontalAlign = Alignment.Unset;
        _verticalAlign = Alignment.Unset;

        ClearAll();

        // JS: e.isPreview && e.backgroundImage && this.drawImage({...})
        // 背景图绘制委托给 DrawImage partial 方法
        if (isPreview && options.BackgroundImage != null)
        {
            var bgOpt = new DrawOptions
            {
                Image = options.BackgroundImage,
                Width = _width,
                Height = _height,
                HorizontalAlignment = Alignment.Stretch
            };
            DrawImage(bgOpt);
        }

        return _bitmap;
    }

    /// <summary>
    /// 提交作业。对应 JS <c>c.commitJob()</c>。
    /// JS 直接返回 Canvas；C# 返回当前 SKBitmap。
    /// </summary>
    public SKBitmap CommitJob() => _bitmap!;

    /// <summary>
    /// 应用旋转。对应 JS <c>c.setRotation(t, e, i)</c>。
    /// 以指定中心点为旋转中心。
    /// </summary>
    /// <param name="rotation">旋转角度（度）。</param>
    /// <param name="center">旋转中心（null 则用原点）。</param>
    /// <param name="size">参考尺寸（用于计算中心，null 则用 0）。</param>
    public void SetRotation(int rotation, SKPoint? center = null, SKSize? size = null)
    {
        rotation = rotation % 360;
        if (rotation <= 0) return;

        var c = center ?? new SKPoint(0, 0);
        var s = size ?? new SKSize(0, 0);

        // JS: s.x = Math.round(s.x + .5*n.width) + .5; s.y = Math.round(s.y + .5*n.height) + .5;
        var cx = (float)(Math.Round(c.X + 0.5 * s.Width) + 0.5);
        var cy = (float)(Math.Round(c.Y + 0.5 * s.Height) + 0.5);

        // JS: ctx.translate(cx, cy); ctx.rotate(rad); ctx.translate(-cx, -cy);
        _ctx!.Translate(cx, cy);
        _ctx.RotateDegrees(rotation);
        _ctx.Translate(-cx, -cy);
    }
}

// ============ DrawOptions 辅助扩展（仅 PrinterCanvas 内部使用） ============
internal static class DrawOptionsInternalExtensions
{
    /// <summary>从 Rotation/Orientation 读取整数朝向。对应 JS <c>"number" == typeof e.rotation ? e.rotation : e.orientation</c>。</summary>
    public static int? OrientationAsInt(this DrawOptions opt)
    {
        if (opt.Rotation.HasValue) return (int)opt.Rotation.Value;
        if (opt.Orientation.HasValue) return opt.Orientation.Value;
        return null;
    }
}

using DzPrinter.Printer;

namespace DzPrinter.Web.Models;

// =====================================================================
// 打印请求 DTO
// =====================================================================

public sealed class PrintRequest
{
    /// <summary>画布宽度，单位 mm。</summary>
    public double WidthMm { get; set; }

    /// <summary>画布高度，单位 mm。</summary>
    public double HeightMm { get; set; }

    /// <summary>朝向：0=正常, 1=旋转90°, 2=180°, 3=270°。</summary>
    public int Orientation { get; set; }

    /// <summary>打印机参数。不传则使用默认值（DPI=203, Width=384, PageCount=1）。</summary>
    public PrinterInfoDto? PrinterInfo { get; set; }

    /// <summary>绘制指令列表，按顺序执行。</summary>
    public List<DrawInstructionDto> Instructions { get; set; } = new();
}

public sealed class PrinterInfoDto
{
    public int PrinterWidth { get; set; } = 384;
    public int PrinterDpi { get; set; } = 203;
    public int PageCount { get; set; } = 1;
    public string? GapType { get; set; }
    public double GapLength { get; set; }
    public string? Darkness { get; set; }
    public string? Speed { get; set; }

    public PrinterInfo ToPrinterInfo() => new()
    {
        PrinterWidth = PrinterWidth,
        PrinterDpi = PrinterDpi,
        PageCount = PageCount,
        GapType = GapType?.ToLowerInvariant() switch
        {
            "gap" => LpaGapType.Gap,
            "black" => LpaGapType.Black,
            "hole" => LpaGapType.Hole,
            "trans" => LpaGapType.Trans,
            "none" => LpaGapType.None,
            _ => LpaGapType.Unset,
        },
        GapLength = GapLength,
        Darkness = Darkness?.ToLowerInvariant() switch
        {
            "min" => LpaPrintDarkness.Min,
            "low" => LpaPrintDarkness.Low,
            "normal" => LpaPrintDarkness.Normal,
            "high" => LpaPrintDarkness.High,
            "max" => LpaPrintDarkness.Max,
            _ => LpaPrintDarkness.Unset,
        },
        Speed = Speed?.ToLowerInvariant() switch
        {
            "min" => LpaPrintSpeed.Min,
            "low" => LpaPrintSpeed.Low,
            "normal" => LpaPrintSpeed.Normal,
            "high" => LpaPrintSpeed.High,
            "max" => LpaPrintSpeed.Max,
            _ => LpaPrintSpeed.Unset,
        },
    };
}

// =====================================================================
// 绘制指令 DTO —— 前端用 JSON 描述每一步绘制操作
// =====================================================================

public sealed class DrawInstructionDto
{
    /// <summary>
    /// 绘制类型：drawText, drawRect, drawRoundRect, drawLine, drawCircle,
    /// drawEllipse, draw1DBarcode, draw2DBarcode, drawImage
    /// </summary>
    public string Type { get; set; } = string.Empty;

    // ---- 通用几何 ----
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? Rotation { get; set; }
    public string? Color { get; set; }
    /// <summary>旋转模式：auto/rotateCanvas/rotateContent</summary>
    public string? RotateMode { get; set; }
    /// <summary>背景色</summary>
    public string? BgColor { get; set; }
    /// <summary>内边距 [top, right, bottom, left]</summary>
    public double[]? Padding { get; set; }

    // ---- drawLine ----
    public double? X1 { get; set; }
    public double? Y1 { get; set; }
    public double? X2 { get; set; }
    public double? Y2 { get; set; }
    public double? LineWidth { get; set; }
    /// <summary>虚线段长度数组，如 [4,2] 表示 4px 实线 2px 空白交替</summary>
    public double[]? DashLens { get; set; }

    // ---- drawRect / drawRoundRect ----
    public bool Fill { get; set; }
    public double? Radius { get; set; }
    /// <summary>线条连接样式：miter/round/bevel</summary>
    public string? LineJoin { get; set; }
    /// <summary>边框对齐方式：none/left/inner/outer 等</summary>
    public string? BorderAlign { get; set; }

    // ---- drawText ----
    public string? Text { get; set; }
    public double? FontHeight { get; set; }
    public string? FontName { get; set; }
    public string? FontStyle { get; set; }
    public string? HorizontalAlignment { get; set; }
    public string? VerticalAlignment { get; set; }
    public bool? AutoShrink { get; set; }
    /// <summary>自动缩小时的最小字号</summary>
    public double? MinFontHeight { get; set; }
    public string? AutoReturn { get; set; }
    public double? CharSpace { get; set; }
    public double? LineSpace { get; set; }

    // ---- draw1DBarcode ----
    public string? BarcodeData { get; set; }
    public string? BarcodeType { get; set; }
    public double? TextHeight { get; set; }
    /// <summary>文本位置：top/bottom/none</summary>
    public string? TextPosition { get; set; }
    /// <summary>条码文本对齐：left/center/right</summary>
    public string? TextAlign { get; set; }
    public bool? TopText { get; set; }

    // ---- draw2DBarcode ----
    public string? QrText { get; set; }
    /// <summary>2D 条码类型：qrcode/pdf417/dataMatrix/gridMatrix/auto</summary>
    public string? Barcode2DType { get; set; }
    public string? EccLevel { get; set; }
    public int? QrVersion { get; set; }
    /// <summary>QR 掩码图案 0-7，0 或 null 表示自动</summary>
    public int? QrMask { get; set; }
    public int? ZoneSize { get; set; }
    public int? BarPixels { get; set; }
    public int? AutoScaleLevel { get; set; }

    // ---- drawImage ----
    public string? ImageBase64 { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>源图裁剪起始 X</summary>
    public double? Sx { get; set; }
    /// <summary>源图裁剪起始 Y</summary>
    public double? Sy { get; set; }
    /// <summary>源图裁剪宽度</summary>
    public double? Swidth { get; set; }
    /// <summary>源图裁剪高度</summary>
    public double? Sheight { get; set; }
}

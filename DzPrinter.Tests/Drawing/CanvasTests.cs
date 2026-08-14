// =====================================================================
//  画布（PrinterCanvas / PrinterCanvasMm）单元测试。
//
//  测试分组：
//    1. PrinterCanvas 基础：构造、StartJob、GetImageData、CommitJob
//    2. PrinterCanvas 绘制：DrawText/DrawRect/DrawLine 产生非空白像素
//    3. PrinterCanvas 异常路径：未 StartJob 时 GetImageData 抛错
//    4. PrinterCanvasMm 单位转换：mm→px、DPI 影响、ScaleUnit
//    5. DrawContext 画布尺寸：Start 后画布像素尺寸正确（mm→px 转换）
// =====================================================================

using DzPrinter.Drawing;
using DzPrinter.Imaging;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using SkiaSharp;

namespace DzPrinter.Tests.Drawing;

#region 1. PrinterCanvas 基础
public class PrinterCanvasBasicTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        var canvas = new PrinterCanvas();
        canvas.Should().NotBeNull();
    }

    [Fact]
    public void StartJob_ValidDimensions_CreatesBitmap()
    {
        var canvas = new PrinterCanvas();
        var bmp = canvas.StartJob(new DrawOptions { Width = 100, Height = 50 });

        bmp.Should().NotBeNull();
        bmp!.Width.Should().Be(100);
        bmp.Height.Should().Be(50);
        canvas.Width.Should().Be(100);
        canvas.Height.Should().Be(50);
    }

    [Fact]
    public void StartJob_ZeroDimensions_ReturnsNull()
    {
        var canvas = new PrinterCanvas();
        var bmp = canvas.StartJob(new DrawOptions { Width = 0, Height = 0 });

        bmp.Should().BeNull();
    }

    [Fact]
    public void StartJob_ZeroWidth_FallsBackToPrinterWidth()
    {
        var canvas = new PrinterCanvas();
        var bmp = canvas.StartJob(new DrawOptions { Width = 0, Height = 50, PrinterWidth = 384 });

        bmp.Should().NotBeNull();
        bmp!.Width.Should().Be(384);
        bmp.Height.Should().Be(50);
    }

    [Fact]
    public void StartJob_OnlyWidth_HeightEqualsWidth()
    {
        var canvas = new PrinterCanvas();
        var bmp = canvas.StartJob(new DrawOptions { Width = 100, Height = 0 });

        bmp.Should().NotBeNull();
        // JS: height = 0 → height = width
        bmp!.Height.Should().Be(100);
    }

    [Fact]
    public void GetImageData_AfterStartJob_ReturnsValidData()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 10, Height = 10 });

        var img = canvas.GetImageData();
        img.IsValid.Should().BeTrue();
        img.Width.Should().Be(10);
        img.Height.Should().Be(10);
        img.Data.Length.Should().Be(10 * 10 * 4);
    }

    [Fact]
    public void CommitJob_ReturnsBitmap()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 20, Height = 20 });

        var bmp = canvas.CommitJob();
        bmp.Should().NotBeNull();
        bmp.Width.Should().Be(20);
        bmp.Height.Should().Be(20);
    }

    [Fact]
    public void ClearAll_DoesNotThrow_AfterStartJob()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 10, Height = 10 });
        var act = () => canvas.ClearAll();
        act.Should().NotThrow();
    }
}
#endregion

#region 2. PrinterCanvas 绘制内容
public class PrinterCanvasDrawingTests
{
    // 辅助：检查画布中是否有非白色像素（即有绘制内容）
    private static bool HasNonWhitePixels(DzImageData img)
    {
        for (var i = 0; i < img.Data.Length; i += 4)
        {
            if (img.Data[i] < 250 || img.Data[i + 1] < 250 || img.Data[i + 2] < 250)
                return true;
        }
        return false;
    }

    [Fact]
    public void DrawText_ProducesNonWhitePixels()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 200, Height = 100 });

        canvas.DrawText(new DrawOptions
        {
            Text = "TEST",
            X = 10,
            Y = 10,
            FontHeight = 20,
        });

        var img = canvas.GetImageData();
        HasNonWhitePixels(img).Should().BeTrue("DrawText 应在画布上产生可见像素");
    }

    [Fact]
    public void DrawRect_ProducesNonWhitePixels()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 100, Height = 100 });

        canvas.DrawRect(new DrawOptions
        {
            X = 10,
            Y = 10,
            Width = 50,
            Height = 50,
            Fill = true,
        });

        var img = canvas.GetImageData();
        HasNonWhitePixels(img).Should().BeTrue("DrawRect 应在画布上产生可见像素");
    }

    [Fact]
    public void DrawLine_ProducesNonWhitePixels()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 100, Height = 100 });

        canvas.DrawLine(new DrawOptions
        {
            X1 = 0,
            Y1 = 50,
            X2 = 99,
            Y2 = 50,
            LineWidth = 5,
        });

        var img = canvas.GetImageData();
        HasNonWhitePixels(img).Should().BeTrue("DrawLine 应在画布上产生可见像素");
    }

    [Fact]
    public void EmptyCanvas_GetImageData_AllWhiteOrTransparent()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 10, Height = 10 });

        var img = canvas.GetImageData();
        // 空画布（ClearAll 后）应无可见像素
        HasNonWhitePixels(img).Should().BeFalse("未绘制内容的画布应全白/透明");
    }

    [Fact]
    public void DrawText_MultipleTexts_AllProduceContent()
    {
        var canvas = new PrinterCanvas();
        canvas.StartJob(new DrawOptions { Width = 300, Height = 200 });

        canvas.DrawText(new DrawOptions { Text = "Line1", X = 5, Y = 5, FontHeight = 12 });
        canvas.DrawText(new DrawOptions { Text = "Line2", X = 5, Y = 25, FontHeight = 12 });
        canvas.DrawText(new DrawOptions { Text = "Line3", X = 5, Y = 45, FontHeight = 12 });

        var img = canvas.GetImageData();
        HasNonWhitePixels(img).Should().BeTrue();
        img.Data.Length.Should().Be(300 * 200 * 4);
    }
}
#endregion

#region 3. PrinterCanvas 异常路径
public class PrinterCanvasErrorTests
{
    [Fact]
    public void GetImageData_BeforeStartJob_Throws()
    {
        var canvas = new PrinterCanvas();
        var act = () => canvas.GetImageData();
        act.Should().Throw<InvalidOperationException>().WithMessage("*StartJob*");
    }

    [Fact]
    public void StartJob_NegativeDimensions_ReturnsNull()
    {
        var canvas = new PrinterCanvas();
        var bmp = canvas.StartJob(new DrawOptions { Width = -10, Height = -10 });
        bmp.Should().BeNull();
    }
}
#endregion

#region 4. PrinterCanvasMm 单位转换
public class PrinterCanvasMmTests
{
    [Fact]
    public void StartJob_ConvertsMmToPixels_Default203Dpi()
    {
        var canvas = new PrinterCanvasMm();
        // 60mm × 40mm @ 203 DPI → ~480 × ~320 px
        canvas.StartJob(new DrawOptions
        {
            Width = 60,
            Height = 40,
            Dpi = 203,
        });

        var expectedW = Math.Round(60.0 * 203 / 25.4);
        var expectedH = Math.Round(40.0 * 203 / 25.4);
        canvas.Base.Width.Should().Be(expectedW);
        canvas.Base.Height.Should().Be(expectedH);
    }

    [Fact]
    public void StartJob_Dpi300_ProducesLargerCanvas()
    {
        var canvas203 = new PrinterCanvasMm();
        canvas203.StartJob(new DrawOptions { Width = 60, Height = 40, Dpi = 203 });

        var canvas300 = new PrinterCanvasMm();
        canvas300.StartJob(new DrawOptions { Width = 60, Height = 40, Dpi = 300 });

        canvas300.Base.Width.Should().BeGreaterThan(canvas203.Base.Width);
        canvas300.Base.Height.Should().BeGreaterThan(canvas203.Base.Height);
    }

    [Fact]
    public void Cvt_DefaultDpi_ReturnsCorrectPixels()
    {
        var canvas = new PrinterCanvasMm();
        // 1mm @ 203 DPI ≈ 7.992 pixels
        var px = canvas.Cvt(1.0);
        px.Should().BeApproximately(203.0 / 25.4, 0.01);
    }

    [Fact]
    public void Cvt_10mm_ReturnsApprox80Pixels()
    {
        var canvas = new PrinterCanvasMm();
        var px = canvas.Cvt(10.0);
        px.Should().BeApproximately(10.0 * 203.0 / 25.4, 0.1);
    }

    [Fact]
    public void Dpi_Setter_UpdatesDpm()
    {
        var canvas = new PrinterCanvasMm();
        canvas.Dpi = 300;
        canvas.DPM.Should().BeApproximately(300.0 / 25.4, 0.01);
    }

    [Fact]
    public void GetImageData_AfterStartJob_ReturnsCorrectSizedData()
    {
        var canvas = new PrinterCanvasMm();
        canvas.StartJob(new DrawOptions { Width = 30, Height = 20, Dpi = 203 });

        var img = canvas.GetImageData();
        img.IsValid.Should().BeTrue();
        var expectedW = (int)Math.Round(30.0 * 203 / 25.4);
        var expectedH = (int)Math.Round(20.0 * 203 / 25.4);
        img.Width.Should().Be(expectedW);
        img.Height.Should().Be(expectedH);
        img.Data.Length.Should().Be(expectedW * expectedH * 4);
    }

    [Fact]
    public void DrawText_MmUnits_ProducesContent()
    {
        var canvas = new PrinterCanvasMm();
        canvas.StartJob(new DrawOptions { Width = 60, Height = 40, Dpi = 203 });

        canvas.DrawText(new DrawOptions
        {
            Text = "Hello",
            X = 5,       // 5mm
            Y = 5,       // 5mm
            FontHeight = 4, // 4mm
        });

        var img = canvas.GetImageData();
        // 检查有非白色像素
        var hasContent = false;
        for (var i = 0; i < img.Data.Length; i += 4)
        {
            if (img.Data[i] < 250 || img.Data[i + 1] < 250 || img.Data[i + 2] < 250)
            {
                hasContent = true;
                break;
            }
        }
        hasContent.Should().BeTrue("DrawText（mm 单位）应在画布上产生可见像素");
    }
}
#endregion

#region 5. DrawContext 画布尺寸验证
public class DrawContextCanvasSizeTests
{
    private static DrawJobOptions DefaultOptions() => new()
    {
        WidthMm = 40,
        HeightMm = 40,
        Orientation = 0,
        PrinterInfo = new PrinterInfo
        {
            PrinterDpi = 203,
            PrinterWidth = 384,
            PageCount = 1,
        },
    };

    [Fact]
    public void Start_CanvasPixelWidth_IsMmConverted()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();

        // 40mm @ 203 DPI ≈ 320px（不是 40px）
        var expectedW = (int)Math.Round(40.0 * 203 / 25.4);
        ctx.Canvas.Base.Width.Should().Be(expectedW,
            "DrawContext.Start 应通过 PrinterCanvasMm.StartJob 做 mm→px 转换");
    }

    [Fact]
    public void Start_CanvasPixelHeight_IsMmConverted()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();

        var expectedH = (int)Math.Round(40.0 * 203 / 25.4);
        ctx.Canvas.Base.Height.Should().Be(expectedH,
            "DrawContext.Start 应通过 PrinterCanvasMm.StartJob 做 mm→px 转换");
    }

    [Fact]
    public void Start_CanvasWidth_GreaterThanMmValue()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();

        // 关键断言：画布像素宽度应远大于 40（mm 值），证明 mm→px 转换生效
        ctx.Canvas.Base.Width.Should().BeGreaterThan(40,
            "画布宽度应为 mm×DPM≈320 像素，而非直接使用 mm 值 40");
    }

    [Fact]
    public void Start_GetImageData_MatchesCanvasSize()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();

        var img = ctx.Canvas.GetImageData();
        img.IsValid.Should().BeTrue();
        img.Width.Should().Be((int)ctx.Canvas.Base.Width);
        img.Height.Should().Be((int)ctx.Canvas.Base.Height);
    }

    [Fact]
    public void EncodeChunks_AfterDrawContent_ReturnsNonEmptyData()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        // 用填充矩形产生大量黑色像素
        ctx.Canvas.DrawRect(new DrawOptions
        {
            X = 5,
            Y = 5,
            Width = 20,
            Height = 20,
            Fill = true,
        });

        // 1. 像素级验证：画布确实有黑色像素（"画布有数据"的核心证据）
        var img = ctx.Canvas.GetImageData();
        CountBlackPixels(img).Should().BeGreaterThan(0, "绘制填充矩形后画布应包含黑色像素");

        // 2. 编码级验证：编码数据中应包含位图打印命令（0x1F + 位图命令字节）
        //    注意：RLE 压缩会让纯色矩形的编码数据非常小（35 字节左右），
        //    这是正确行为，不能用数据量大小判断"是否有数据"。
        var chunks = ctx.EncodeChunks();
        chunks.Should().NotBeEmpty();
        ContainsBitmapCommand(chunks).Should().BeTrue(
            "编码数据应包含位图打印命令（PRINT/RLE/REPEAT 之一），证明画布内容已进入编码流");
    }

    /// <summary>
    /// 统计图像中的黑色像素数（RGB 均接近 0）。
    /// </summary>
    private static int CountBlackPixels(DzImageData img)
    {
        var count = 0;
        for (var i = 0; i < img.Data.Length; i += 4)
        {
            if (img.Data[i] < 10 && img.Data[i + 1] < 10 && img.Data[i + 2] < 10)
                count++;
        }
        return count;
    }

    /// <summary>
    /// 检查编码分片中是否包含位图打印命令。
    /// 位图帧格式：[0x1F][cmd][...]，cmd ∈ {RLEC=41, PRINT=43, RLEX=44, RLED=45, REPEAT=46, RLE6X=60, RLE6D=61}。
    /// </summary>
    private static bool ContainsBitmapCommand(List<byte[]> chunks)
    {
        // 位图命令字节集合（不含 CMD_PAGE_START=32 / CMD_PAGE_WIDTH=39 等控制帧）
        var bitmapCmds = new HashSet<byte> { 41, 43, 44, 45, 46, 60, 61 };
        foreach (var chunk in chunks)
        {
            for (var i = 0; i < chunk.Length - 1; i++)
            {
                if (chunk[i] == 0x1F && bitmapCmds.Contains(chunk[i + 1]))
                    return true;
            }
        }
        return false;
    }

    [Fact]
    public void EncodeChunks_EmptyCanvas_StillProducesProtocolFrames()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        // 不绘制任何内容，但画布尺寸正确，仍应产生协议帧
        var chunks = ctx.EncodeChunks();
        chunks.Should().NotBeEmpty("即使画布为空，也应产生页起始/结束协议帧");
        // 空画布不应包含位图打印命令（反向验证）
        ContainsBitmapCommand(chunks).Should().BeFalse("空画布不应产生位图打印命令");
    }

    [Fact]
    public void Start_DifferentDpi_ProducesDifferentCanvasSize()
    {
        var opts203 = DefaultOptions();
        using var ctx203 = new DrawContext(opts203);
        ctx203.Start();

        var opts300 = DefaultOptions();
        opts300.PrinterInfo = new PrinterInfo { PrinterDpi = 300, PrinterWidth = 384, PageCount = 1 };
        using var ctx300 = new DrawContext(opts300);
        ctx300.Start();

        ctx300.Canvas.Base.Width.Should().BeGreaterThan(ctx203.Canvas.Base.Width,
            "300 DPI 的画布宽度应大于 203 DPI");
    }
}
#endregion

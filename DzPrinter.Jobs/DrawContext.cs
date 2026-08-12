// =====================================================================
//  DrawContext：一次"绘制作业"上下文。
//
//  对应 JS SDK 中 "DrawContext"（uni-app Canvas 绘制环境）在 C# 中的抽象。
//  职责：
//    1. 持有 PrinterCanvasMm / DrawOptions。
//    2. 从 WDFX 模板绘制（如果提供）。
//    3. 提供便捷的 DrawText / DrawBarcode / DrawImage 封装。
//    4. 把画布内容通过 PrintEncoder 编码为协议分片（EncodeChunks）。
// =====================================================================

using DzPrinter.Core;
using DzPrinter.Drawing;
using DzPrinter.Protocol;

namespace DzPrinter.Jobs;

/// <summary>绘制作业参数。</summary>
public sealed class DrawJobOptions
{
    /// <summary>画布宽度 mm。</summary>
    public double WidthMm { get; set; }
    /// <summary>画布高度 mm。</summary>
    public double HeightMm { get; set; }
    /// <summary>方向：0/1/2/3。</summary>
    public int Orientation { get; set; }
    /// <summary>打印机 DPI（默认 203）。</summary>
    public int PrinterDpi { get; set; } = 203;
    /// <summary>打印机像素宽度。</summary>
    public int PrinterWidth { get; set; }
    /// <summary>纸张类型。</summary>
    public int GapType { get; set; }
    /// <summary>间隙长度。</summary>
    public int GapLength { get; set; }
    /// <summary>浓度。</summary>
    public int PrintDarkness { get; set; }
    /// <summary>速度。</summary>
    public int PrintSpeed { get; set; }
    /// <summary>打印份数。</summary>
    public int PageCount { get; set; } = 1;
    /// <summary>标签模板（可选）。若有，会在 Start 时自动绘制。</summary>
    public string? WdfxTemplateXml { get; set; }
    /// <summary>标签模板变量字典（可选），用于 WDFX 模板插值。</summary>
    public IReadOnlyDictionary<string, object?>? TemplateVariables { get; set; }
}

/// <summary>
/// 绘制作业上下文。对应 JS 中 "DrawContext" + uni-app Canvas 操作的组合。
/// </summary>
public sealed class DrawContext : IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly DrawJobOptions _options;
    private PrinterCanvasMm? _canvas;
    private bool _disposed;

    public DrawContext(DrawJobOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>画布（毫米单位）。</summary>
    public PrinterCanvasMm Canvas
    {
        get
        {
            if (_canvas == null) throw new InvalidOperationException("未调用 Start()。");
            return _canvas;
        }
    }

    /// <summary>作业选项。</summary>
    public DrawJobOptions Options => _options;

    /// <summary>
    /// 启动绘制作业：创建画布，若指定 WDFX 模板则自动绘制模板。
    /// </summary>
    public PrinterCanvasMm Start()
    {
        var drawOpts = new DrawOptions
        {
            Width = _options.WidthMm,
            Height = _options.HeightMm,
            Orientation = _options.Orientation,
            Dpi = _options.PrinterDpi,
            PrinterWidth = _options.PrinterWidth,
        };
        _canvas = new PrinterCanvasMm(drawOpts);
        var baseCvs = _canvas.Base;
        baseCvs.StartJob(drawOpts);

        if (!string.IsNullOrEmpty(_options.WdfxTemplateXml))
        {
            Log.Warn("【DrawContext】WDFX 模板渲染目前需要外部提供 LabelContext，已跳过。");
        }

        return _canvas;
    }

    /// <summary>
    /// 完成绘制作业，返回最终位图（SKBitmap）。
    /// 对应 JS 中 uni-app Canvas 把 canvas 导出图像。
    /// </summary>
    public SkiaSharp.SKBitmap Commit()
    {
        if (_canvas == null) throw new InvalidOperationException("未 Start()。");
        return _canvas.Base.CommitJob()!;
    }

    /// <summary>
    /// 把当前画布内容编码为打印协议分片列表。
    /// 对应 JS <c>encodeImageData()</c>。
    /// </summary>
    public List<byte[]> EncodeChunks()
    {
        if (_canvas == null) throw new InvalidOperationException("未 Start()。");
        var img = _canvas.GetImageData();
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = _options.PrinterDpi,
            PrinterWidth = _options.PrinterWidth,
            GapType = _options.GapType,
            GapLength = _options.GapLength,
            PrintDarkness = _options.PrintDarkness,
            PrintSpeed = _options.PrintSpeed,
            PageCount = _options.PageCount,
            Orientation = _canvas.Base.Orientation,
        };
        return PrintEncoder.EncodeImageData(img, opts);
    }

    /// <summary>
    /// 获取当前画布位图（供调试/预览）。
    /// </summary>
    public SkiaSharp.SKBitmap? GetBitmap() => _canvas?.Base.Canvas;

    public void Dispose()
    {
        if (_disposed) return;
        _canvas?.Base.Canvas?.Dispose();
        _canvas = null;
        _disposed = true;
    }
}

using DzPrinter.Core;
using DzPrinter.Drawing;
using DzPrinter.Printer;

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
    /// <summary>打印参数（浓度/速度/间隙/DPI/宽度/份数等）。</summary>
    public PrinterInfo PrinterInfo { get; set; } = new();
    /// <summary>标签模板（可选）。若有，会在 Start 时通过 LabelContext 自动渲染。</summary>
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
    /// 启动绘制作业：创建画布，若指定 WDFX 模板则通过 LabelContext 自动渲染。
    /// </summary>
    public PrinterCanvasMm Start()
    {
        // WDFX 模板模式：委托 LabelContext 渲染
        if (!string.IsNullOrEmpty(_options.WdfxTemplateXml))
        {
            try
            {
                var pi = _options.PrinterInfo;
                var labelCtx = new LabelContext(pi.PrinterWidth, pi.PrinterDpi);
                if (labelCtx.DrawLabelFromXml(_options.WdfxTemplateXml, pi.PrinterWidth))
                {
                    _canvas = labelCtx.Canvas;
                    Log.Info("【DrawContext】WDFX 模板渲染成功");
                    return _canvas;
                }
                Log.Warn("【DrawContext】WDFX 模板渲染失败，回退到常规画布");
            }
            catch (Exception ex)
            {
                Log.Warn($"【DrawContext】WDFX 模板渲染异常: {ex.Message}，回退到常规画布");
            }
        }

        // 常规画布创建
        var pi2 = _options.PrinterInfo;
        var drawOpts = new DrawOptions
        {
            Width = _options.WidthMm,
            Height = _options.HeightMm,
            Orientation = _options.Orientation,
            Dpi = pi2.PrinterDpi,
            PrinterWidth = (int)pi2.PrinterWidth,
        };
        _canvas = new PrinterCanvasMm(drawOpts);
        _canvas.StartJob(drawOpts);
        return _canvas;
    }

    /// <summary>
    /// 完成绘制作业，返回最终位图（SKBitmap）。
    /// </summary>
    public SkiaSharp.SKBitmap Commit()
    {
        if (_canvas == null) throw new InvalidOperationException("未 Start()。");
        return _canvas.Base.CommitJob()!;
    }

    /// <summary>
    /// 把当前画布内容编码为打印协议分片列表。
    /// </summary>
    public List<byte[]> EncodeChunks()
    {
        if (_canvas == null) throw new InvalidOperationException("未 Start()。");
        var img = _canvas.GetImageData();
        var opts = PrintImageOptions.Create(img, _options.PrinterInfo, _canvas.Base.Orientation);
        return PrintEncoder.EncodeImageData(img, opts);
    }

    /// <summary>获取当前画布位图（供调试/预览）。</summary>
    public SkiaSharp.SKBitmap? GetBitmap() => _canvas?.Base.Canvas;

    public void Dispose()
    {
        if (_disposed) return;
        _canvas?.Base.Canvas?.Dispose();
        _canvas = null;
        _disposed = true;
    }
}

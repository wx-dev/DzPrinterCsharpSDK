using DzPrinter.Barcode;
using DzPrinter.Core;
using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport;
using DzPrinter.Transport.BleXPlat;
using DzPrinter.Transport.File;
using DzPrinter.Transport.Hid;
using DzPrinter.Web.Models;
using SkiaSharp;
using System.Text;

namespace DzPrinter.Web.Services;

public sealed class PrinterService : IDisposable
{
    private readonly ILogger<PrinterService> _logger;
    private readonly IConfiguration _config;
    private readonly object _lock = new();

    private IDeviceTransport? _transport;
    private DzPrinterManager? _manager;
    private IReadOnlyList<PrinterDevice>? _lastDiscovered;

    public PrinterService(ILogger<PrinterService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    // ---- 设备发现 ----

    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(
        string? transportType = null,
        CancellationToken ct = default)
    {
        var manager = GetOrCreateManager(transportType);
        _lastDiscovered = await manager.DiscoverAsync(LpaDeviceType.Auto, ct);
        return _lastDiscovered;
    }

    // ---- 连接 ----

    public async Task<LpaResult> ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        var manager = GetOrCreateManager();
        if (_lastDiscovered == null)
        {
            _lastDiscovered = await manager.DiscoverAsync(LpaDeviceType.Auto, ct);
        }

        var device = _lastDiscovered.FirstOrDefault(d => d.DeviceId == deviceId);
        if (device == null)
            return LpaResult.ErrorParam;

        return await manager.ConnectAsync(device, ct);
    }

    // ---- 断开 ----

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_manager != null)
            await _manager.DisconnectAsync(ct);
    }

    // ---- 状态查询 ----

    public bool IsConnected => _manager?.IsConnected ?? false;

    public DeviceInfo? ConnectedDevice => _manager?.ConnectedDevice;

    public async Task<PrinterStatusCode?> GetPrintableStatusAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (_manager == null) return null;
        return await _manager.Api.GetPrintableStatusAsync(timeoutMs, ct);
    }

    public async Task<PrinterHardwareInfo?> GetPrinterInfoAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (_manager == null) return null;
        return await _manager.Api.GetPrinterInfoAsync(timeoutMs, ct);
    }

    // ---- 打印 ----

    public async Task<(LpaResult result, int chunks, string? previewPath, string? previewBase64)> PrintAsync(
        PrintRequest request,
        CancellationToken ct = default)
    {
        var manager = GetOrCreateManager();
        if (!manager.IsConnected)
            return (LpaResult.ErrorNoPrinter, 0, null, null);

        var (bitmap, ctx) = DrawToBitmap(request);

        // 在 ctx 存活期间完成所有 bitmap 访问（原生内存）
        string? previewBase64 = null;
        string? previewPath = null;

        if (bitmap != null)
        {
            try
            {
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                previewBase64 = Convert.ToBase64String(data.ToArray());

                var pngPath = _config["Printer:File:PngOutputPath"] ?? "output/preview.png";
                var dir = Path.GetDirectoryName(pngPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using (var fs = File.Create(pngPath))
                    data.SaveTo(fs);
                previewPath = Path.GetFullPath(pngPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save preview PNG");
            }
        }

        // 用同一个 ctx 打印
        var result = await manager.PrintAsync(ctx, ct);
        var chunks = 0;
        try { chunks = ctx.EncodeChunks().Count; } catch { }
        ctx.Dispose();

        return (result, chunks, previewPath, previewBase64);
    }

    // ---- 预览（只绘制不打印） ----

    public string? Preview(PrintRequest request)
    {
        var (bitmap, ctx) = DrawToBitmap(request);
        // 必须在 ctx 存活期间编码，bitmap 是 canvas 内部原生指针
        var base64 = BitmapToBase64(bitmap);
        ctx.Dispose();
        return base64;
    }

    private (SKBitmap? bitmap, DrawContext ctx) DrawToBitmap(PrintRequest request)
    {
        var manager = GetOrCreateManager();
        var options = new DrawJobOptions
        {
            WidthMm = request.WidthMm,
            HeightMm = request.HeightMm,
            Orientation = request.Orientation,
            PrinterInfo = request.PrinterInfo?.ToPrinterInfo() ?? new PrinterInfo(),
        };

        var ctx = manager.CreateDrawContext(options);
        ctx.Start();

        foreach (var instruction in request.Instructions)
        {
            ExecuteInstruction(ctx.Canvas, instruction);
        }

        return (ctx.GetBitmap(), ctx);
    }

    private static string? BitmapToBase64(SKBitmap? bitmap)
    {
        if (bitmap == null) return null;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    // ---- 发送原始数据 ----

    public async Task<LpaResult> SendRawAsync(byte[] data, CancellationToken ct = default)
    {
        if (_manager == null || !_manager.IsConnected)
            return LpaResult.ErrorNoPrinter;
        return await _manager.SendRawAsync(data, ct);
    }

    // ---- 指令执行 ----

    private void ExecuteInstruction(PrinterCanvasMm canvas, DrawInstructionDto inst)
    {
        var opt = new DrawOptions
        {
            X = inst.X,
            Y = inst.Y,
            Width = inst.Width,
            Height = inst.Height,
            Rotation = inst.Rotation,
            Color = inst.Color,
            BgColor = inst.BgColor,
            RotateMode = ParseRotateMode(inst.RotateMode),
            Padding = inst.Padding,
        };

        switch (inst.Type.ToLowerInvariant())
        {
            case "drawtext":
                opt.Text = inst.Text;
                opt.FontHeight = inst.FontHeight;
                opt.FontName = inst.FontName;
                opt.FontStyle = ParseFontStyle(inst.FontStyle);
                opt.HorizontalAlignment = ParseAlignment(inst.HorizontalAlignment);
                opt.VerticalAlignment = ParseAlignment(inst.VerticalAlignment);
                opt.AutoShrink = inst.AutoShrink;
                opt.MinFontHeight = inst.MinFontHeight;
                opt.AutoReturn = ParseWrapMode(inst.AutoReturn);
                opt.CharSpace = inst.CharSpace;
                opt.LineSpace = inst.LineSpace;
                canvas.DrawText(opt);
                break;

            case "drawrect":
                opt.Fill = inst.Fill;
                opt.LineWidth = inst.LineWidth;
                opt.LineJoin = inst.LineJoin;
                opt.BorderAlign = ParseBorderAlign(inst.BorderAlign);
                opt.DashLens = inst.DashLens;
                canvas.DrawRect(opt);
                break;

            case "drawroundrect":
                opt.Fill = inst.Fill;
                opt.LineWidth = inst.LineWidth;
                opt.CornerWidth = inst.Radius;
                opt.CornerHeight = inst.Radius;
                opt.LineJoin = inst.LineJoin;
                opt.DashLens = inst.DashLens;
                canvas.DrawRoundRect(opt);
                break;

            case "drawline":
                opt.X1 = inst.X1;
                opt.Y1 = inst.Y1;
                opt.X2 = inst.X2;
                opt.Y2 = inst.Y2;
                opt.LineWidth = inst.LineWidth;
                opt.DashLens = inst.DashLens;
                canvas.DrawLine(opt);
                break;

            case "drawcircle":
                opt.Radius = inst.Radius;
                opt.Fill = inst.Fill;
                opt.LineWidth = inst.LineWidth;
                canvas.DrawCircle(opt);
                break;

            case "drawellipse":
                opt.Fill = inst.Fill;
                opt.LineWidth = inst.LineWidth;
                canvas.DrawEllipse(opt);
                break;

            case "draw1dbarcode":
                var barcodeResult = Barcode1DCreator.Create1DBarcode(new Barcode1DRequest
                {
                    Text = inst.BarcodeData ?? string.Empty,
                    BarcodeType = ParseBarcodeType(inst.BarcodeType),
                });
                if (barcodeResult != null && barcodeResult.Items.Count > 0)
                {
                    opt.Datas = barcodeResult.Items.Select(i => new BarcodeItem(i.Data, i.Text)).ToList();
                    opt.TextHeight = inst.TextHeight;
                    opt.HorizontalAlignment = ParseAlignment(inst.HorizontalAlignment);
                    opt.VerticalAlignment = ParseAlignment(inst.VerticalAlignment);
                    opt.TextAlign = ParseAlignment(inst.TextAlign);
                    opt.TextAlignment = ParseAlignment(inst.TextAlign);
                    opt.TextFlag = ParseTextPosition(inst.TextPosition);
                    opt.Flag = opt.TextFlag;
                    opt.TopText = inst.TopText ?? false;
                    opt.AutoScaleLevel = inst.AutoScaleLevel ?? 2;
                    canvas.Draw1DBarcode(opt);
                }
                break;

            case "draw2dbarcode":
            case "drawqrcode":
                var barcode2DType = (inst.Barcode2DType ?? "qrcode").ToLowerInvariant();
                var req2D = new Barcode2DRequest
                {
                    Text = inst.QrText ?? inst.Text ?? string.Empty,
                    BarcodeType = barcode2DType,
                    EccLevel = ParseEccLevel(inst.EccLevel),
                    Version = inst.QrVersion,
                    QrMask = inst.QrMask,
                };

                BitMatrix? matrix2D = barcode2DType switch
                {
                    "qrcode" or "auto" or "" => Barcode2DCreator.CreateQRCode(req2D),
                    "pdf417" => Barcode2DCreator.Create2DBarcode(req2D),
                    "datamatrix" => Barcode2DCreator.Create2DBarcode(req2D),
                    "gridmatrix" => Barcode2DCreator.Create2DBarcode(req2D),
                    _ => Barcode2DCreator.Create2DBarcode(req2D),
                };

                if (matrix2D != null)
                {
                    opt.Data = matrix2D;
                    opt.ZoneSize = inst.ZoneSize ?? 2;
                    opt.BarPixels = inst.BarPixels ?? 4;
                    opt.AutoScaleLevel = inst.AutoScaleLevel ?? 2;
                    opt.HorizontalAlignment = ParseAlignment(inst.HorizontalAlignment);
                    opt.VerticalAlignment = ParseAlignment(inst.VerticalAlignment);
                    canvas.Draw2DBarcode(opt);
                }
                else
                {
                    _logger.LogWarning("2D barcode creation returned null for type {Type}", barcode2DType);
                }
                break;

            case "drawimage":
                var imageBytes = LoadImageBytes(inst.ImageBase64, inst.ImageUrl);
                if (imageBytes != null)
                {
                    opt.Image = SKBitmap.Decode(imageBytes);
                    opt.Sx = inst.Sx;
                    opt.Sy = inst.Sy;
                    opt.Swidth = inst.Swidth;
                    opt.Sheight = inst.Sheight;
                    canvas.DrawImage(opt);
                }
                break;
        }
    }

    private byte[]? LoadImageBytes(string? base64, string? url)
    {
        try
        {
            if (!string.IsNullOrEmpty(base64))
                return Convert.FromBase64String(base64);
            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                using var client = new HttpClient();
                return client.GetByteArrayAsync(url).Result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load image");
        }
        return null;
    }

    // ---- Transport 管理 ----

    private DzPrinterManager GetOrCreateManager(string? transportType = null)
    {
        lock (_lock)
        {
            if (_manager != null && transportType == null)
                return _manager;

            var type = (transportType ?? _config["Printer:Transport"] ?? "file").ToLowerInvariant();
            (_transport as IDisposable)?.Dispose();
            _transport = CreateTransport(type);

            _manager?.Dispose();
            _manager = new DzPrinterManager(_transport);

            _logger.LogInformation("Created DzPrinterManager with {Transport} transport", type);
            return _manager;
        }
    }

    private IDeviceTransport CreateTransport(string type) => type switch
    {
        "ble" => new BleXPlatTransport(new BleXPlatTransportOptions
        {
            ServiceUuid = Guid.Parse(_config["Printer:Ble:ServiceUuid"] ?? "000018f0-0000-1000-8000-00805f9b34fb"),
            PackSize = int.Parse(_config["Printer:Ble:PackSize"] ?? "20"),
            ScanTimeoutMs = int.Parse(_config["Printer:Ble:ScanTimeoutMs"] ?? "5000"),
        }),

        "hid" => new HidSharpTransport(new HidTransportOptions
        {
            NameContains = _config["Printer:Hid:NameContains"] ?? "Printer",
            ReportId = byte.Parse(_config["Printer:Hid:ReportId"] ?? "0"),
            ReadTimeoutMs = int.Parse(_config["Printer:Hid:ReadTimeoutMs"] ?? "500"),
            WriteTimeoutMs = int.Parse(_config["Printer:Hid:WriteTimeoutMs"] ?? "2000"),
            SendIntervalMs = int.Parse(_config["Printer:Hid:SendIntervalMs"] ?? "20"),
        }),

        "file" => new FileTransport(new FileTransportOptions
        {
            OutputPath = _config["Printer:File:OutputPath"] ?? "output/print.bin",
            Format = string.Equals(_config["Printer:File:Format"], "HexText", StringComparison.OrdinalIgnoreCase)
                ? FileOutputFormat.HexText
                : FileOutputFormat.RawBinary,
            SavePngPreview = bool.Parse(_config["Printer:File:SavePngPreview"] ?? "true"),
            PngOutputPath = _config["Printer:File:PngOutputPath"] ?? "output/preview.png",
            PngScale = int.Parse(_config["Printer:File:PngScale"] ?? "2"),
        }),

        _ => throw new ArgumentException($"Unknown transport type: {type}"),
    };

    // ---- 枚举解析辅助 ----

    private static FontStyle? ParseFontStyle(string? style) => style?.ToLowerInvariant() switch
    {
        "bold" => FontStyle.Bold,
        "italic" => FontStyle.Italic,
        "underline" => FontStyle.Underline,
        "bolditalic" or "italicbold" => FontStyle.Bold | FontStyle.Italic,
        _ => null,
    };

    private static Alignment? ParseAlignment(string? align) => align?.ToLowerInvariant() switch
    {
        "left" or "start" => Alignment.Start,
        "center" => Alignment.Center,
        "right" or "end" => Alignment.End,
        "stretch" => Alignment.Stretch,
        _ => null,
    };

    private static WrapMode? ParseWrapMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "none" => WrapMode.None,
        "char" => WrapMode.Char,
        "word" or "auto" or "wrap" => WrapMode.Word,
        _ => null,
    };

    private static BarcodeType ParseBarcodeType(string? type) => type?.ToUpperInvariant() switch
    {
        "CODE128" => BarcodeType.CODE128,
        "CODE39" => BarcodeType.CODE39,
        "CODE93" => BarcodeType.CODE93,
        "EAN13" => BarcodeType.EAN13,
        "EAN8" => BarcodeType.EAN8,
        "UPC_A" or "UPCA" => BarcodeType.UPC_A,
        "UPC_E" or "UPCE" => BarcodeType.UPC_E,
        "ITF25" or "ITF" => BarcodeType.ITF25,
        "CODABAR" => BarcodeType.CODABAR,
        "ISBN" => BarcodeType.ISBN,
        "ITF14" => BarcodeType.ITF14,
        "GS1_128" or "GS1128" or "EAN128" => BarcodeType.GS1_128,
        "AUTO" => BarcodeType.AUTO,
        _ => BarcodeType.AUTO,
    };

    private static RotateMode? ParseRotateMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "auto" => RotateMode.Auto,
        "rotatecanvas" => RotateMode.RotateCanvas,
        "rotatecontent" => RotateMode.RotateContent,
        _ => null,
    };

    private static BorderAlign? ParseBorderAlign(string? align) => align?.ToLowerInvariant() switch
    {
        "none" => BorderAlign.None,
        "left" => BorderAlign.Left,
        "right" => BorderAlign.Right,
        "top" => BorderAlign.Top,
        "bottom" => BorderAlign.Bottom,
        "inner" => BorderAlign.Inner,
        "outer" => BorderAlign.Outer,
        _ => null,
    };

    private static int? ParseTextPosition(string? pos) => pos?.ToLowerInvariant() switch
    {
        "top" => 0,
        "bottom" => 1,
        "none" => 2,
        _ => null,
    };

    private static DzPrinter.Barcode.EccLevel ParseEccLevel(string? level) => level?.ToUpperInvariant() switch
    {
        "L" => DzPrinter.Barcode.EccLevel.Low,
        "M" => DzPrinter.Barcode.EccLevel.Middle,
        "Q" => DzPrinter.Barcode.EccLevel.Quality,
        "H" => DzPrinter.Barcode.EccLevel.High,
        _ => DzPrinter.Barcode.EccLevel.Middle,
    };

    public void Dispose()
    {
        _manager?.Dispose();
        (_transport as IDisposable)?.Dispose();
    }
}

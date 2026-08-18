// =====================================================================
//  DzPrinter Windows 示例：BLE + HID + File 打印。
//
//  本示例演示如何通过 DzPrinterManager + 传输层实现完整的
//  "发现设备 → 连接 → 绘制 → 打印 → 断开" 流程。
//
//  用法：
//    dotnet run --project DzPrinter.Samples -- ble        # BLE 打印
//    dotnet run --project DzPrinter.Samples -- hid        # HID 打印
//    dotnet run --project DzPrinter.Samples -- file       # File 输出 (二进制 + PNG 预览)
//    dotnet run --project DzPrinter.Samples -- file-hex   # File 输出 (十六进制文本 + PNG 预览)
//    dotnet run --project DzPrinter.Samples -- list       # 仅列出设备
// =====================================================================

using DzPrinter.Barcode;
using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport;
using DzPrinter.Transport.Ble;
using DzPrinter.Transport.File;
using DzPrinter.Transport.Hid;

// 注册 GBK 编码（打印机中文需要）
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "file";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== DzPrinter Windows 示例 ===");
Console.WriteLine($"模式: {mode}");
Console.WriteLine();

switch (mode)
{
    case "ble":
        await RunBleSampleAsync();
        break;
    case "hid":
        await RunHidSampleAsync();
        break;
    case "file":
        await RunFileSampleAsync(FileOutputFormat.RawBinary);
        break;
    case "file-hex":
        await RunFileSampleAsync(FileOutputFormat.HexText);
        break;
    case "list":
        await ListDevicesAsync();
        break;
    default:
        Console.WriteLine("用法: dotnet run -- [ble|hid|file|file-hex|list]");
        break;
}

// =====================================================================
//  BLE 打印示例
// =====================================================================
static async Task RunBleSampleAsync()
{
    Console.WriteLine("[BLE] 创建 WinRtBleTransport ...");
    using var transport = new WinRtBleTransport(new BleTransportOptions
    {
        ServiceUuid = new Guid("000018F0-0000-1000-8000-00805F9B34FB"),
        PackSize = 20,         // MTU=23 - 3字节ATT头
        ScanTimeoutMs = 5000,  // 扫描 5 秒
    });

    await PrintWithTransportAsync(transport, "BLE");
}

// =====================================================================
//  HID 打印示例
// =====================================================================
static async Task RunHidSampleAsync()
{
    Console.WriteLine("[HID] 创建 HidSharpTransport ...");
    using var transport = new HidSharpTransport(new HidTransportOptions
    {
        // 按需填写 VendorId / ProductId 过滤：
        // VendorId = 0x0483,
        // ProductId = 0x5750,
        NameContains = "Printer", // 按名称模糊匹配
        ReportId = 0,
    });

    await PrintWithTransportAsync(transport, "HID");
}

// =====================================================================
//  File（文件输出）打印示例
//
//  将打印数据写入本地文件（二进制或十六进制文本），并自动解码生成 PNG 预览。
//  无需真实打印机即可验证绘制与编码结果。
// =====================================================================
static async Task RunFileSampleAsync(FileOutputFormat format)
{
    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var ext = format == FileOutputFormat.RawBinary ? "bin" : "hex";
    var outDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "DzPrinter_Output");
    Directory.CreateDirectory(outDir);

    var baseName = $"label_{stamp}";
    var outputPath = Path.Combine(outDir, $"{baseName}.{ext}");
    var pngPath = Path.Combine(outDir, $"{baseName}.png");

    Console.WriteLine($"[FILE] 创建 FileTransport ({format}) ...");
    Console.WriteLine($"[FILE]   数据文件: {outputPath}");
    Console.WriteLine($"[FILE]   PNG 预览: {pngPath}");

    using var transport = new FileTransport(new FileTransportOptions
    {
        OutputPath = outputPath,
        Format = format,
        Append = false,
        SavePngPreview = true,
        PngOutputPath = pngPath,
        PngBackground = 1, // 白底黑字
        PngScale = 2,      // 放大 2 倍便于查看
    });

    await PrintWithFileTransportAsync(transport, "File", outputPath, pngPath);
}

/// <summary>
/// File 传输专用流程：跳过真实设备的发现/选择，直接构造虚拟设备并连接。
/// </summary>
static async Task PrintWithFileTransportAsync(
    IDeviceTransport transport,
    string label,
    string rawOutputPath,
    string pngOutputPath)
{
    using var manager = new DzPrinterManager(transport);

    // 1. 发现（FileTransport 返回一台虚拟 D60-File 打印机）
    Console.WriteLine($"[{label}] 枚举虚拟设备 ...");
    IReadOnlyList<PrinterDevice> devices;
    try
    {
        devices = await manager.DiscoverAsync(LpaDeviceType.File);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{label}] 发现失败: {ex.Message}");
        return;
    }

    if (devices.Count == 0)
    {
        Console.WriteLine($"[{label}] 未发现虚拟设备（未预期）。");
        return;
    }

    var device = devices[0];
    Console.WriteLine($"[{label}] 虚拟设备: {device.Name}  (ID: {device.DeviceId})");

    // 2. 连接（打开文件流）
    Console.WriteLine($"[{label}] 打开输出文件 ...");
    try
    {
        var connectResult = await manager.ConnectAsync(device);
        if (connectResult != LpaResult.Ok)
        {
            Console.WriteLine($"[{label}] 连接失败: {connectResult}");
            return;
        }
        if (!manager.IsConnected)
        {
            Console.WriteLine($"[{label}] 连接失败: 状态未就绪");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{label}] 连接异常: {ex.Message}");
        return;
    }
    Console.WriteLine($"[{label}] 连接就绪。");

    try
    {
        // 3. 创建画布并绘制
        Console.WriteLine($"[{label}] 创建画布 48×220mm ...");
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 48,
            HeightMm = 220,
            Orientation = 0,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
                PageCount = 1,
            },
        });
        ctx.Start();

        DrawComplexLabel(ctx.Canvas, label);

        // 4. 打印（写入文件）
        Console.WriteLine($"[{label}] 正在写入打印数据 ...");
        var result = await manager.PrintAsync(ctx);
        Console.WriteLine($"[{label}] 写入结果: {result}");

        if (result == LpaResult.Ok)
        {
            Console.WriteLine($"[{label}] ✓ 数据已写入: {rawOutputPath}");
        }
        else
        {
            Console.WriteLine($"[{label}] ✗ 写入失败: {result}");
        }
    }
    finally
    {
        // 5. 断开（关闭文件流 + 生成 PNG 预览）
        Console.WriteLine($"[{label}] 关闭输出文件（生成 PNG 预览） ...");
        await manager.DisconnectAsync();
        Console.WriteLine($"[{label}] ✓ 已断开。");

        if (File.Exists(rawOutputPath))
        {
            var info = new FileInfo(rawOutputPath);
            Console.WriteLine($"[{label}] RAW : {rawOutputPath}  ({info.Length} bytes)");
        }
        if (File.Exists(pngOutputPath))
        {
            var info = new FileInfo(pngOutputPath);
            Console.WriteLine($"[{label}] PNG : {pngOutputPath}  ({info.Length} bytes)");
        }
    }
}

// =====================================================================
//  仅列出设备（不打印）
// =====================================================================
static async Task ListDevicesAsync()
{
    Console.WriteLine("--- BLE 设备 ---");
    using var bleTransport = new WinRtBleTransport(new BleTransportOptions { ScanTimeoutMs = 5000 });
    await ListFromTransportAsync(bleTransport);

    Console.WriteLine();
    Console.WriteLine("--- HID 设备 ---");
    using var hidTransport = new HidSharpTransport();
    await ListFromTransportAsync(hidTransport);
}

static async Task ListFromTransportAsync(IDeviceTransport transport)
{
    try
    {
        var devices = await transport.DiscoverAsync();
        if (devices.Count == 0)
        {
            Console.WriteLine("  （未发现设备）");
            return;
        }
        foreach (var d in devices)
        {
            Console.WriteLine($"  [{d.TransportType}] {d.DeviceName}  (ID: {d.DeviceId})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  扫描失败: {ex.Message}");
    }
}

// =====================================================================
//  通用打印流程：发现 → 选择 → 连接 → 绘制 → 打印 → 断开
// =====================================================================
static async Task PrintWithTransportAsync(IDeviceTransport transport, string label)
{
    using var manager = new DzPrinterManager(transport);

    // 1. 发现设备
    Console.WriteLine($"[{label}] 正在扫描设备 ...");
    IReadOnlyList<PrinterDevice> devices;
    try
    {
        devices = await manager.DiscoverAsync(label == "BLE" ? LpaDeviceType.Ble : LpaDeviceType.UsbHid);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{label}] 扫描失败: {ex.Message}");
        return;
    }

    if (devices.Count == 0)
    {
        Console.WriteLine($"[{label}] 未发现设备。");
        return;
    }

    Console.WriteLine($"[{label}] 发现 {devices.Count} 台设备:");
    for (var i = 0; i < devices.Count; i++)
    {
        Console.WriteLine($"  {i}: {devices[i].Name}  (ID: {devices[i].DeviceId})");
    }

    // 2. 选择设备（默认第一台）
    var idx = 0;
    if (devices.Count > 1)
    {
        Console.Write($"选择设备编号 (默认 0): ");
        var input = Console.ReadLine();
        if (int.TryParse(input, out var n) && n >= 0 && n < devices.Count)
            idx = n;
    }
    var device = devices[idx];
    Console.WriteLine($"[{label}] 选中: {device.Name}");

    // 3. 连接
    Console.WriteLine($"[{label}] 正在连接 ...");
    try
    {
        var connectResult = await manager.ConnectAsync(device);
        if (connectResult != LpaResult.Ok)
        {
            Console.WriteLine($"[{label}] 连接失败: {connectResult}");
            return;
        }
        if (!manager.IsConnected)
        {
            Console.WriteLine($"[{label}] 连接失败: 连接状态未就绪");
            return;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{label}] 连接异常: {ex.Message}");
        return;
    }
    Console.WriteLine($"[{label}] 连接成功！");

    try
    {
        // 4. 创建画布并绘制内容
        Console.WriteLine($"[{label}] 创建画布 48×220mm ...");
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 48,
            HeightMm = 220,
            Orientation = 0,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
                PageCount = 1,
            },
        });
        ctx.Start();

        DrawComplexLabel(ctx.Canvas, label);

        // 5. 打印
        Console.WriteLine($"[{label}] 正在打印 ...");
        var result = await manager.PrintAsync(ctx);
        Console.WriteLine($"[{label}] 打印结果: {result}");

        if (result == LpaResult.Ok)
        {
            Console.WriteLine($"[{label}] 打印成功！");
        }
        else
        {
            Console.WriteLine($"[{label}] 打印失败: {result}");
        }
    }
    finally
    {
        // 6. 断开连接
        Console.WriteLine($"[{label}] 断开连接 ...");
        await manager.DisconnectAsync();
        Console.WriteLine($"[{label}] 已断开。");
    }
}

// =====================================================================
//  复杂标签绘制：48×200mm 画布，覆盖 SDK 全部绘图能力。
//
//  布局（Y 坐标，单位 mm）：
//    0 ~  20   标题区：粗体大标题 + 副标题 + 分隔线
//   20 ~  45   信息区：日期/模式/尺寸 + 虚线分隔
//   45 ~  70   图形区①：填充矩形 + 空心矩形 + 圆角矩形
//   70 ~  95   图形区②：圆 + 椭圆 + 嵌套矩形
//   95 ~ 120   网格区：虚线网格 + 对角线
//  120 ~ 145   文本区①：多行文本 + 自动换行 + 反色文本
//  145 ~ 170   条码区：1D Code128 条码
//  170 ~ 195   二维码区：QR Code
//  210 ~ 215   底部脚注
// =====================================================================
static void DrawComplexLabel(PrinterCanvasMm canvas, string label)
{
    // ========== 1. 标题区 (Y: 0~20mm) ==========

    // 外边框（整个标签）
    canvas.DrawRect(new DrawOptions
    {
        X = 2, Y = 2,
        Width = 44, Height = 246,
        LineWidth = 0.5,
        Fill = false,
    });

    // 粗体大标题
    canvas.DrawText(new DrawOptions
    {
        Text = "DzPrinter SDK",
        X = 4,
        Y = 4,
        FontHeight = 6,
        FontStyle = FontStyle.Bold,
        TextAlignment = Alignment.Start,
    });

    // 副标题
    canvas.DrawText(new DrawOptions
    {
        Text = "Complex Label Test (48x200mm)",
        X = 4,
        Y = 11,
        FontHeight = 3,
        TextAlignment = Alignment.Start,
    });

    // 标题区分隔线（粗线）
    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 17,
        X2 = 44, Y2 = 17,
        LineWidth = 1,
    });

    // ========== 2. 信息区 (Y: 20~45mm) ==========

    canvas.DrawText(new DrawOptions
    {
        Text = $"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        X = 4,
        Y = 19,
        FontHeight = 3,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = $"Mode : {label} Transport",
        X = 4,
        Y = 23,
        FontHeight = 3,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Size : 48 x 200 mm @ 203 DPI",
        X = 4,
        Y = 27,
        FontHeight = 3,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = $"Page : 1/1  [{DateTime.Now:HHmmss}]",
        X = 4,
        Y = 31,
        FontHeight = 3,
    });

    // 虚线分隔
    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 36,
        X2 = 44, Y2 = 36,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    // ========== 3. 图形区① (Y: 37~70mm) — 矩形系列 ==========

    canvas.DrawText(new DrawOptions
    {
        Text = "--- Shapes: Rect ---",
        X = 4,
        Y = 38,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 填充矩形（实心黑块）
    canvas.DrawRect(new DrawOptions
    {
        X = 4,
        Y = 43,
        Width = 12,
        Height = 12,
        Fill = true,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Fill",
        X = 4,
        Y = 56,
        FontHeight = 2.5,
        TextAlignment = Alignment.Start,
    });

    // 空心矩形
    canvas.DrawRect(new DrawOptions
    {
        X = 18,
        Y = 43,
        Width = 12,
        Height = 12,
        LineWidth = 1,
        Fill = false,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Stroke",
        X = 18,
        Y = 56,
        FontHeight = 2.5,
        TextAlignment = Alignment.Start,
    });

    // 圆角矩形
    canvas.DrawRoundRect(new DrawOptions
    {
        X = 32,
        Y = 43,
        Width = 12,
        Height = 12,
        Radius = 2,
        LineWidth = 1,
        Fill = false,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Round",
        X = 32,
        Y = 56,
        FontHeight = 2.5,
        TextAlignment = Alignment.Start,
    });

    // ========== 4. 图形区② (Y: 62~95mm) — 圆形系列 ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 62,
        X2 = 44, Y2 = 62,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    canvas.DrawText(new DrawOptions
    {
        Text = "--- Shapes: Circle/Ellipse ---",
        X = 4,
        Y = 63,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 实心圆
    canvas.DrawCircle(new DrawOptions
    {
        X = 10,
        Y = 75,
        Radius = 5,
        Fill = true,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Circle",
        X = 5,
        Y = 82,
        FontHeight = 2.5,
    });

    // 空心圆
    canvas.DrawCircle(new DrawOptions
    {
        X = 24,
        Y = 75,
        Radius = 5,
        Fill = false,
        LineWidth = 1,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Ring",
        X = 20,
        Y = 82,
        FontHeight = 2.5,
    });

    // 椭圆
    canvas.DrawEllipse(new DrawOptions
    {
        X = 33,
        Y = 70,
        Width = 10,
        Height = 8,
        Fill = false,
        LineWidth = 1,
    });
    canvas.DrawText(new DrawOptions
    {
        Text = "Ellipse",
        X = 33,
        Y = 80,
        FontHeight = 2.5,
    });

    // ========== 5. 网格区 (Y: 88~120mm) ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 88,
        X2 = 44, Y2 = 88,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    canvas.DrawText(new DrawOptions
    {
        Text = "--- Grid Pattern ---",
        X = 4,
        Y = 89,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 网格：水平虚线（5 条，间距 5mm）
    for (var gy = 95; gy <= 115; gy += 5)
    {
        canvas.DrawLine(new DrawOptions
        {
            X1 = 6, Y1 = gy,
            X2 = 42, Y2 = gy,
            LineWidth = 0.3,
            DashLen = "1,1",
        });
    }

    // 网格：垂直虚线（8 条，间距 5mm）
    for (var gx = 6; gx <= 42; gx += 5)
    {
        canvas.DrawLine(new DrawOptions
        {
            X1 = gx, Y1 = 95,
            X2 = gx, Y2 = 115,
            LineWidth = 0.3,
            DashLen = "1,1",
        });
    }

    // 对角线（交叉）
    canvas.DrawLine(new DrawOptions
    {
        X1 = 6, Y1 = 95,
        X2 = 42, Y2 = 115,
        LineWidth = 0.5,
    });
    canvas.DrawLine(new DrawOptions
    {
        X1 = 42, Y1 = 95,
        X2 = 6, Y2 = 115,
        LineWidth = 0.5,
    });

    // ========== 6. 文本区① (Y: 118~150mm) ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 118,
        X2 = 44, Y2 = 118,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    canvas.DrawText(new DrawOptions
    {
        Text = "--- Text Styles ---",
        X = 4,
        Y = 119,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 粗体
    canvas.DrawText(new DrawOptions
    {
        Text = "Bold Text",
        X = 4,
        Y = 124,
        FontHeight = 4,
        FontStyle = FontStyle.Bold,
    });

    // 斜体
    canvas.DrawText(new DrawOptions
    {
        Text = "Italic Text",
        X = 4,
        Y = 129,
        FontHeight = 4,
        FontStyle = FontStyle.Italic,
    });

    // 下划线
    canvas.DrawText(new DrawOptions
    {
        Text = "Underline Text",
        X = 4,
        Y = 134,
        FontHeight = 4,
        FontStyle = FontStyle.Underline,
    });

    // 反色文本（白底黑字 → 黑底白字）
    canvas.DrawText(new DrawOptions
    {
        Text = "Inverse",
        X = 4,
        Y = 140,
        Width = 20,
        Height = 6,
        FontHeight = 4,
        AntiColor = true,
    });

    // 右侧：自动换行多行文本
    canvas.DrawText(new DrawOptions
    {
        Text = "Auto wrap text demo: this long string will wrap automatically within the given width.",
        X = 26,
        Y = 124,
        Width = 18,
        FontHeight = 2.5,
        AutoReturn = WrapMode.Char,
    });

    // ========== 7. 条码区 (Y: 148~175mm) ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 148,
        X2 = 44, Y2 = 148,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    canvas.DrawText(new DrawOptions
    {
        Text = "--- 1D Barcode (Code128) ---",
        X = 4,
        Y = 149,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 生成 Code128 条码
    var barcode1D = Barcode1DCreator.Create1DBarcode(new Barcode1DRequest
    {
        Text = "DZ20260817",
        BarcodeType = BarcodeType.CODE128,
    });

    if (barcode1D != null)
    {
        canvas.Draw1DBarcode(new DrawOptions
        {
            Datas = barcode1D.Items.Select(i => new BarcodeItem
            {
                Data = i.Data,
                Text = i.Text,
            }).ToList(),
            X = 4,
            Y = 155,
            Width = 40,
            Height = 14,
            TextHeight = 3,
            TextAlignment = Alignment.Center,
            FontHeight = 3,
        });
    }

    // ========== 8. 二维码区 (Y: 172~198mm) ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 172,
        X2 = 44, Y2 = 172,
        LineWidth = 0.5,
        DashLen = "2,1",
    });

    canvas.DrawText(new DrawOptions
    {
        Text = "--- 2D Barcode (QR) ---",
        X = 4,
        Y = 173,
        FontHeight = 2.5,
        FontStyle = FontStyle.Italic,
    });

    // 生成 QR 码
    var qrMatrix = Barcode2DCreator.CreateQRCode(new Barcode2DRequest
    {
        Text = "https://github.com/DzPrinter/DzPrinterCsharpSDK",
        BarcodeType = TwoDBarcodeKind.QRCode,
    });

    if (qrMatrix != null)
    {
        canvas.Draw2DBarcode(new DrawOptions
        {
            Data = qrMatrix,
            X = 14,
            Y = 180,
            Width = 20,
            ZoneSize = 2,
            BarPixels = 4,
            AutoScaleLevel = 2,
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.Center,
        });
    }

    // 右侧二维码说明
    canvas.DrawText(new DrawOptions
    {
        Text = "Scan",
        X = 36,
        Y = 185,
        FontHeight = 3,
    });

    // ========== 9. 底部脚注 (Y: 210~215mm) ==========

    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 210,
        X2 = 44, Y2 = 210,
        LineWidth = 0.5,
    });

    canvas.DrawText(new DrawOptions
    {
        Text = $"Generated by DzPrinter SDK  [{DateTime.Now:yyyy-MM-dd}]",
        X = 4,
        Y = 215,
        FontHeight = 2,
        TextAlignment = Alignment.Start,
    });
}

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

    // 不通过 PrintWithTransportAsync 的"发现设备→选择→连接"流程（File 是虚拟设备）
    // 直接走通用流程即可；传入 "File" label，通用方法会自动处理 LpaDeviceType.File。
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
        // 3. 创建画布并绘制（48×48mm 正方形布局）
        Console.WriteLine($"[{label}] 创建画布 48×48mm ...");
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 48,
            HeightMm = 48,
            Orientation = 0,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
                PageCount = 1,
            },
        });
        ctx.Start();

        // ---- 标题 ----
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "DZPrinter SDK",
            X = 3,
            Y = 3,
            FontHeight = 5,
            TextAlignment = Alignment.Start,
            FontStyle = FontStyle.Bold,
        });

        // ---- 分隔线（纯线） ----
        ctx.Canvas.DrawLine(new DrawOptions
        {
            X1 = 3,
            Y1 = 9,
            X2 = 45, // 宽度 48 - 边距 3 = 45
            Y2 = 9,
            LineWidth = 1,
        });

        // ---- 文本信息 ----
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            X = 3,
            Y = 11,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Mode : {label} (FileTransport)",
            X = 3,
            Y = 14,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "Output: 48 x 48 mm",
            X = 3,
            Y = 17,
            FontHeight = 3,
        });

        // ---- 填充矩形（实心） ----
        ctx.Canvas.DrawRect(new DrawOptions
        {
            X = 3,
            Y = 22,
            Width = 12,
            Height = 12,
            LineWidth = 1,
            Fill = true,
        });

        // ---- 空心矩形 ----
        ctx.Canvas.DrawRect(new DrawOptions
        {
            X = 18,
            Y = 22,
            Width = 12,
            Height = 12,
            LineWidth = 1,
            Fill = false,
        });

        // ---- 矩形右侧的二维码占位（用多行小点模拟，便于看位置） ----
        ctx.Canvas.DrawRect(new DrawOptions
        {
            X = 34,
            Y = 22,
            Width = 11,
            Height = 11,
            Fill = true,
        });
        ctx.Canvas.DrawRect(new DrawOptions
        {
            X = 34,
            Y = 34,
            Width = 11,
            Height = 4,
            Fill = false,
            LineWidth = 1,
        });

        // ---- 底部脚注 ----
        ctx.Canvas.DrawLine(new DrawOptions
        {
            X1 = 3,
            Y1 = 43,
            X2 = 45,
            Y2 = 43,
            LineWidth = 1,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "Virtual File Print Preview",
            X = 3,
            Y = 44,
            FontHeight = 2.5,
            TextAlignment = Alignment.Start,
        });

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
        Console.WriteLine($"[{label}] 创建画布 40×30mm ...");
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 40,
            HeightMm = 30,
            Orientation = 0,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
                PageCount = 1,
            },
        });
        ctx.Start();

        // 绘制标题
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "DzPrinter SDK",
            X = 5,
            Y = 3,
            FontHeight = 4,
            TextAlignment = Alignment.Start,
        });

        // 绘制分隔线
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "--------------------",
            X = 5,
            Y = 5,
            FontHeight = 3,
        });

        // 绘制内容
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Date: {DateTime.Now:yyyy-MM-dd}",
            X = 5,
            Y = 7,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Transport: {label}",
            X = 5,
            Y = 10,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "Hello World!",
            X = 5,
            Y = 13,
            FontHeight = 4,
            TextAlignment = Alignment.Start,
        });

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

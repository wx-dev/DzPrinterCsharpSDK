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
//    dotnet run --project DzPrinter.Samples -- info-ble             # 查看设备信息
//    dotnet run --project DzPrinter.Samples -- info-ble --print     # 查看信息并打印到标签
//    dotnet run --project DzPrinter.Samples -- info-hid --print     # 同上 (HID)
// =====================================================================

using DzPrinter.Barcode;
using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport;
using DzPrinter.Transport.Ble;
using DzPrinter.Transport.File;
using DzPrinter.Transport.Hid;
using SkiaSharp;

// 注册 GBK 编码（打印机中文需要）
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "info-ble";

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
    case "info-ble":
        await RunInfoSampleAsync("ble", "BLE", args.Contains("--print"));
        break;
    case "info-hid":
        await RunInfoSampleAsync("hid", "HID", args.Contains("--print"));
        break;
    case "list":
        await ListDevicesAsync();
        break;
    default:
        Console.WriteLine("用法: dotnet run -- [ble|hid|file|file-hex|list|info-ble|info-hid]");
        break;
}

// =====================================================================
//  打印机信息展示示例（可选打印到标签）
// =====================================================================
static async Task RunInfoSampleAsync(string transportMode, string label, bool printLabel)
{
    IDeviceTransport transport = transportMode switch
    {
        "ble" => new WinRtBleTransport(new BleTransportOptions
        {
            ServiceUuid = new Guid("000018F0-0000-1000-8000-00805F9B34FB"),
            PackSize = 20,
            ScanTimeoutMs = 5000,
        }),
        "hid" => new HidSharpTransport(new HidTransportOptions
        {
            NameContains = "Printer",
            ReportId = 0,
        }),
        _ => throw new ArgumentException($"Unknown transport: {transportMode}"),
    };

    {
        using var manager = new DzPrinterManager(transport);

        // 1. 发现设备
        Console.WriteLine($"[{label}] 正在扫描设备 ...");
        IReadOnlyList<PrinterDevice> devices;
        try
        {
            devices = await manager.DiscoverAsync(
                label == "BLE" ? LpaDeviceType.Ble : LpaDeviceType.UsbHid);
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
            Console.WriteLine($"  {i}: {devices[i].Name}  (ID: {devices[i].DeviceId})");

        var device = devices[0];
        Console.WriteLine($"[{label}] 选中: {device.Name}");

        // 2. 连接
        Console.WriteLine($"[{label}] 正在连接 ...");
        try
        {
            var connectResult = await manager.ConnectAsync(device);
            if (connectResult != LpaResult.Ok || !manager.IsConnected)
            {
                Console.WriteLine($"[{label}] 连接失败: {connectResult}");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{label}] 连接异常: {ex.Message}");
            return;
        }
        Console.WriteLine($"[{label}] 连接成功！\n");

        // 3. 查询信息
        var hwInfo = await manager.Api.GetPrinterInfoAsync();
        var status = await manager.Api.GetPrintableStatusAsync();

        // 4. 美观输出
        PrintInfoCard(label, device, hwInfo, status);

        // 5. 可选：将信息打印到标签
        if (printLabel && hwInfo != null)
        {
            Console.WriteLine($"\n[{label}] 正在打印信息标签 ...");
            try
            {
                using var ctx = manager.CreateDrawContext(new DrawJobOptions
                {
                    WidthMm = 48,
                    HeightMm = 70,
                    Orientation = 0,
                    PrinterInfo = new PrinterInfo
                    {
                        PrinterDpi = hwInfo.Dpi > 0 ? hwInfo.Dpi : 203,
                        PrinterWidth = hwInfo.PrinterWidth > 0 ? hwInfo.PrinterWidth : 384,
                        PageCount = 1,
                    },
                });
                ctx.Start();
                DrawInfoLabel(ctx.Canvas, label, device, hwInfo, status);
                var result = await manager.PrintAsync(ctx);
                Console.WriteLine($"[{label}] 打印结果: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{label}] 打印异常: {ex.Message}");
            }
        }

        // 6. 断开
        Console.WriteLine($"\n[{label}] 断开连接 ...");
        await manager.DisconnectAsync();
        Console.WriteLine($"[{label}] 已断开。");
    }
}

static void PrintInfoCard(string label, PrinterDevice device,
    PrinterHardwareInfo? hw, PrinterStatusCode status)
{
    const int W = 50;
    var bar     = new string('═', W);
    var dashBar = new string('─', W);

    static int DispWidth(string s)
    {
        int w = 0;
        foreach (var c in s)
        {
            // CJK + 全角 + emoji = 2 列
            if (c >= 0x2E80 && c <= 0x303E ||
                c >= 0x3040 && c <= 0x33BF ||
                c >= 0x3400 && c <= 0x4DBF ||
                c >= 0x4E00 && c <= 0xA4CF ||
                c >= 0xAC00 && c <= 0xD7A3 ||
                c >= 0xF900 && c <= 0xFAFF ||
                c >= 0xFE30 && c <= 0xFE4F ||
                c >= 0xFF00 && c <= 0xFF60 ||
                c >= 0xFFE0 && c <= 0xFFE6 ||
                c >= 0x1F300 && c <= 0x1FAFF)
                w += 2;
            else
                w += 1;
        }
        return w;
    }

    void Line(string content)
    {
        int pad = W - 2 - DispWidth(content);
        Console.WriteLine($"│  {content}{new string(' ', Math.Max(0, pad))}│");
    }

    void Sep() => Console.WriteLine($"├{dashBar}┤");

    Console.WriteLine($"┌{bar}┐");
    Line("DzPrinter 设备信息");
    Console.WriteLine($"├{bar}┤");

    Line($"▸ 设备名称      {device.Name}");
    Line($"▸ 设备 ID       {device.DeviceId}");
    Line($"▸ 传输方式      {label}");
    Sep();

    if (hw == null)
    {
        Line("⚠ 无法获取硬件信息");
    }
    else
    {
        Line($"▸ DPI 分辨率    {hw.Dpi}");
        Line($"▸ 打印宽度      {hw.PrinterWidth} px");
        var paperWidthMm = hw.Dpi > 0
            ? $"{hw.PrinterWidth / (hw.Dpi / 25.4):F1} mm"
            : "—";
        Line($"▸ 纸张宽度      {paperWidthMm}");
        Line($"▸ 缓冲区大小    {hw.BufferSize / 1024} KB ({hw.BufferSize} bytes)");
        Sep();

        var chargeStr = hw.ChargeStatus ? "⚡ 充电中" : "🔋 电池供电";
        var voltageStr = hw.BatteryVoltage > 0 ? $"{hw.BatteryVoltage:F2} V" : "—";
        var batteryBar = hw.BatteryVoltage switch
        {
            > 4.0  => "██████████",
            > 3.8  => "████████░░",
            > 3.6  => "██████░░░░",
            > 3.4  => "████░░░░░░",
            > 0.1  => "██░░░░░░░░",
            _       => "░░░░░░░░░░",
        };
        Line($"▸ 电池数量      {hw.BatteryCount}");
        Line($"▸ 电池电压      {voltageStr}  [{batteryBar}]");
        Line($"▸ 充电状态      {chargeStr}");
        Sep();

        Line($"▸ 硬件标志      0x{(uint)hw.HardwareFlags:X8}");
        Line($"▸ 软件标志      0x{(uint)hw.SoftwareFlags:X8}");
    }

    Sep();
    var statusStr = status switch
    {
        PrinterStatusCode.DZIP_PRINTABLE   => "✓ 可打印",
        PrinterStatusCode.DZIP_ISPRINTING  => "⋯ 打印中",
        PrinterStatusCode.DZIP_ISROTATING  => "⋯ 进纸中",
        PrinterStatusCode.DZIP_VOLTOOLOW   => "⚠ 电量过低",
        PrinterStatusCode.DZIP_VOLTOOHIGH  => "⚠ 电量过高",
        PrinterStatusCode.DZIP_TPHTOOHOT   => "⚠ 温度过高",
        PrinterStatusCode.DZIP_COVEROPENED => "⚠ 盖板打开",
        PrinterStatusCode.DZIP_NO_PAPER    => "⚠ 缺纸",
        _                                  => status.ToString(),
    };
    Line($"▸ 打印机状态    {statusStr}");
    Console.WriteLine($"└{bar}┘");
}

// =====================================================================
//  信息标签绘制：将打印机信息排版到 48×65mm 标签上
// =====================================================================
static void DrawInfoLabel(PrinterCanvasMm canvas, string label,
    PrinterDevice device, PrinterHardwareInfo hw, PrinterStatusCode status)
{
    // 外边框
    canvas.DrawRect(new DrawOptions
    {
        X = 2, Y = 2, Width = 44, Height = 60, LineWidth = 0.5, Fill = false,
    });

    // 标题
    canvas.DrawText(new DrawOptions
    {
        Text = "Printer Info",
        X = 4, Y = 4, FontHeight = 5, FontStyle = FontStyle.Bold,
    });

    // 标题分隔线
    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = 11, X2 = 44, Y2 = 11, LineWidth = 0.5,
    });

    // 内容区
    var y = 13;
    var lineH = 4;

    void Row(string key, string val)
    {
        canvas.DrawText(new DrawOptions
        {
            Text = key, X = 4, Y = y, FontHeight = 3,
            FontStyle = FontStyle.Bold,
        });
        canvas.DrawText(new DrawOptions
        {
            Text = val, X = 22, Y = y, FontHeight = 3,
        });
        y += lineH;
    }

    Row("Device:", device.Name);
    Row("Transport:", label);
    Row("DPI:", hw.Dpi.ToString());
    Row("Width:", $"{hw.PrinterWidth} px");
    Row("Buffer:", $"{hw.BufferSize / 1024} KB");

    // 电池区分隔线
    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = y + 1, X2 = 44, Y2 = y + 1, LineWidth = 0.5,
        DashLen = "2,1",
    });
    y += 3;

    Row("Battery:", $"{hw.BatteryCount} cell(s)");
    Row("Voltage:", $"{hw.BatteryVoltage:F2} V");
    Row("Charging:", hw.ChargeStatus ? "Yes" : "No");

    // 状态区分隔线
    canvas.DrawLine(new DrawOptions
    {
        X1 = 4, Y1 = y + 1, X2 = 44, Y2 = y + 1, LineWidth = 0.5,
        DashLen = "2,1",
    });
    y += 3;

    var statusStr = status switch
    {
        PrinterStatusCode.DZIP_PRINTABLE   => "Ready",
        PrinterStatusCode.DZIP_ISPRINTING  => "Printing...",
        PrinterStatusCode.DZIP_VOLTOOLOW   => "Low Battery!",
        PrinterStatusCode.DZIP_VOLTOOHIGH  => "High Voltage!",
        PrinterStatusCode.DZIP_TPHTOOHOT   => "Overheating!",
        PrinterStatusCode.DZIP_COVEROPENED => "Cover Open!",
        PrinterStatusCode.DZIP_NO_PAPER    => "No Paper!",
        _                                  => status.ToString(),
    };
    Row("Status:", statusStr);

    // 底部时间戳
    canvas.DrawText(new DrawOptions
    {
        Text = $"DzPrinter SDK  [{DateTime.Now:yyyy-MM-dd HH:mm}]",
        X = 4, Y = 58, FontHeight = 2.5,
    });
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
        Console.WriteLine($"[{label}] 创建画布 48mm×220mm ...");
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

    // ===== 新增：获取设备信息 =====
    var hwInfo = await manager.Api.GetPrinterInfoAsync();
    if (hwInfo != null)
    {
        Console.WriteLine($"[{label}] 硬件标志: {hwInfo.HardwareFlags}");
        Console.WriteLine($"[{label}] 软件标志: {hwInfo.SoftwareFlags}");
        Console.WriteLine($"[{label}] 缓冲区: {hwInfo.BufferSize} bytes");
        Console.WriteLine($"[{label}] DPI: {hwInfo.Dpi}");
        Console.WriteLine($"[{label}] 打印宽度: {hwInfo.PrinterWidth} px");
        Console.WriteLine($"[{label}] 电池数量: {hwInfo.BatteryCount}");
        Console.WriteLine($"[{label}] 电池电压: {hwInfo.BatteryVoltage:F2}V");
        Console.WriteLine($"[{label}] 充电状态: {(hwInfo.ChargeStatus ? "充电中" : "未充电")}");
    }

    var status = await manager.Api.GetPrintableStatusAsync();
    Console.WriteLine($"[{label}] 打印机状态: {status}");
    if (status == PrinterStatusCode.DZIP_VOLTOOLOW)
        Console.WriteLine($"[{label}] ⚠ 电量过低！");
    else if (status == PrinterStatusCode.DZIP_VOLTOOHIGH)
        Console.WriteLine($"[{label}] ⚠ 电量过高！");
    // ==============================

    try
    {
        // 4. 创建画布并绘制内容
        Console.WriteLine($"[{label}] 创建画布 48mm×220mm ...");
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
//  复杂标签绘制：48×220mm 画布，覆盖 SDK 全部绘图能力。
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
        Width = 44, Height = 216,
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
        Text = "Complex Label Test (48x220mm)",
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
        FontHeight = 3,
        FontStyle = FontStyle.Bold,
    });

    // 斜体
    canvas.DrawText(new DrawOptions
    {
        Text = "Italic Text",
        X = 4,
        Y = 129,
        FontHeight = 3,
        FontStyle = FontStyle.Italic,
    });

    // 下划线
    canvas.DrawText(new DrawOptions
    {
        Text = "Underline Text",
        X = 4,
        Y = 134,
        FontHeight = 3,
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
        Text = "https://github.com/wx-dev/DzPrinterCsharpSDK",
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

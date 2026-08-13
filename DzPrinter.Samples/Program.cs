// =====================================================================
//  DzPrinter Windows 示例：BLE + HID 打印。
//
//  本示例演示如何通过 DzPrinterManager + 传输层实现完整的
//  "发现设备 → 连接 → 绘制 → 打印 → 断开" 流程。
//
//  用法：
//    dotnet run --project DzPrinter.Samples -- ble     # BLE 打印
//    dotnet run --project DzPrinter.Samples -- hid     # HID 打印
//    dotnet run --project DzPrinter.Samples -- list    # 仅列出设备
// =====================================================================

using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport;
using DzPrinter.Transport.Ble;
using DzPrinter.Transport.Hid;

// 注册 GBK 编码（打印机中文需要）
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

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
    case "list":
        await ListDevicesAsync();
        break;
    default:
        Console.WriteLine("用法: dotnet run -- [ble|hid|list]");
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
        devices = await manager.DiscoverAsync();
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
        Console.WriteLine($"[{label}] 创建画布 60×40mm ...");
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 60,
            HeightMm = 40,
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
            Y = 10,
            FontHeight = 3,
        });

        // 绘制内容
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Date: {DateTime.Now:yyyy-MM-dd}",
            X = 5,
            Y = 16,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = $"Transport: {label}",
            X = 5,
            Y = 22,
            FontHeight = 3,
        });
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "Hello World!",
            X = 5,
            Y = 28,
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

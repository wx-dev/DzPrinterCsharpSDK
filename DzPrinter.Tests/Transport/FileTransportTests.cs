// =====================================================================
//  FileTransport 单元测试。
//  验证：
//    1. 连接生命周期：Discover → Connect → Send → Disconnect
//    2. RawBinary 格式：写入的字节与发送的一致
//    3. HexText 格式：每行带时间戳和十六进制字符串
//    4. 追加写入（Append=true）
//    5. DiscoverAsync 返回的虚拟设备匹配 SupportPrinterMatcher
//    6. 完整打印流程：通过 LPAPI/DzPrinterManager 将 DrawContext 内容编码后写入文件
// =====================================================================

using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Transport;
using DzPrinter.Transport.File;
using Xunit.Abstractions;

namespace DzPrinter.Tests.Transport;

public class FileTransportTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _testDir;

    public FileTransportTests(ITestOutputHelper @out)
    {
        _out = @out;
        _testDir = Path.Combine(Path.GetTempPath(), "DzPrinterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { /* 忽略 */ }
    }

    private string MkPath(string name) => Path.Combine(_testDir, name);

    // ============ 1. 发现设备 ============

    [Fact]
    public async Task DiscoverAsync_ReturnsVirtualDevice_WithSupportedPrefix()
    {
        using var transport = new FileTransport();
        var devices = await transport.DiscoverAsync();

        devices.Should().HaveCountGreaterOrEqualTo(1);
        var dev = devices[0];
        dev.DeviceId.Should().Be("virtual-file-printer");
        // 设备名必须以 D60 开头才能通过 SupportPrinterMatcher
        SupportPrinterMatcher.IsSupported(dev.DeviceName).Should().BeTrue(
            $"虚拟设备名 '{dev.DeviceName}' 应匹配 SupportPrinterMatcher");
    }

    // ============ 2. 连接/断开 + RawBinary 写入 ============

    [Fact]
    public async Task Connect_SendRawBinary_Disconnect_WritesFile()
    {
        var path = MkPath("raw_print.bin");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.RawBinary,
        });

        var devices = await transport.DiscoverAsync();
        var dev = devices[0];

        await transport.ConnectAsync(dev);
        transport.State.Should().Be(ConnectionState.Connected);

        var data = new byte[] { 0x1F, 0x20, 0x02, 0x00, 0x00, 0x88 }; // 示例协议帧
        await transport.SendAsync(data);
        await transport.SendAsync(new byte[] { 0x0C }); // 页结束符
        await transport.DisconnectAsync();

        transport.State.Should().Be(ConnectionState.Disconnected);
        File.Exists(path).Should().BeTrue();
        var written = File.ReadAllBytes(path);
        written.Should().BeEquivalentTo(new byte[] { 0x1F, 0x20, 0x02, 0x00, 0x00, 0x88, 0x0C });
        _out.WriteLine($"RawBinary 写入 {written.Length} 字节 → {path}");
    }

    // ============ 3. HexText 格式 ============

    [Fact]
    public async Task SendAsync_HexText_WritesTimestampedLines()
    {
        var path = MkPath("hex_print.txt");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.HexText,
        });

        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);
        await transport.SendAsync(new byte[] { 0x1F, 0x2B, 0x00, 0x28, 0x00 });
        await transport.SendAsync(new byte[] { 0x0C });
        await transport.DisconnectAsync();

        var text = File.ReadAllText(path);
        _out.WriteLine(text);
        // 文件头
        text.Should().Contain("FileTransport capture start");
        text.Should().Contain("Format: HexText");
        // 时间戳行格式：[HH:mm:ss.fff] 5 bytes: 1F 2B 00 28 00
        text.Should().MatchRegex(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\] 5 bytes: 1F 2B 00 28 00");
        text.Should().MatchRegex(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\] 1 bytes: 0C");
        // 文件尾
        text.Should().Contain("FileTransport capture end");
    }

    // ============ 4. 追加写入 ============

    [Fact]
    public async Task SendAsync_AppendTrue_ConcatenatesSessions()
    {
        var path = MkPath("append.bin");
        var data1 = new byte[] { 0x11, 0x22 };
        var data2 = new byte[] { 0x33, 0x44 };

        // 第一次连接写入
        using (var t1 = new FileTransport(new FileTransportOptions
               { OutputPath = path, Format = FileOutputFormat.RawBinary, Append = false }))
        {
            var ds = await t1.DiscoverAsync();
            await t1.ConnectAsync(ds[0]);
            await t1.SendAsync(data1);
            await t1.DisconnectAsync();
        }
        File.ReadAllBytes(path).Should().BeEquivalentTo(new byte[] { 0x11, 0x22 });

        // 第二次追加写入
        using (var t2 = new FileTransport(new FileTransportOptions
               { OutputPath = path, Format = FileOutputFormat.RawBinary, Append = true }))
        {
            var ds = await t2.DiscoverAsync();
            await t2.ConnectAsync(ds[0]);
            await t2.SendAsync(data2);
            await t2.DisconnectAsync();
        }
        File.ReadAllBytes(path).Should().BeEquivalentTo(new byte[] { 0x11, 0x22, 0x33, 0x44 });
    }

    // ============ 5. 未连接时 Send 抛错 ============

    [Fact]
    public async Task SendAsync_BeforeConnect_Throws()
    {
        var path = MkPath("before_connect.bin");
        using var transport = new FileTransport(new FileTransportOptions { OutputPath = path });
        var act = () => transport.SendAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*未连接*");
    }

    // ============ 6. 完整流程：DrawContext → EncodeChunks → FileTransport ============

    [Fact]
    public async Task FullPrintPipeline_WritesEncodedDataToFile()
    {
        var path = MkPath("full_pipeline.bin");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.RawBinary,
        });

        // 连接虚拟文件设备
        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);

        // 通过 DeviceConnection (FileConnection) 包装传输层
        using var conn = new FileConnection(transport);
        conn.IsConnected.Should().BeTrue("ConnectAsync 后设备连接应已就绪");

        // 创建画布并绘制内容
        var opts = new DrawJobOptions
        {
            WidthMm = 40, HeightMm = 40, Orientation = 0,
            PrinterInfo = new PrinterInfo { PrinterDpi = 203, PrinterWidth = 384, PageCount = 1 },
        };
        using var ctx = new DrawContext(opts);
        ctx.Start();
        ctx.Canvas.DrawRect(new DrawOptions { X = 5, Y = 5, Width = 20, Height = 20, Fill = true });
        var chunks = ctx.EncodeChunks();
        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(0);

        // 逐个分片发送（模拟 LPAPI.SendChunksAsync）
        long sentBytes = 0;
        foreach (var chunk in chunks)
        {
            await transport.SendAsync(chunk);
            sentBytes += chunk.Length;
        }
        await transport.DisconnectAsync();

        // 验证文件内容
        File.Exists(path).Should().BeTrue();
        var fileBytes = File.ReadAllBytes(path);
        fileBytes.Length.Should().Be((int)sentBytes,
            "文件中的总字节数应等于所有分片之和");
        fileBytes[0].Should().Be(0x1F, "协议帧应以 0x1F 开头");
        fileBytes[^1].Should().Be(0x0C, "最后一帧应为页结束符 0x0C");

        // 验证位图命令存在（填充矩形 → PRINT 或 RLE 或 REPEAT）
        var foundBitmap = false;
        var bitmapCmds = new HashSet<byte> { 41, 43, 44, 45, 46, 60, 61 };
        for (var i = 0; i < fileBytes.Length - 1; i++)
        {
            if (fileBytes[i] == 0x1F && bitmapCmds.Contains(fileBytes[i + 1]))
            {
                foundBitmap = true;
                break;
            }
        }
        foundBitmap.Should().BeTrue("文件中应包含位图打印命令（PRINT/RLE/REPEAT 之一）");

        _out.WriteLine($"完整打印流程：{chunks.Count} 分片，共 {sentBytes} 字节 → {path}");
        _out.WriteLine($"前 32 字节十六进制：{Convert.ToHexString(fileBytes.Take(32).ToArray())}");
    }

    // ============ 7. RequestAsync 返回模拟响应 ============

    [Fact]
    public async Task RequestAsync_SendsAndReturnsMockResponse()
    {
        var path = MkPath("request.bin");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.RawBinary,
        });
        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);

        byte[]? captured = null;
        transport.DataReceived += (_, e) => captured = e.Data;

        // 发送握手帧（以 0x1F 开头的命令帧）
        var response = await transport.RequestAsync(new byte[] { 0x1F, 0x20, 0x02, 0x00, 0x00, 0x88 });

        response.Should().NotBeNull("以 0x1F 开头的请求应返回模拟响应");
        response![0].Should().Be(0x1F, "响应帧应以 0x1F 开头");
        captured.Should().NotBeNull("应触发 DataReceived 事件");
        captured!.Should().BeEquivalentTo(response);
    }

    // ============ 8. 实际路径可用，文件路径支持目录自动创建 ============

    [Fact]
    public async Task ConnectAsync_CreatesNestedDirectory()
    {
        var path = MkPath(Path.Combine("sub1", "sub2", "deep.bin"));
        using var transport = new FileTransport(new FileTransportOptions { OutputPath = path });
        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);
        await transport.SendAsync(new byte[] { 0xAA, 0xBB });
        await transport.DisconnectAsync();

        File.Exists(path).Should().BeTrue();
        transport.ActualPath.Should().Be(Path.GetFullPath(path));
    }

    // ============ 9. SavePngPreview 自动生成 PNG ============

    [Fact]
    public async Task DisconnectAsync_WithPngPreview_GeneratesValidPngFile()
    {
        var path = MkPath("png_preview.bin");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.RawBinary,
            SavePngPreview = true,
            PngScale = 1,  // 1:1 便于像素级验证
            PngBackground = 1,
        });

        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);

        // 发送真实的打印编码流：DrawContext → 编码 → 传输
        var opts = new DrawJobOptions
        {
            WidthMm = 40, HeightMm = 40, Orientation = 0,
            PrinterInfo = new PrinterInfo { PrinterDpi = 203, PrinterWidth = 384, PageCount = 1 },
        };
        using var ctx = new DrawContext(opts);
        ctx.Start();
        ctx.Canvas.DrawRect(new DrawOptions { X = 5, Y = 5, Width = 20, Height = 20, Fill = true });
        var chunks = ctx.EncodeChunks();
        foreach (var chunk in chunks) await transport.SendAsync(chunk);
        await transport.DisconnectAsync();

        // 验证 PNG 文件已创建
        var expectedPng = Path.ChangeExtension(path, ".png");
        File.Exists(expectedPng).Should().BeTrue("SavePngPreview=true 时应生成同名 .png 文件");
        transport.ActualPngPath.Should().Be(expectedPng);

        // 验证 PNG 是有效图像文件（非 0 字节，头部为 0x89 'P' 'N' 'G'）
        using var fs = File.OpenRead(expectedPng);
        fs.Length.Should().BeGreaterThan(8, "PNG 文件长度应 > 8 字节");
        var header = new byte[8];
        fs.Read(header, 0, 8);
        header[0].Should().Be(0x89);
        header[1].Should().Be(0x50); // 'P'
        header[2].Should().Be(0x4E); // 'N'
        header[3].Should().Be(0x47); // 'G'

        // 用 SkiaSharp 验证 PNG 解码成功且尺寸正确 + 含黑色像素
        var blackPixels = 0;
        SKBitmap? decoded = null;
        try
        {
            decoded = SKBitmap.Decode(expectedPng);
            decoded.Should().NotBeNull("PNG 应能成功解码");
            // 40mm 画布@203DPI → byteWidth=40 → 320px；scale=2 → 640px；打印机物理宽 384 → 384/768px
            decoded!.Width.Should().BeOneOf(new[] { 320, 640, 384, 768 },
                "PNG 像素宽度：320/640=画布实际(40byte*8×scale) 或 384/768=打印机物理宽×scale");
            decoded.Height.Should().BeGreaterThanOrEqualTo(320, "PNG 高度应至少为 320 行（320px ≈ 40mm）");

            // GetPixelSpan 返回的是只读 Span<byte>；通过 GetPixels 得到 IntPtr，用 Marshal 拷贝
            var w = decoded.Width;
            var h = decoded.Height;
            var rb = decoded.RowBytes;
            // 改为逐像素调用 GetPixel()，性能 OK 因为只用于测试
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = decoded.GetPixel(x, y);
                if (c.Red < 50) blackPixels++; // Gray8 → R=G=B
            }

            // 20mm×20mm 填充矩形：20mm@8pmm = 160×160 = 25600 px @ scale=1
            // scale=2 → 25600 × 4 = 102400 px。实际 25281 或 102xxx 均合理。
            var minExpected = w >= 600 ? 80000 : 20000;
            blackPixels.Should().BeGreaterThan(minExpected,
                $"矩形填充：w={w} scale>=2 期望 ≥8 万黑点, scale=1 期望 ≥2 万黑点");

            _out.WriteLine($"PNG: {w}x{h}, blackPixels={blackPixels}, file={expectedPng}");
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    // ============ 10. PrintPreviewDecoder 单元：直接解码字节流 → DzImageData 像素级匹配 ============

    [Fact]
    public void Decoder_DrawContextPngPreview_MatchesCanvasPixelData()
    {
        // 用 DrawContext 绘制 + 编码
        var opts = new DrawJobOptions
        {
            WidthMm = 40, HeightMm = 40, Orientation = 0,
            PrinterInfo = new PrinterInfo { PrinterDpi = 203, PrinterWidth = 384, PageCount = 1 },
        };
        using var ctx = new DrawContext(opts);
        ctx.Start();
        ctx.Canvas.DrawRect(new DrawOptions { X = 5, Y = 5, Width = 20, Height = 20, Fill = true });
        var original = ctx.Canvas.GetImageData();
        var chunks = ctx.EncodeChunks();

        // 拼接所有编码帧字节
        using var ms = new MemoryStream();
        foreach (var c in chunks) ms.Write(c, 0, c.Length);
        var bytes = ms.ToArray();

        // 解码 → DzImageData
        var result = PrintPreviewDecoder.Decode(bytes);
        result.Success.Should().BeTrue("解码器应成功解码");
        result.ByteWidth.Should().Be(original.Width / 8, "字节宽度应等于源图像宽度/8");

        var decoded = PrintPreviewDecoder.ToDzImageData(result, background: 1);
        decoded.Width.Should().Be(original.Width, "解码宽度应等于原画布宽度");
        decoded.Height.Should().BeGreaterThanOrEqualTo(original.Height,
            "解码高度应至少等于原画布高度（包含走纸空行）");

        // 像素级匹配：允许走纸导致的底部额外空白行
        var matchingPixels = 0;
        var mismatched = 0;
        var rowsToCompare = Math.Min(decoded.Height, original.Height);
        for (var y = 0; y < rowsToCompare; y++)
        for (var x = 0; x < original.Width; x++)
        {
            var i1 = 4 * (y * original.Width + x);
            var i2 = 4 * (y * decoded.Width + x);
            // 只比较 R 通道（灰度图，R=G=B）
            var origBlack = original.Data[i1] < 50;
            var decBlack = decoded.Data[i2] < 50;
            if (origBlack == decBlack) matchingPixels++;
            else mismatched++;
        }

        _out.WriteLine($"匹配像素: {matchingPixels}, 不匹配: {mismatched}, 总: {original.Width * rowsToCompare}");
        // 允许小于 1% 的位翻转（边缘走纸差异）
        mismatched.Should().BeLessThan((int)(0.02 * original.Width * rowsToCompare),
            "解码后的像素应与原画布的像素基本一致（允许 2% 误差）");
    }

    // ============ 11. HexText 格式同时支持 PNG 预览（内部捕获原始字节）============

    [Fact]
    public async Task HexTextFormat_SavePngPreview_StillGeneratesValidPng()
    {
        var path = MkPath("hex_and_png.txt");
        using var transport = new FileTransport(new FileTransportOptions
        {
            OutputPath = path,
            Format = FileOutputFormat.HexText,
            SavePngPreview = true,
            PngScale = 1,
        });

        var devices = await transport.DiscoverAsync();
        await transport.ConnectAsync(devices[0]);

        var opts = new DrawJobOptions
        {
            WidthMm = 40, HeightMm = 40, Orientation = 0,
            PrinterInfo = new PrinterInfo { PrinterDpi = 203, PrinterWidth = 384, PageCount = 1 },
        };
        using var ctx = new DrawContext(opts);
        ctx.Start();
        ctx.Canvas.DrawText(new DrawOptions { X = 2, Y = 2, Text = "Hello" });
        var chunks = ctx.EncodeChunks();
        foreach (var chunk in chunks) await transport.SendAsync(chunk);
        await transport.DisconnectAsync();

        // .txt 文件为 HexText 内容
        File.ReadAllText(path).Should().Contain("bytes:");
        // .png 仍然创建（从内部原始字节捕获）
        var expectedPng = Path.Combine(Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + ".png");
        File.Exists(expectedPng).Should().BeTrue("HexText 模式下 PNG 预览也应生成");

        using var bmp = SKBitmap.Decode(expectedPng);
        bmp.Should().NotBeNull();
        // 画布 40mm@203DPI → 40*203/25.4 ≈ 320 像素 = 40 字节宽
        // 但 PAGE_WIDTH 记录的是 byteWidth，所以像素宽度 = byteWidth*8，320 或 384 都合理
        bmp!.Width.Should().BeOneOf(new[] { 320, 384 }, "PNG 宽度应为 320px(40mm画布) 或 384px(打印机宽度)");
        _out.WriteLine($"HexText+PNG: {bmp.Width}x{bmp.Height} → {expectedPng}");
    }
}

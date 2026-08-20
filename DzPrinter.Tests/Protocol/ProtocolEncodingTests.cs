// =====================================================================
//  协议编码断言测试。
//
//  显式字节断言验证 PrintEncoder.EncodeImageData 产生的分片结构：
//    1. LPAPI 路径：SentFrames[0] 以 HostToDeviceDataStart + CMD_PAGE_START 开头
//    2. DrawContext 路径：chunks[0] 同样以帧头开头
//    3. 完整协议帧布局：[0x1F][CMD][LEN][...DATA...][0x88]
//    4. 末尾分片以 PageEndBytes(0x0C) 结束
//    5. UpdatePageKey 能正确替换 pageKey 字节
// =====================================================================

using DzPrinter.Imaging;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Tests.Infrastructure;

namespace DzPrinter.Tests.Printer.Protocol;

#region 1. LPAPI 路径协议帧头断言
public class LpApiProtocolHeaderTests
{
    private static LPAPI CreateLpApi(out MockTransport transport)
    {
        transport = new MockTransport();
        var captured = transport;
        return new LPAPI(_ => captured, new PrinterInfo
        {
            PrinterWidth = 384,
            PrinterDpi = 203,
            PageCount = 1,
        });
    }

    [Fact]
    public async Task SentFrames_FirstFrame_StartsWithHostToDeviceDataStart()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        transport.SentFrames.Should().NotBeEmpty();
        var first = transport.SentFrames[0];
        first[0].Should().Be(ProtocolConstants.HostToDeviceDataStart, // 0x1F
            "首帧应以 HostToDeviceDataStart(0x1F) 开头");
    }

    [Fact]
    public async Task SentFrames_FirstFrame_SecondByteIsCmdPageStart()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        var first = transport.SentFrames[0];
        first[1].Should().Be((byte)PrinterCommand.CMD_PAGE_START, // 0x20 = 32
            "首帧第二字节应为 CMD_PAGE_START(32)");
    }

    [Fact]
    public async Task SentFrames_FirstFrame_PageStartFrameHasCorrectLayout()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        var first = transport.SentFrames[0];
        // CMD_PAGE_START 帧：[0x1F][0x20][LEN=2][pageKeyHi][pageKeyLo][CRC=0x88]
        first[0].Should().Be(0x1F);
        first[1].Should().Be(0x20);
        first[2].Should().Be(0x02, "pageKey 数据长度固定 2 字节");
        // pageKey 默认 0 → 大端 [0x00, 0x00]
        first[3].Should().Be(0x00);
        first[4].Should().Be(0x00);
        first[5].Should().Be(ProtocolConstants.FixedPackageCrcResult, // 0x88
            "CRC 固定为 0x88");
    }

    [Fact]
    public async Task SentFrames_FirstFrame_ContainsCmdPageWidthAfterPageStart()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        var first = transport.SentFrames[0];
        // CMD_PAGE_START 帧 6 字节后紧跟 CMD_PAGE_WIDTH 帧
        // CMD_PAGE_WIDTH = 39 = 0x27
        first[6].Should().Be(ProtocolConstants.HostToDeviceDataStart); // 0x1F
        first[7].Should().Be((byte)PrinterCommand.CMD_PAGE_WIDTH);     // 0x27
    }

    [Fact]
    public async Task SentFrames_LastFrame_EndsWithPageEndByte()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        var last = transport.SentFrames[^1];
        last[^1].Should().Be(0x0C, "最后一个分片应以 PageEndBytes(0x0C) 结束");
    }

    [Fact]
    public async Task SentFrames_AllFramesAreNonEmpty()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        transport.SentFrames.Should().AllSatisfy(f => f.Should().NotBeEmpty());
    }

    [Fact]
    public async Task SentFrames_TotalByteCount_IsPositive()
    {
        var api = CreateLpApi(out var transport);
        Assert.Equal(LpaResult.Ok, await api.ConnectAsync(new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
        }));
        api.CreateCanvas(60, 40);
        await api.PrintAsync();

        transport.SentFrames.Should().NotBeEmpty();
        transport.SentFrames.Sum(f => f.Length).Should().BeGreaterThan(0,
            "打印应产生非零字节的编码分片");
    }
}
#endregion

#region 2. DrawContext 路径协议帧头断言
public class DrawContextProtocolHeaderTests
{
    [Fact]
    public void Chunks_FirstChunk_StartsWithPageStartFrame()
    {
        using var ctx = new DrawContext(new DrawJobOptions
        {
            WidthMm = 60,
            HeightMm = 40,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
            },
        });
        ctx.Start();
        var chunks = ctx.EncodeChunks();

        chunks.Should().NotBeEmpty();
        var first = chunks[0];
        first[0].Should().Be(0x1F);
        first[1].Should().Be((byte)PrinterCommand.CMD_PAGE_START); // 0x20
        first[2].Should().Be(0x02);
    }

    [Fact]
    public void Chunks_FirstChunk_PageStartCrcIsFixed()
    {
        using var ctx = new DrawContext(new DrawJobOptions
        {
            WidthMm = 40,
            HeightMm = 30,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
            },
        });
        ctx.Start();
        var chunks = ctx.EncodeChunks();

        var first = chunks[0];
        first[5].Should().Be(0x88, "CMD_PAGE_START 帧的 CRC 固定为 0x88");
    }

    [Fact]
    public void Chunks_LastChunk_EndsWithPageEndByte()
    {
        using var ctx = new DrawContext(new DrawJobOptions
        {
            WidthMm = 40,
            HeightMm = 30,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
            },
        });
        ctx.Start();
        var chunks = ctx.EncodeChunks();

        chunks[^1][^1].Should().Be(0x0C);
    }

    [Fact]
    public void Chunks_PageStartFrameContainsPageKeyInBigEndian()
    {
        // 默认 PageKey=0 → [0x00, 0x00]
        using var ctx = new DrawContext(new DrawJobOptions
        {
            WidthMm = 40,
            HeightMm = 30,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
            },
        });
        ctx.Start();
        var chunks = ctx.EncodeChunks();

        var first = chunks[0];
        first[3].Should().Be(0x00, "pageKey 高字节（默认 0）");
        first[4].Should().Be(0x00, "pageKey 低字节（默认 0）");
    }
}
#endregion

#region 3. PrintEncoder 直接调用断言
public class PrintEncoderDirectTests
{
    private static DzImageData CreateBlankImage(int width, int height)
    {
        // 全白图像（RGBA，每像素 4 字节，255 = 白）
        var data = new byte[width * height * 4];
        Array.Fill(data, (byte)255);
        return new DzImageData(width, height, data);
    }

    [Fact]
    public void EncodeImageData_ReturnsNonEmptyChunks()
    {
        var img = CreateBlankImage(48, 32); // 48px wide = 6 bytes/row
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        chunks.Should().NotBeEmpty();
    }

    [Fact]
    public void EncodeImageData_FirstChunkStartsWithPageStartHeader()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        chunks[0][0].Should().Be(0x1F);
        chunks[0][1].Should().Be((byte)PrinterCommand.CMD_PAGE_START);
    }

    [Fact]
    public void EncodeImageData_LastChunkEndsWithPageEnd()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        chunks[^1][^1].Should().Be(0x0C);
    }

    [Fact]
    public void EncodeImageData_InvalidImage_ReturnsEmptyList()
    {
        var invalidImg = new DzImageData(0, 0, Array.Empty<byte>());
        var opts = new PrintImageOptions { ImageData = invalidImg };

        var chunks = PrintEncoder.EncodeImageData(invalidImg, opts);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void UpdatePageKey_ReplacesPageKeyBytes()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        var originalFirst = chunks[0].ToArray();

        // 更新 pageKey 为 0x1234
        PrintEncoder.UpdatePageKey(chunks, 0x1234);

        chunks[0][0].Should().Be(0x1F, "帧头不变");
        chunks[0][1].Should().Be(0x20, "CMD 不变");
        chunks[0][2].Should().Be(0x02, "长度不变");
        chunks[0][3].Should().Be(0x12, "pageKey 高字节 = 0x12");
        chunks[0][4].Should().Be(0x34, "pageKey 低字节 = 0x34");
        chunks[0][5].Should().Be(0x88, "CRC 不变");
    }

    [Fact]
    public void EncodeImageData_PageWidthFrameContainsByteWidth()
    {
        // 48 像素宽 → byteWidth = (48+7)/8 = 6
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0, // landscape → printDimension = img.Width = 48
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        var first = chunks[0];

        // CMD_PAGE_START 帧 6 字节 [0..5]，之后是 CMD_PAGE_WIDTH 帧
        first[6].Should().Be(0x1F);
        first[7].Should().Be((byte)PrinterCommand.CMD_PAGE_WIDTH); // 0x27
        // 数据长度 = 1（Ebv(6) = [6] 单字节）
        first[8].Should().Be(0x01);
        // byteWidth = 6
        first[9].Should().Be(0x06);
        first[10].Should().Be(0x88, "CRC");
    }

    [Fact]
    public void EncodeImageData_DarknessAndSpeedFramesAbsent_WhenSetTo255()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
            PrintDarkness = 255, // Unset → should NOT send
            PrintSpeed = 255,    // Unset → should NOT send
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        var allBytes = new List<byte>();
        foreach (var chunk in chunks) allBytes.AddRange(chunk);

        var hasDarkness = false;
        var hasSpeed = false;
        for (var i = 0; i < allBytes.Count - 1; i++)
        {
            if (allBytes[i] == 0x1F && allBytes[i + 1] == (byte)PrinterCommand.CMD_DARKNESS)
                hasDarkness = true;
            if (allBytes[i] == 0x1F && allBytes[i + 1] == (byte)PrinterCommand.CMD_SPEED)
                hasSpeed = true;
        }
        hasDarkness.Should().BeFalse("darkness=255 means Unset, should not send CMD_DARKNESS");
        hasSpeed.Should().BeFalse("speed=255 means Unset, should not send CMD_SPEED");
    }

    [Fact]
    public void EncodeImageData_DarknessAndSpeedFramesPresent_WhenSetToValidValues()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
            PrintDarkness = 6,  // Normal → sends 6-1 = 5
            PrintSpeed = 3,     // Normal → sends 3-1 = 2
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        var first = chunks[0];

        // CMD_PAGE_START: 6 bytes [0..5]
        // CMD_PAGE_WIDTH: 5 bytes [6..10]
        // CMD_DARKNESS:   5 bytes [11..15] (0x1F 0x43 0x01 0x05 0x88)
        first[11].Should().Be(0x1F);
        first[12].Should().Be((byte)PrinterCommand.CMD_DARKNESS);
        first[13].Should().Be(0x01);
        first[14].Should().Be(0x05, "darkness-1 = 5");
        first[15].Should().Be(0x88);

        // CMD_SPEED: 5 bytes [16..20] (0x1F 0x44 0x01 0x02 0x88)
        first[16].Should().Be(0x1F);
        first[17].Should().Be((byte)PrinterCommand.CMD_SPEED);
        first[18].Should().Be(0x01);
        first[19].Should().Be(0x02, "speed-1 = 2");
        first[20].Should().Be(0x88);
    }

    [Fact]
    public void EncodeImageData_GapTypeFramePresent_WhenInValidRange()
    {
        var img = CreateBlankImage(48, 32);
        var opts = new PrintImageOptions
        {
            ImageData = img,
            PrinterDpi = 203,
            PrinterWidth = 384,
            Orientation = 0,
            GapType = 2, // 有效范围 [0,8]
            GapLength = 0, // GapLength=0 → 不发送 CMD_GAP_LEN
            PrintDarkness = 0, // 无效 → 不发送
            PrintSpeed = 0,    // 无效 → 不发送
        };

        var chunks = PrintEncoder.EncodeImageData(img, opts);
        var first = chunks[0];

        // CMD_PAGE_START: 6 bytes [0..5]
        // CMD_PAGE_WIDTH: 5 bytes [6..10]
        // CMD_GAP_TYPE:   5 bytes [11..15] (0x1F 0x42 0x01 0x02 0x88)
        first[11].Should().Be(0x1F);
        first[12].Should().Be((byte)PrinterCommand.CMD_GAP_TYPE); // 0x42 = 66
        first[13].Should().Be(0x01);
        first[14].Should().Be(0x02, "gapType = 2");
        first[15].Should().Be(0x88);
    }
}
#endregion

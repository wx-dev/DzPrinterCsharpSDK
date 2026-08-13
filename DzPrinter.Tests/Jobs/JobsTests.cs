// =====================================================================
//  Jobs 层单元测试（模块 8：DrawContext + DzPrinterManager）。
//
//  测试分组：
//    1. DrawContext 生命周期：Start → Canvas → Commit → EncodeChunks
//    2. DrawContext 异常路径：未 Start 时访问 Canvas/Commit/EncodeChunks 抛错
//    3. DzPrinterManager 全链路：Discover → Connect → CreateDrawContext → PrintAsync
//    4. DzPrinterManager 异常路径：未连接 PrintAsync/SendRawAsync 返回错误码
// =====================================================================

using DzPrinter.Drawing;
using DzPrinter.Jobs;
using DzPrinter.Printer;
using DzPrinter.Tests.Infrastructure;
using DzPrinter.Transport;

namespace DzPrinter.Tests.Jobs;

#region 1. DrawContext 生命周期
public class DrawContextLifecycleTests
{
    private static DrawJobOptions DefaultOptions() => new()
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
    };

    [Fact]
    public void Start_CreatesCanvas_And_ExposesViaProperty()
    {
        using var ctx = new DrawContext(DefaultOptions());
        var canvas = ctx.Start();
        canvas.Should().NotBeNull();
        canvas.Should().BeSameAs(ctx.Canvas);
        canvas.Base.Should().NotBeNull();
    }

    [Fact]
    public void Start_WithWdfxTemplate_LogsWarning_ButDoesNotThrow()
    {
        var opts = DefaultOptions();
        opts.WdfxTemplateXml = "<root></root>";
        using var ctx = new DrawContext(opts);
        var act = () => ctx.Start();
        act.Should().NotThrow();
    }

    [Fact]
    public void Commit_ReturnsBitmap_AfterStart()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        var bmp = ctx.Commit();
        bmp.Should().NotBeNull();
    }

    [Fact]
    public void EncodeChunks_ReturnsNonEmptyList()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        // 画一点内容，确保有非全白像素
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "TEST",
            X = 5,
            Y = 5,
            FontHeight = 8,
        });

        var chunks = ctx.EncodeChunks();
        chunks.Should().NotBeEmpty();
        chunks.Sum(c => c.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public void EncodeChunks_FirstChunkStartsWithPageStartFrame()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        var chunks = ctx.EncodeChunks();
        chunks.Should().NotBeEmpty();

        var first = chunks[0];
        first.Length.Should().BeGreaterOrEqualTo(6);
        // 帧头：0x1F + CMD_PAGE_START(32=0x20)
        first[0].Should().Be(ProtocolConstants.HostToDeviceDataStart); // 0x1F
        first[1].Should().Be((byte)PrinterCommand.CMD_PAGE_START);     // 0x20
    }

    [Fact]
    public void GetBitmap_ReturnsNull_BeforeStart()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.GetBitmap().Should().BeNull();
    }

    [Fact]
    public void GetBitmap_ReturnsBitmap_AfterStart()
    {
        using var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        ctx.GetBitmap().Should().NotBeNull();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var ctx = new DrawContext(DefaultOptions());
        ctx.Start();
        ctx.Dispose();
        ctx.Dispose(); // 不抛异常
    }
}
#endregion

#region 2. DrawContext 异常路径
public class DrawContextErrorTests
{
    [Fact]
    public void Canvas_BeforeStart_Throws()
    {
        using var ctx = new DrawContext(new DrawJobOptions());
        var act = () => _ = ctx.Canvas;
        act.Should().Throw<InvalidOperationException>().WithMessage("*Start*");
    }

    [Fact]
    public void Commit_BeforeStart_Throws()
    {
        using var ctx = new DrawContext(new DrawJobOptions());
        var act = () => ctx.Commit();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Start*");
    }

    [Fact]
    public void EncodeChunks_BeforeStart_Throws()
    {
        using var ctx = new DrawContext(new DrawJobOptions());
        var act = () => ctx.EncodeChunks();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Start*");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new DrawContext(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
#endregion

#region 3. DzPrinterManager 全链路
public class DzPrinterManagerTests
{
    private static PrinterDevice TestDevice() => new()
    {
        DeviceId = "test-001",
        Name = "P2-TEST",
        DeviceType = LpaDeviceType.Ble,
    };

    [Fact]
    public async Task FullChain_DiscoverConnectDrawPrint_Succeeds()
    {
        var transport = new MockTransport
        {
            DiscoverDevices = new[]
            {
                new DeviceInfo
                {
                    DeviceId = "test-001",
                    DeviceName = "P2-TEST",
                    TransportType = TransportType.BluetoothLowEnergy,
                },
            },
        };
        using var manager = new DzPrinterManager(transport);

        // 1. 发现
        var devices = await manager.DiscoverAsync();
        devices.Should().HaveCount(1);

        // 2. 连接
        await manager.ConnectAsync(devices[0]);
        manager.IsConnected.Should().BeTrue();
        manager.ConnectedDevice.Should().NotBeNull();

        // 3. 创建绘制作业
        using var ctx = manager.CreateDrawContext(new DrawJobOptions
        {
            WidthMm = 60,
            HeightMm = 40,
            PrinterInfo = new PrinterInfo
            {
                PrinterDpi = 203,
                PrinterWidth = 384,
                PageCount = 1,
            },
        });
        ctx.Start();
        ctx.Canvas.DrawText(new DrawOptions
        {
            Text = "Hello",
            X = 5,
            Y = 5,
            FontHeight = 8,
        });

        // 4. 打印
        var result = await manager.PrintAsync(ctx);
        result.Should().Be(LpaResult.Ok);
        transport.SentFrames.Should().NotBeEmpty();
        transport.SentFrames.Sum(f => f.Length).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PrintAsync_Disconnected_ReturnsErrorNoPrinter()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        using var ctx = manager.CreateDrawContext(new DrawJobOptions { WidthMm = 40, HeightMm = 30 });
        ctx.Start();

        var result = await manager.PrintAsync(ctx);
        result.Should().Be(LpaResult.ErrorNoPrinter);
        transport.SentFrames.Should().BeEmpty();
    }

    [Fact]
    public async Task PrintAsync_NullContext_Throws()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        var act = () => manager.PrintAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendRawAsync_Disconnected_ReturnsErrorNoPrinter()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        var result = await manager.SendRawAsync(new byte[] { 0x01, 0x02 });
        result.Should().Be(LpaResult.ErrorNoPrinter);
    }

    [Fact]
    public async Task SendRawAsync_Connected_SendsData()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        await manager.ConnectAsync(TestDevice());

        var result = await manager.SendRawAsync(new byte[] { 0xAA, 0xBB, 0xCC });
        result.Should().Be(LpaResult.Ok);
        transport.SentFrames.Should().HaveCount(1);
        transport.SentFrames[0].Should().Equal(0xAA, 0xBB, 0xCC);
    }

    [Fact]
    public async Task Disconnect_ClearsConnectedDevice()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        await manager.ConnectAsync(TestDevice());
        manager.ConnectedDevice.Should().NotBeNull();

        await manager.DisconnectAsync();
        manager.IsConnected.Should().BeFalse();
        manager.ConnectedDevice.Should().BeNull();
    }

    [Fact]
    public async Task TransportForceDisconnect_ClearsConnectedDevice()
    {
        var transport = new MockTransport();
        using var manager = new DzPrinterManager(transport);
        await manager.ConnectAsync(TestDevice());

        transport.ForceState(ConnectionState.Disconnected);
        manager.ConnectedDevice.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullTransport_Throws()
    {
        var act = () => new DzPrinterManager(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Dispose_DisconnectsTransport()
    {
        var transport = new MockTransport();
        var manager = new DzPrinterManager(transport);
        await manager.ConnectAsync(TestDevice());

        manager.Dispose();
        transport.State.Should().Be(ConnectionState.Disconnected);
    }
}
#endregion

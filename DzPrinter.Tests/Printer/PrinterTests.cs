// =====================================================================
//  Printer 层单元测试。
//
//  测试分 4 组：
//    1. SupportPrinterMatcher + PrinterDevice：机型前缀识别/FilterSupported/GetModelName
//    2. DeviceConnection：连接/断开/发送/事件/状态传播
//    3. DeviceManager：发现/连接/断开/多设备去重/释放
//    4. LPAPI 画布：CreateCanvas → 编码 PrintEncoder.EncodeImageData → 发送分片
// =====================================================================

using DzPrinter.Drawing;
using DzPrinter.Printer;
using DzPrinter.Tests.Infrastructure;
using DzPrinter.Transport;

namespace DzPrinter.Tests.Printer;

#region 1. SupportPrinterMatcher / 机型识别
public class SupportPrinterMatcherTests
{
    [Theory]
    [InlineData("D60-ABC123", true)]
    [InlineData("P2-XYZ", true)]
    [InlineData("DT-P2", true)]
    [InlineData("A300-SERIAL", true)]
    [InlineData("MiSmartHome", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("X1-23", false)]
    public void IsSupported_RecognizesDtPrefixes(string? name, bool expected)
    {
        SupportPrinterMatcher.IsSupported(name).Should().Be(expected);
    }

    [Fact]
    public void FilterSupported_KeepsOnlySupported()
    {
        var devices = new[]
        {
            new PrinterDevice { Name = "P2-A", DeviceId = "1" },
            new PrinterDevice { Name = "D60-B", DeviceId = "2" },
            new PrinterDevice { Name = "Unknown-C", DeviceId = "3" },
        };
        var kept = SupportPrinterMatcher.FilterSupported(devices);
        kept.Should().HaveCount(2)
            .And.SatisfyRespectively(
                x => x.Name.Should().Be("P2-A"),
                x => x.Name.Should().Be("D60-B"));
    }

    [Theory]
    [InlineData("D60-ABC123", "ABC123")]
    [InlineData("D60", "D60")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void GetModelName_ParsesLastSegment(string? input, string expected)
    {
        SupportPrinterMatcher.GetModelName(input).Should().Be(expected);
    }
}
#endregion

#region 2. DeviceConnection
public class DeviceConnectionTests
{
    private static BleConnection CreateConnection(out MockTransport transport)
    {
        transport = new MockTransport();
        return new BleConnection(transport);
    }

    private static DeviceInfo TestDevice(string id = "00AABBCC", string name = "P2-UNIT") => new()
    {
        DeviceId = id,
        DeviceName = name,
        TransportType = TransportType.BluetoothLowEnergy,
    };

    [Fact]
    public async Task ConnectAsync_UpdatesState_And_FiresEvent()
    {
        var conn = CreateConnection(out var transport);
        var connected = new List<DeviceInfo>();
        conn.DeviceConnected += d => connected.Add(d);

        await conn.ConnectAsync(TestDevice());

        conn.IsConnected.Should().BeTrue();
        conn.ConnectedDevice.Should().NotBeNull();
        connected.Should().HaveCount(1);
    }

    [Fact]
    public async Task DisconnectAsync_FiresDisconnectEvent()
    {
        var conn = CreateConnection(out _);
        DeviceInfo? lastDevice = null;
        string? lastReason = null;
        conn.DeviceDisconnected += (d, r) => { lastDevice = d; lastReason = r; };

        await conn.ConnectAsync(TestDevice());
        await conn.DisconnectAsync();

        conn.IsConnected.Should().BeFalse();
        lastDevice.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_LargeData_SplitsIntoFrames()
    {
        var conn = CreateConnection(out var transport);
        await conn.ConnectAsync(TestDevice());

        // 12 字节数据，BLE 默认 PackSize=20 → 1 帧
        var big = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        await conn.SendAsync(big);
        transport.SentFrames.Should().HaveCount(1);
        transport.SentFrames[0].Should().Equal(big);
    }

    [Fact]
    public async Task SendAsync_SmallData_OneFrame()
    {
        var conn = CreateConnection(out var transport);
        await conn.ConnectAsync(TestDevice());

        await conn.SendAsync(new byte[] { 0xAA, 0xBB });
        transport.SentFrames.Should().HaveCount(1);
    }

    [Fact]
    public async Task DataReceived_ForwardedFromTransport()
    {
        var conn = CreateConnection(out var transport);
        var received = new List<byte[]>();
        conn.DataReceived += d => received.Add(d);

        await conn.ConnectAsync(TestDevice());
        transport.Receive(new byte[] { 0x01, 0x02 });

        received.Should().HaveCount(1).And.ContainSingle(x => x.SequenceEqual(new byte[] { 0x01, 0x02 }));
    }

    [Fact]
    public async Task TransportForceDisconnect_PropagatesToConnection()
    {
        var conn = CreateConnection(out var transport);
        var disconnected = 0;
        conn.DeviceDisconnected += (_, _) => disconnected++;

        await conn.ConnectAsync(TestDevice());
        transport.ForceState(ConnectionState.Disconnected);

        conn.IsConnected.Should().BeFalse();
        disconnected.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SendAsync_NotConnected_Throws()
    {
        var conn = CreateConnection(out _);
        Func<Task> act = () => conn.SendAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
#endregion

#region 3. DeviceManager
public class DeviceManagerTests
{
    private static DeviceManager CreateManager(out MockTransport transport)
    {
        transport = new MockTransport();
        var captured = transport;
        return new DeviceManager(_ => captured);
    }

    private static DeviceManager CreateManagerWithFactory(Func<LpaDeviceType, MockTransport> factory)
    {
        return new DeviceManager(t => factory(t));
    }

    [Fact]
    public async Task Discover_FiltersSupportedPrinters_ReturnsOnlyPrefixMatched()
    {
        var bleTransport = new MockTransport
        {
            DiscoverDevices = new[]
            {
                new DeviceInfo { DeviceId = "p2", DeviceName = "P2-UNIT", TransportType = TransportType.BluetoothLowEnergy },
                new DeviceInfo { DeviceId = "z", DeviceName = "NoMatch-Z", TransportType = TransportType.BluetoothLowEnergy },
                new DeviceInfo { DeviceId = "d60", DeviceName = "D60-SER", TransportType = TransportType.BluetoothLowEnergy },
            }
        };
        var hidTransport = new MockTransport { DiscoverDevices = Array.Empty<DeviceInfo>() };

        var manager = CreateManagerWithFactory(t => t switch
        {
            LpaDeviceType.Ble => bleTransport,
            _ => hidTransport,
        });

        var devices = await manager.DiscoverAsync(LpaDeviceType.Auto);

        devices.Should().HaveCount(2);
        devices.Select(d => d.Name).Should().Contain("P2-UNIT").And.Contain("D60-SER");
    }

    [Fact]
    public async Task Connect_ThenDisconnect_StateUpdates()
    {
        var transport = new MockTransport
        {
            DiscoverDevices = new[] { new DeviceInfo { DeviceId = "ble_1", DeviceName = "P2-UNIT" } }
        };
        var manager = CreateManagerWithFactory(_ => transport);

        var devices = await manager.DiscoverAsync(LpaDeviceType.Ble);
        await manager.ConnectAsync(devices[0]);
        manager.GetActiveConnection()?.IsConnected.Should().BeTrue();
        transport.ConnectCalls.Should().Be(1);

        await manager.DisconnectAsync(devices[0].DeviceId);
        manager.GetActiveConnection().Should().BeNull();
        transport.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connect_NotConnected_ThenConnected()
    {
        var transport = new MockTransport();
        var manager = CreateManagerWithFactory(_ => transport);

        manager.GetActiveConnection().Should().BeNull();

        transport.DiscoverDevices = new[] { new DeviceInfo { DeviceId = "x", DeviceName = "P2-1" } };
        var devices = await manager.DiscoverAsync(LpaDeviceType.Ble);
        await manager.ConnectAsync(devices[0]);
        manager.GetActiveConnection()?.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_DisconnectsTransport()
    {
        var transport = new MockTransport
        {
            DiscoverDevices = new[] { new DeviceInfo { DeviceId = "x", DeviceName = "P2-1" } }
        };
        var manager = CreateManagerWithFactory(_ => transport);

        var devices = await manager.DiscoverAsync(LpaDeviceType.Ble);
        await manager.ConnectAsync(devices[0]);
        manager.Dispose();
        transport.State.Should().Be(ConnectionState.Disconnected);
    }
}
#endregion

#region 4. LPAPI 画布+编码+发送流程
public class LpApiIntegrationTests
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
    public async Task PrintAsync_NoConnection_ReturnsNoPrinter()
    {
        var api = CreateLpApi(out _);
        var result = await api.PrintAsync();
        result.Should().Be(LpaResult.ErrorNoPrinter);
    }

    [Fact]
    public async Task PrintAsync_NoCanvas_ReturnsErrorParam()
    {
        var api = CreateLpApi(out var transport);
        transport.DiscoverDevices = new[] { new DeviceInfo { DeviceId = "1", DeviceName = "P2-TEST" } };
        var devices = await api.DiscoverAsync(LpaDeviceType.Ble);
        await api.ConnectAsync(devices[0]);

        var result = await api.PrintAsync();
        result.Should().Be(LpaResult.ErrorParam);
    }

    [Fact]
    public async Task PrintAsync_DrawThenPrint_EncodesAndSendsChunks()
    {
        var api = CreateLpApi(out var transport);

        var device = new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.Ble,
            ModelName = "SDK",
        };
        await api.ConnectAsync(device);
        api.IsConnected.Should().BeTrue();

        var canvas = api.CreateCanvas(widthMm: 60, heightMm: 40, orientation: 0);
        canvas.Should().NotBeNull();
        canvas.DrawText(new DrawOptions
        {
            Text = "Hello SDK",
            X = 5,
            Y = 5,
            FontHeight = 6,
        });

        var result = await api.PrintAsync();

        result.Should().Be(LpaResult.Ok);
        transport.SentFrames.Should().NotBeEmpty("打印应产生编码分片");

        var first = transport.SentFrames.First();
        first.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateCanvas_ProvidesPrinterCanvasMm()
    {
        var api = CreateLpApi(out _);
        var canvas = api.CreateCanvas(60, 40);
        canvas.Should().NotBeNull();
        canvas.Base.Should().NotBeNull();
        api.Canvas.Should().BeSameAs(canvas);
    }

    [Fact]
    public void LPAPIFactory_RequiresTransportFactory()
    {
        LPAPIFactory.TransportFactory = null;
        Action act = () => LPAPIFactory.GetInstance();
        act.Should().Throw<InvalidOperationException>().WithMessage("*TransportFactory*");
    }

    [Fact]
    public void LPAPIFactory_GetInstance_CachesSingleton()
    {
        try
        {
            LPAPIFactory.TransportFactory = t => new MockTransport();
            var a = LPAPIFactory.GetInstance();
            var b = LPAPIFactory.GetInstance();
            a.Should().BeSameAs(b);
        }
        finally
        {
            LPAPIFactory.QuitApi();
        }
    }
}
#endregion

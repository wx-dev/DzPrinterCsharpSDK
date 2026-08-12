// =====================================================================
//  Printer 层单元测试（模块 8 之前的现有 Printer 模块）。
//
//  测试分 4 组：
//    1. SupportPrinterMatcher + PrinterDevice：机型前缀识别/FilterSupported/GetModelName
//    2. DeviceConnection / BleConnection：分片发送、状态变更、事件
//    3. DeviceManager：发现 + 连接 + 断开 + 多设备去重
//    4. LPAPI：CreateCanvas → 编码 PrintEncoder.EncodeImageData → 发送分片
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

#region 2. DeviceConnection + BleConnection
public class DeviceConnectionTests
{
    private static DeviceInfo MakeDevice(string id = "00AABBCC", string name = "P2-UNIT") => new()
    {
        DeviceId = id,
        DeviceName = name,
        TransportType = TransportType.BluetoothLowEnergy,
    };

    [Fact]
    public async Task DeviceConnection_Connect_FiresEvent_And_UpdatesStatus()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport);

        var connected = new List<DeviceInfo>();
        conn.DeviceConnected += d => connected.Add(d);

        var device = MakeDevice();
        await conn.ConnectAsync(device);

        conn.IsConnected.Should().BeTrue();
        conn.ConnectedDevice.Should().BeSameAs(device);
        conn.State.Should().Be(ConnectionState.Connected);
        conn.PrintStatus.Should().Be(EPrintStatus.ReadyPrint);
        connected.Should().HaveCount(1).And.ContainSingle(x => x.DeviceId == device.DeviceId);
    }

    [Fact]
    public async Task DeviceConnection_Disconnect_FiresDisconnectEvent()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport);
        DeviceInfo? lastDevice = null;
        string? lastReason = null;
        conn.DeviceDisconnected += (d, r) => { lastDevice = d; lastReason = r; };

        var device = MakeDevice();
        await conn.ConnectAsync(device);
        await conn.DisconnectAsync();

        conn.IsConnected.Should().BeFalse();
        conn.PrintStatus.Should().Be(EPrintStatus.None);
        lastDevice.Should().NotBeNull();
        lastReason.Should().BeNull();
    }

    [Fact]
    public async Task BleConnection_SendLargeData_SplitsIntoPackSizeFrames()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport)
        {
            PackSize = 5,
            SendIntervalMs = 0,
        };
        await conn.ConnectAsync(MakeDevice());

        var big = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }; // 12B, PackSize=5
        await conn.SendAsync(big);

        transport.SentFrames.Should().HaveCount(3);
        transport.SentFrames[0].Should().Equal(1, 2, 3, 4, 5);
        transport.SentFrames[1].Should().Equal(6, 7, 8, 9, 10);
        transport.SentFrames[2].Should().Equal(11, 12);
    }

    [Fact]
    public async Task BleConnection_SendSmallData_OneFrame()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport) { PackSize = 20, SendIntervalMs = 0 };
        await conn.ConnectAsync(MakeDevice());

        await conn.SendAsync(new byte[] { 0xAA, 0xBB });
        transport.SentFrames.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceConnection_DataReceived_PipesFromTransport()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport);
        var received = new List<byte[]>();
        conn.DataReceived += d => received.Add(d);
        await conn.ConnectAsync(MakeDevice());

        transport.Receive(new byte[] { 0x01, 0x02 });
        received.Should().HaveCount(1).And.ContainSingle(x => x.SequenceEqual(new byte[] { 0x01, 0x02 }));
    }

    [Fact]
    public async Task DeviceConnection_TransportForceDisconnect_PropagatesToConnection()
    {
        var transport = new MockTransport();
        using var conn = new BleConnection(transport);
        var disconnected = 0;
        conn.DeviceDisconnected += (_, _) => disconnected++;

        await conn.ConnectAsync(MakeDevice());
        transport.ForceState(ConnectionState.Disconnected);

        conn.IsConnected.Should().BeFalse();
        conn.ConnectedDevice.Should().BeNull();
        disconnected.Should().Be(1);
    }

    [Fact]
    public void BleConnection_DeviceType_IsWebBle()
    {
        new BleConnection(new MockTransport()).DeviceType.Should().Be(LpaDeviceType.WebBle);
    }

    [Fact]
    public void HidConnection_DeviceType_IsWebHid()
    {
        new HidConnection(new MockTransport()).DeviceType.Should().Be(LpaDeviceType.WebHid);
    }
}
#endregion

#region 3. DeviceManager
public class DeviceManagerTests
{
    private static readonly DeviceInfo BleDevice = new()
    {
        DeviceId = "ble_1",
        DeviceName = "P2-UNIT",
        TransportType = TransportType.BluetoothLowEnergy,
    };

    private static readonly DeviceInfo HidDevice = new()
    {
        DeviceId = "hid_1",
        DeviceName = "DT-P2",
        TransportType = TransportType.HidUsb,
    };

    private static DeviceManager CreateManager(Func<LpaDeviceType, (MockTransport Transport, IDeviceTransport Interface)> provider)
    {
        IDeviceTransport Factory(LpaDeviceType t) => provider(t).Interface;
        return new DeviceManager(Factory);
    }

    [Fact]
    public async Task Discover_FiltersSupportedPrinters_ReturnsOnlyPrefixMatched()
    {
        // 构造：BleTransport 返回 3 个设备（2 个支持 + 1 个不支持）
        var bleTransport = new MockTransport
        {
            DiscoverDevices = new[]
            {
                BleDevice,
                new DeviceInfo { DeviceId = "z", DeviceName = "NoMatch-Z", TransportType = TransportType.BluetoothLowEnergy },
                new DeviceInfo { DeviceId = "d60", DeviceName = "D60-SER", TransportType = TransportType.BluetoothLowEnergy },
            }
        };
        var hidTransport = new MockTransport { DiscoverDevices = Array.Empty<DeviceInfo>() };

        var dm = CreateManager(t => t switch
        {
            LpaDeviceType.WebBle => (bleTransport, bleTransport),
            _ => (hidTransport, hidTransport),
        });

        var devices = await dm.DiscoverAsync(LpaDeviceType.Auto, filterSupported: true);

        devices.Should().HaveCount(2);
        devices.Select(d => d.Name).Should().Contain("P2-UNIT").And.Contain("D60-SER");
    }

    [Fact]
    public async Task Connect_ThenDisconnect_StateUpdatesAndEvents()
    {
        var transport = new MockTransport { DiscoverDevices = new[] { BleDevice } };
        var dm = CreateManager(_ => (transport, transport));

        var device = new PrinterDevice
        {
            DeviceId = BleDevice.DeviceId,
            Name = BleDevice.DeviceName,
            DeviceType = LpaDeviceType.WebBle,
            ModelName = "UNIT",
        };

        // 连接
        var conn = await dm.ConnectAsync(device);
        dm.ConnectionCount.Should().Be(1);
        dm.ActiveDeviceType.Should().Be(LpaDeviceType.WebBle);
        conn.Should().NotBeNull();
        transport.ConnectCalls.Should().Be(1);

        // 断开（DeviceManager 会同时调 conn.DisconnectAsync + conn.Dispose，都可能触发 Transport.DisconnectAsync）
        await dm.DisconnectAsync(device.DeviceId);
        dm.ConnectionCount.Should().Be(0);
        transport.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task GetActiveConnection_ReturnsAliveConnection()
    {
        var transport = new MockTransport();
        var dm = CreateManager(_ => (transport, transport));

        var device = new PrinterDevice
        {
            DeviceId = "x",
            Name = "P2-1",
            DeviceType = LpaDeviceType.WebBle,
            ModelName = "1",
        };
        dm.GetActiveConnection().Should().BeNull();
        await dm.ConnectAsync(device);
        dm.GetActiveConnection().Should().NotBeNull();
    }

    [Fact]
    public async Task DisconnectAll_DisconnectsEach()
    {
        var t1 = new MockTransport();
        var t2 = new MockTransport();
        var transports = new Dictionary<LpaDeviceType, MockTransport>
        {
            [LpaDeviceType.WebBle] = t1,
            [LpaDeviceType.WebHid] = t2,
        };
        var dm = CreateManager(t => (transports[t], transports[t]));

        await dm.ConnectAsync(new PrinterDevice
        {
            DeviceId = "b",
            Name = "P2-B",
            DeviceType = LpaDeviceType.WebBle,
        });
        await dm.ConnectAsync(new PrinterDevice
        {
            DeviceId = "h",
            Name = "D60-H",
            DeviceType = LpaDeviceType.WebHid,
        });

        dm.ConnectionCount.Should().Be(2);
        await dm.DisconnectAllAsync();
        dm.ConnectionCount.Should().Be(0);
        t1.State.Should().Be(ConnectionState.Disconnected);
        t2.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task DeviceManager_Dispose_DisconnectsAll()
    {
        var transport = new MockTransport();
        var dm = CreateManager(_ => (transport, transport));
        await dm.ConnectAsync(new PrinterDevice
        {
            DeviceId = "x",
            Name = "P2-1",
            DeviceType = LpaDeviceType.WebBle,
        });
        dm.Dispose();
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
        var captured = transport; // 避免在 lambda 中捕获 out 形参 (CS1628)
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
        var devices = await api.DiscoverAsync(LpaDeviceType.WebBle);
        await api.ConnectAsync(devices[0]);

        var result = await api.PrintAsync();
        result.Should().Be(LpaResult.ErrorParam);
    }

    [Fact]
    public async Task PrintAsync_DrawThenPrint_EncodesAndSendsChunks()
    {
        var api = CreateLpApi(out var transport);

        // 连接
        var device = new PrinterDevice
        {
            DeviceId = "001122",
            Name = "P2-SDK",
            DeviceType = LpaDeviceType.WebBle,
            ModelName = "SDK",
        };
        await api.ConnectAsync(device);
        api.IsConnected.Should().BeTrue();

        // 创建 60mm × 40mm 画布，写一点文本
        var canvas = api.CreateCanvas(widthMm: 60, heightMm: 40, orientation: 0);
        canvas.Should().NotBeNull();
        canvas.DrawText(new DrawOptions
        {
            Text = "Hello SDK",
            X = 5,
            Y = 5,
            FontHeight = 6,
        });

        // 执行打印 → PrintEncoder 编码 → 发送分片
        var result = await api.PrintAsync();

        result.Should().Be(LpaResult.Ok);
        transport.SentFrames.Should().NotBeEmpty("打印应产生编码分片");

        // 第一个分片以 HostToDeviceDataStart 开头（对应 CMD_PAGE_START 或握手包）
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
        LPAPIFactory.TransportFactory = null; // reset
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

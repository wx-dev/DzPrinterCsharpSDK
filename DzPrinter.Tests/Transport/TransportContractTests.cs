// =====================================================================
//  Transport 层单元测试（模块 6：接口 + 状态机 + 事件 + 模型）。
//
//  被测内容：
//    - DeviceInfo / ConnectionState / TransportType 枚举/类初始化与 ToString
//    - EventArgs 构造
//    - MockTransport 自身的状态机流转（IDeviceTransport 契约验证）
//    - ConnectionStateChanged / DataReceived 事件分发
//    - RequestAsync 超时/响应
// =====================================================================

using DzPrinter.Tests.Infrastructure;
using DzPrinter.Transport;

namespace DzPrinter.Tests.Transport;

public class DeviceInfoTests
{
    [Fact]
    public void DeviceInfo_Defaults_AreSane()
    {
        var info = new DeviceInfo();
        info.DeviceId.Should().Be(string.Empty);
        info.DeviceName.Should().Be(string.Empty);
        info.TransportType.Should().Be(TransportType.Unknown);
        info.HardwareFlags.Should().Be(0);
        info.SoftwareFlags.Should().Be(0);
        info.BufferSize.Should().Be(0);
        info.Dpi.Should().Be(0);
        info.PrinterWidth.Should().Be(0);
        info.ClientType.Should().Be(0);
        info.NativeDevice.Should().BeNull();
    }

    [Fact]
    public void DeviceInfo_ToString_ContainsNameAndType()
    {
        var info = new DeviceInfo
        {
            DeviceName = "DT-888",
            DeviceId = "0011223344",
            TransportType = TransportType.BluetoothLowEnergy,
        };
        var s = info.ToString();
        s.Should().Contain("DT-888");
        s.Should().Contain("BluetoothLowEnergy");
        s.Should().Contain("0011223344");
    }

    [Fact]
    public void TransportType_Values_MatchContract()
    {
        // 这些值是跨模块共享的契约，禁止随意变更数字。
        ((int)TransportType.Unknown).Should().Be(0);
        ((int)TransportType.BluetoothLowEnergy).Should().Be(1);
        ((int)TransportType.HidUsb).Should().Be(2);
        ((int)TransportType.BluetoothClassic).Should().Be(3);
        ((int)TransportType.TcpIp).Should().Be(4);
        ((int)TransportType.Mock).Should().Be(99);
    }

    [Fact]
    public void ConnectionState_Values_MatchContract()
    {
        ((int)ConnectionState.Disconnected).Should().Be(0);
        ((int)ConnectionState.Connecting).Should().Be(1);
        ((int)ConnectionState.Connected).Should().Be(2);
        ((int)ConnectionState.Disconnecting).Should().Be(3);
        ((int)ConnectionState.Failed).Should().Be(4);
    }

    [Fact]
    public void EventArgs_Constructors_Work()
    {
        var dataArgs = new DataReceivedEventArgs(new byte[] { 1, 2, 3 });
        dataArgs.Data.Should().Equal(1, 2, 3);

        var stateArgs = new ConnectionStateChangedEventArgs(ConnectionState.Failed, "boom");
        stateArgs.State.Should().Be(ConnectionState.Failed);
        stateArgs.Message.Should().Be("boom");

        var stateArgsNoMsg = new ConnectionStateChangedEventArgs(ConnectionState.Connected);
        stateArgsNoMsg.Message.Should().BeNull();
    }
}

public class MockTransportStateMachineTests
{
    [Fact]
    public async Task Connect_Disconnect_StateTransitions_AreOrdered()
    {
        var t = new MockTransport();
        var device = new DeviceInfo { DeviceId = "x", DeviceName = "y" };

        await t.ConnectAsync(device);
        t.State.Should().Be(ConnectionState.Connected);
        t.ConnectedDevice.Should().BeSameAs(device);
        t.ConnectCalls.Should().Be(1);

        await t.DisconnectAsync();
        t.State.Should().Be(ConnectionState.Disconnected);
        t.ConnectedDevice.Should().BeNull();
        t.DisconnectCalls.Should().Be(1);

        t.StateTransitions.Should().Equal(
            ConnectionState.Connecting,
            ConnectionState.Connected,
            ConnectionState.Disconnecting,
            ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connect_WithException_GoesToFailed()
    {
        var t = new MockTransport { ConnectException = new InvalidOperationException("nope") };
        var act = () => t.ConnectAsync(new DeviceInfo { DeviceId = "x" });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("nope");
        t.State.Should().Be(ConnectionState.Failed);
    }

    [Fact]
    public async Task Send_NotConnected_Throws()
    {
        var t = new MockTransport();
        var act = () => t.SendAsync(new byte[] { 1, 2, 3 });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*未连接*");
    }

    [Fact]
    public async Task Send_RecordsFrame()
    {
        var t = new MockTransport();
        await t.ConnectAsync(new DeviceInfo { DeviceId = "x" });
        await t.SendAsync(new byte[] { 0xAA, 0xBB, 0xCC });
        t.SentFrames.Should().HaveCount(1).And.ContainSingle(f => f.SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC }));
    }

    [Fact]
    public async Task Discover_ReturnsConfiguredDevices()
    {
        var devices = new[]
        {
            new DeviceInfo { DeviceId = "1", DeviceName = "A", TransportType = TransportType.BluetoothLowEnergy },
            new DeviceInfo { DeviceId = "2", DeviceName = "B", TransportType = TransportType.HidUsb },
        };
        var t = new MockTransport { DiscoverDevices = devices };
        var result = await t.DiscoverAsync();
        result.Should().BeEquivalentTo(devices);
    }

    [Fact]
    public void DataReceived_Event_FiresWhenReceiveCalled()
    {
        var t = new MockTransport();
        var received = new List<byte[]>();
        t.DataReceived += (_, e) => received.Add(e.Data);
        t.Receive(new byte[] { 0x11, 0x22 });
        received.Should().HaveCount(1).And.ContainSingle(x => x.SequenceEqual(new byte[] { 0x11, 0x22 }));
    }

    [Fact]
    public async Task ConnectionStateChanged_Event_FiresForEachTransition()
    {
        var t = new MockTransport();
        var events = new List<ConnectionState>();
        t.ConnectionStateChanged += (_, e) => events.Add(e.State);
        await t.ConnectAsync(new DeviceInfo { DeviceId = "x" });
        await t.DisconnectAsync();
        events.Should().Equal(
            ConnectionState.Connecting,
            ConnectionState.Connected,
            ConnectionState.Disconnecting,
            ConnectionState.Disconnected);
    }

    [Fact]
    public async Task RequestAsync_UsesResponder()
    {
        var t = new MockTransport();
        byte[]? gotRequest = null;
        t.RequestResponder = req =>
        {
            gotRequest = req;
            return new byte[] { 0xFF, 0xFE };
        };
        var received = new List<byte[]>();
        t.DataReceived += (_, e) => received.Add(e.Data);

        await t.ConnectAsync(new DeviceInfo { DeviceId = "x" });
        var resp = await t.RequestAsync(new byte[] { 0x01, 0x02 }, timeoutMs: 200);

        gotRequest.Should().Equal(0x01, 0x02);
        resp.Should().Equal(0xFF, 0xFE);
        received.Should().ContainSingle(x => x.SequenceEqual(new byte[] { 0xFF, 0xFE }));
    }
}

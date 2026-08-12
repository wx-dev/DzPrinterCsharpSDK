// =====================================================================
//  MockTransport：内存级 IDeviceTransport 伪造。
//
//  设计目标：
//    1. 零平台依赖，所有 Transport 测试 / Printer 测试共用同一个伪造实现。
//    2. 覆盖连接生命周期：Disconnected → Connecting → Connected → Disconnecting → Disconnected。
//    3. Send 会记录到 SentFrames 列表，方便断言。
//    4. Receive(byte[]) 方法：测试侧手动触发"设备→主机"通知，验证事件分发与 RequestAsync。
//    5. 可配置 DiscoverDevices / ConnectException / SendException 以注入故障。
// =====================================================================

using DzPrinter.Transport;

namespace DzPrinter.Tests.Infrastructure;

/// <summary>可观测的内存级 IDeviceTransport 伪造。</summary>
public sealed class MockTransport : IDeviceTransport, IDisposable
{
    private readonly object _sync = new();
    private ConnectionState _state = ConnectionState.Disconnected;
    private DeviceInfo? _connected;

    // 注入钩子
    public IReadOnlyList<DeviceInfo> DiscoverDevices { get; set; } = Array.Empty<DeviceInfo>();
    public Exception? ConnectException { get; set; }
    public Exception? SendException { get; set; }
    public int RequestDelayMs { get; set; } = 0;
    public Func<byte[], byte[]>? RequestResponder { get; set; }

    // 可观测
    public List<byte[]> SentFrames { get; } = new();
    public List<ConnectionState> StateTransitions { get; } = new();
    public int ConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }

    public ConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public DeviceInfo? ConnectedDevice
    {
        get { lock (_sync) return _connected; }
    }

    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    // ============ 操作 ============

    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DiscoverDevices);
    }

    public async Task ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        SetState(ConnectionState.Connecting);
        await Task.Yield();
        if (ConnectException != null)
        {
            SetState(ConnectionState.Failed);
            throw ConnectException;
        }
        lock (_sync)
        {
            _connected = device;
            ConnectCalls++;
        }
        SetState(ConnectionState.Connected);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(ConnectionState.Disconnecting);
        await Task.Yield();
        lock (_sync)
        {
            _connected = null;
            DisconnectCalls++;
        }
        SetState(ConnectionState.Disconnected);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (SendException != null) throw SendException;
        if (State != ConnectionState.Connected)
            throw new InvalidOperationException("未连接");
        lock (SentFrames) SentFrames.Add(data.ToArray());
    }

    public async Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(data, cancellationToken).ConfigureAwait(false);
        if (RequestDelayMs > 0)
            await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        if (RequestResponder != null)
        {
            var response = RequestResponder(data.ToArray());
            Receive(response);
            return response;
        }
        return null;
    }

    /// <summary>测试侧模拟"设备→主机"发回数据，触发 DataReceived 事件。</summary>
    public void Receive(byte[] payload)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        DataReceived?.Invoke(this, new DataReceivedEventArgs(payload));
    }

    /// <summary>测试侧强制切换状态（用于模拟断线/故障）。</summary>
    public void ForceState(ConnectionState state, string? message = null)
    {
        SetState(state, message);
    }

    public void Dispose()
    {
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
    }

    // ============ 私有 ============

    private void SetState(ConnectionState state, string? msg = null)
    {
        lock (_sync) _state = state;
        StateTransitions.Add(state);
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, msg));
    }
}

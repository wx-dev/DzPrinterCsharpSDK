// =====================================================================
//  传输层基类。提取 BLE/HID/File 三个 Transport 实现中的公共成员：
//    - 状态字段与属性（_sync / _state / _connectedDevice / State / ConnectedDevice）
//    - 事件（DataReceived / ConnectionStateChanged）
//    - SetState 状态机辅助方法
//    - TryExtractProtocolFrame 协议帧提取（[0x1F][CMD][EBV长度][data][CRC]）
//    - RequestAsyncCore 请求-响应模板（tcs + 响应缓冲区 + 超时 + 帧提取）
//    - Dispose 模板
//  子类只需实现设备特有逻辑（GATT / HID 报告 / 文件 IO）与发送方法。
//
//  本类不改变任何运行时行为：帧提取、CRC 校验逻辑、超时机制、状态机
//  与原三个实现逐字节一致，仅做代码物理位置迁移。
// =====================================================================

namespace DzPrinter.Transport;

/// <summary>
/// 传输层基类，提取 <see cref="IDeviceTransport"/> 实现中的公共状态机、
/// 协议帧提取与请求-响应模板逻辑。具体传输（BLE/HID/File）继承本类并实现
/// 设备特有逻辑。
/// </summary>
/// <remarks>
/// <para>本类提供以下公共能力：</para>
/// <list type="bullet">
///   <item>线程安全的连接状态与已连接设备字段（<see cref="_sync"/> 保护）</item>
///   <item><see cref="SetState"/> 状态切换 + 事件触发</item>
///   <item><see cref="TryExtractProtocolFrame"/> 协议帧提取（对应 JS SDK 帧解析）</item>
///   <item><see cref="RequestAsyncCore"/> 请求-响应模板（发送 + 累积 + 超时 + 帧提取）</item>
///   <item><see cref="Dispose(bool)"/> 统一释放模板</item>
/// </list>
/// </remarks>
public abstract class TransportBase : IDeviceTransport, IDisposable
{
    // ============ 受保护字段 ============

    /// <summary>状态与字段访问同步锁。</summary>
    protected readonly object _sync = new();

    /// <summary>当前连接状态。</summary>
    protected ConnectionState _state = ConnectionState.Disconnected;

    /// <summary>已连接的设备信息（null 表示未连接）。</summary>
    protected DeviceInfo? _connectedDevice;

    /// <summary>当前挂起的请求-响应等待句柄（无挂起请求时为 null）。</summary>
    protected TaskCompletionSource<byte[]>? _pendingResponse;

    /// <summary>
    /// 响应累积缓冲区：传输层可能分片到达，需累积到完整协议帧再交付。
    /// </summary>
    protected readonly List<byte> _responseBuffer = new();

    // ============ 公共属性 ============

    /// <inheritdoc />
    public ConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    /// <inheritdoc />
    public DeviceInfo? ConnectedDevice
    {
        get { lock (_sync) return _connectedDevice; }
        protected set { lock (_sync) _connectedDevice = value; }
    }

    /// <summary>本传输的类型标识。子类返回对应的 <see cref="TransportType"/> 枚举值。</summary>
    public abstract TransportType TransportType { get; }

    // ============ 公共事件 ============

    /// <inheritdoc />
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    // ============ 抽象方法（由子类实现设备特有逻辑） ============

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // ============ 请求-响应默认实现 ============

    /// <inheritdoc />
    /// <remarks>
    /// 默认实现通过 <see cref="RequestAsyncCore"/> 封装"发送 + 累积响应 + 超时 + 帧提取"模板。
    /// 子类如需不同行为（如 FileTransport 的模拟响应）可重写本方法。
    /// </remarks>
    public virtual async Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        return await RequestAsyncCore(data, timeoutMs, cancellationToken, SendAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// 请求-响应模板：发送数据 → 累积分片响应 → 提取完整协议帧 → 超时兜底。
    /// 对应 JS SDK 中 <c>requestMessage()</c> 的请求-响应模式。
    /// </summary>
    /// <param name="data">要发送的原始字节数据。</param>
    /// <param name="timeoutMs">超时毫秒数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="sendFunc">子类提供的发送委托（通常传入 <see cref="SendAsync"/>）。</param>
    /// <returns>完整协议帧字节数组；超时返回 null。</returns>
    protected async Task<byte[]?> RequestAsyncCore(ReadOnlyMemory<byte> data, int timeoutMs,
        CancellationToken cancellationToken, Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendFunc)
    {
        var tcs = new TaskCompletionSource<byte[]>();

        lock (_sync)
        {
            _responseBuffer.Clear();
            _pendingResponse = tcs;
            PrepareRequestBuffer();
        }

        await sendFunc(data, cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.Token.Register(() =>
        {
            byte[]? rawData = null;
            lock (_sync)
            {
                if (_responseBuffer.Count > 0)
                {
                    rawData = _responseBuffer.ToArray();
                    _responseBuffer.Clear();
                }
            }
            tcs.TrySetResult(rawData ?? Array.Empty<byte>());
        });
        cts.CancelAfter(timeoutMs);

        var result = await tcs.Task.ConfigureAwait(false);

        lock (_sync)
        {
            _pendingResponse = null;
            _responseBuffer.Clear();
        }
        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// 在 <see cref="RequestAsyncCore"/> 设置 _pendingResponse 后、发送数据前调用
    /// （已持有 <see cref="_sync"/> 锁）。子类可重写以清理特有缓冲区
    /// （如 HID 的 <c>_readBuffer</c>）。
    /// </summary>
    protected virtual void PrepareRequestBuffer() { }

    // ============ 受保护辅助方法 ============

    /// <summary>
    /// 从缓冲区提取一个完整的协议帧 [0x1F][CMD][EBV长度][data][CRC]。
    /// 找不到 0x1F 起始符时不清空缓冲区，保留数据等待后续通知补充。
    /// </summary>
    protected static byte[]? TryExtractProtocolFrame(List<byte> buffer)
    {
        int startIdx = -1;
        for (int i = 0; i < buffer.Count; i++)
        {
            if (buffer[i] == 0x1F) { startIdx = i; break; }
        }
        if (startIdx < 0) return null;
        if (startIdx > 0) buffer.RemoveRange(0, startIdx);

        if (buffer.Count < 4) return null;

        int dataOffset;
        int dataLength;
        if (buffer[2] >= 192)
        {
            if (buffer.Count < 5) return null;
            dataLength = ((buffer[2] & 0x3F) << 8) | buffer[3];
            dataOffset = 4;
        }
        else
        {
            dataLength = buffer[2];
            dataOffset = 3;
        }

        int totalLength = dataOffset + dataLength + 1;
        if (buffer.Count < totalLength) return null;

        var frame = buffer.GetRange(0, totalLength).ToArray();
        buffer.RemoveRange(0, totalLength);
        return frame;
    }

    /// <summary>
    /// 设置连接状态并触发 <see cref="ConnectionStateChanged"/> 事件。
    /// </summary>
    /// <param name="state">新的连接状态。</param>
    /// <param name="msg">可选的描述信息（如错误原因）。</param>
    protected void SetState(ConnectionState state, string? msg = null)
    {
        lock (_sync) _state = state;
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, msg));
    }

    /// <summary>
    /// 触发 <see cref="DataReceived"/> 事件。供子类在收到设备数据时调用。
    /// </summary>
    /// <param name="data">接收到的原始字节数据。</param>
    protected void RaiseDataReceived(byte[] data)
        => DataReceived?.Invoke(this, new DataReceivedEventArgs(data));

    // ============ Dispose 模板 ============

    /// <summary>
    /// 释放资源。默认调用 <see cref="DisconnectAsync"/> 同步等待断开。
    /// 子类如有非托管资源可重写本方法。
    /// </summary>
    /// <param name="disposing">true 表示由 Dispose 调用；false 表示由终结器调用。</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

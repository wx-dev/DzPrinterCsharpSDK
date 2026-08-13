using DzPrinter.Core;
using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  DeviceConnection（设备连接基类）。对应 JS SDK 中 <c>ai</c> 类。
//  JS 中 <c>ai</c> 是所有设备连接的基类，定义了连接/断开/发送数据等抽象行为，
//  派生类 <c>hi</c>（BleConnection）针对 BLE 实现具体逻辑。
//
//  C# 实现策略：
//   - JS 通过 uni-app 的 <c>uni.createBLEConnection</c>/<c>uni.writeBLECharacteristicValue</c>
//     等接口直接操作蓝牙；C# 中将这些平台相关操作抽象为 <see cref="IDeviceTransport"/>，
//     由宿主应用注入具体实现（WinRT BLE / CoreBluetooth / HidSharp 等）。
//   - 本类负责：连接状态机、设备信息缓存、事件分发、协议层数据收发协调。
//   - 事件命名与 JS 一致："deviceConnect"/"deviceDisconnect"/"deviceFound"/"dataReceived"
//     等，便于上层对照。
// =====================================================================

/// <summary>
/// 设备连接抽象基类。对应 JS SDK 中的 <c>ai</c>（DeviceConnection）类。
/// 包装 <see cref="IDeviceTransport"/> 提供统一的连接生命周期管理与事件分发。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS 中 <c>ai</c> 持有 <c>mAdapter</c>（BleAdapter）与
/// <c>mConnectionMap</c>（设备映射）；C# 中改为持有 <see cref="IDeviceTransport"/>
/// 实例，将平台差异下沉到传输层。</para>
/// <para><b>线程安全</b>：连接状态变更与发送操作使用锁保护。</para>
/// </remarks>
public abstract class DeviceConnection : IDisposable
{
    /// <summary>日志接口（共享 DzPrinter.Core 的统一日志）。</summary>
    protected static ILogger Log => DzLogger.Current;

    /// <summary>底层传输层实例。由派生类在构造时注入。</summary>
    protected IDeviceTransport Transport { get; }

    /// <summary>事件发射器。对应 JS <c>Ne</c>（LPAEmitter）。</summary>
    protected LpaEventEmitter Emitter { get; } = new();

    /// <summary>同步锁。</summary>
    protected readonly object SyncRoot = new();

    private PrintStatus _printStatus = PrintStatus.None;
    private DeviceInfo? _connectedDevice;
    private bool _disposed;

    /// <summary>
    /// 构造 DeviceConnection。对应 JS <c>ai.constructor(adapter)</c>。
    /// </summary>
    /// <param name="transport">传输层实现。</param>
    protected DeviceConnection(IDeviceTransport transport)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Transport.ConnectionStateChanged += OnTransportStateChanged;
        Transport.DataReceived += OnTransportDataReceived;
    }

    // ============ 属性 ============

    /// <summary>当前连接状态。对应 JS <c>mConnected</c>。</summary>
    public ConnectionState State
    {
        get { lock (SyncRoot) { return Transport.State; } }
    }

    /// <summary>当前打印状态。对应 JS <c>mPrintStatus</c>。</summary>
    public PrintStatus PrintStatus
    {
        get { lock (SyncRoot) { return _printStatus; } }
        internal set { lock (SyncRoot) { _printStatus = value; } }
    }

    /// <summary>已连接的设备信息。对应 JS <c>mConnectedDevice</c>。</summary>
    public DeviceInfo? ConnectedDevice
    {
        get { lock (SyncRoot) { return _connectedDevice; } }
        protected set { lock (SyncRoot) { _connectedDevice = value; } }
    }

    /// <summary>是否已连接。对应 JS <c>isConnected()</c>。</summary>
    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>设备类型。派生类重写以返回具体类型。</summary>
    public abstract LpaDeviceType DeviceType { get; }

    // ============ 事件 ============

    /// <summary>设备连接成功事件。对应 JS <c>"deviceConnect"</c>。</summary>
    public event Action<DeviceInfo>? DeviceConnected;

    /// <summary>设备断开事件。对应 JS <c>"deviceDisconnect"</c>。</summary>
    public event Action<DeviceInfo?, string?>? DeviceDisconnected;

    /// <summary>接收到设备数据事件。对应 JS <c>"dataReceived"</c>。</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>打印状态变化事件。对应 JS <c>"printStatusChanged"</c>。</summary>
    public event Action<PrintStatus>? PrintStatusChanged;

    // ============ 公共方法 ============

    /// <summary>
    /// 发现附近设备。对应 JS <c>discoverPrinters(options)</c>。
    /// 默认委托给 <see cref="Transport.DiscoverAsync"/>。
    /// </summary>
    public virtual async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        Log.Info($"【DeviceConnection】DiscoverAsync() —— DeviceType={DeviceType}");
        var devices = await Transport.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<PrinterDevice>(devices.Count);
        foreach (var d in devices)
        {
            result.Add(new PrinterDevice
            {
                DeviceId = d.DeviceId,
                Name = d.DeviceName,
                ModelName = LpaUtils.GetModelName(d.DeviceName),
                DeviceType = DeviceType,
            });
        }
        return result;
    }

    /// <summary>
    /// 连接到指定设备。对应 JS <c>connectDevice(options)</c>。
    /// </summary>
    /// <param name="device">目标设备。可传 <see cref="PrinterDevice"/> 或直接构造 <see cref="DeviceInfo"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public virtual async Task ConnectAsync(DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        Log.Info($"【DeviceConnection】ConnectAsync() —— device={device}");

        PrintStatus = PrintStatus.Connected;
        await Transport.ConnectAsync(device, cancellationToken).ConfigureAwait(false);
        ConnectedDevice = device;
        PrintStatus = PrintStatus.ReadyPrint;
        DeviceConnected?.Invoke(device);
    }

    /// <summary>
    /// 断开当前连接。对应 JS <c>disconnect(options)</c>。
    /// </summary>
    public virtual async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Log.Info($"【DeviceConnection】DisconnectAsync() —— wasConnected={IsConnected}");
        var prev = ConnectedDevice;
        try
        {
            await Transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ConnectedDevice = null;
            PrintStatus = PrintStatus.None;
            DeviceDisconnected?.Invoke(prev, null);
        }
    }

    /// <summary>
    /// 发送原始字节数据。对应 JS <c>sendData(data, options)</c>。
    /// </summary>
    public virtual async Task SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("设备未连接，无法发送数据。");
        Log.Debug($"【DeviceConnection】SendAsync() —— {data.Length} bytes");
        PrintStatus = PrintStatus.Sending;
        try
        {
            await Transport.SendAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PrintStatus = PrintStatus.ReadyPrint;
        }
    }

    /// <summary>
    /// 发送数据并等待响应。对应 JS <c>requestMessage(data, timeout)</c>。
    /// </summary>
    public virtual async Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("设备未连接，无法发送数据。");
        Log.Debug($"【DeviceConnection】RequestAsync() —— {data.Length} bytes, timeout={timeoutMs}ms");
        return await Transport.RequestAsync(data, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册事件监听。对应 JS <c>on(eventName, handler)</c>。
    /// 支持的事件名与 JS 一致：deviceConnect/deviceDisconnect/dataReceived/printStatusChanged。
    /// </summary>
    public void On(string eventName, Action<object?> handler) => Emitter.On(eventName, handler);

    /// <summary>
    /// 移除事件监听。对应 JS <c>off(eventName, handler)</c>。
    /// </summary>
    public void Off(string eventName, Action<object?> handler) => Emitter.Off(eventName, handler);

    // ============ 受保护方法 ============

    /// <summary>传输层连接状态变化回调。派生类可重写以添加自定义逻辑。</summary>
    protected virtual void OnTransportStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Log.Info($"【DeviceConnection】OnTransportStateChanged() —— state={e.State}, msg={e.Message}");
        if (e.State == ConnectionState.Disconnected || e.State == ConnectionState.Failed)
        {
            var prev = ConnectedDevice;
            ConnectedDevice = null;
            PrintStatus = PrintStatus.None;
            DeviceDisconnected?.Invoke(prev, e.Message);
        }
        Emitter.Emit("connectionStateChanged", e.State);
    }

    /// <summary>传输层数据接收回调。派生类可重写以解析协议帧。</summary>
    protected virtual void OnTransportDataReceived(object? sender, DataReceivedEventArgs e)
    {
        Log.Debug($"【DeviceConnection】OnTransportDataReceived() —— {e.Data.Length} bytes");
        DataReceived?.Invoke(e.Data);
        Emitter.Emit("dataReceived", e.Data);
    }

    /// <summary>触发打印状态变化事件。</summary>
    protected void RaisePrintStatusChanged(PrintStatus newStatus)
    {
        PrintStatus = newStatus;
        PrintStatusChanged?.Invoke(newStatus);
        Emitter.Emit("printStatusChanged", newStatus);
    }

    // ============ IDisposable ============

    /// <summary>释放资源。</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>释放资源。对应 JS <c>quit()</c>。</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            Transport.ConnectionStateChanged -= OnTransportStateChanged;
            Transport.DataReceived -= OnTransportDataReceived;
            try { Transport.DisconnectAsync().GetAwaiter().GetResult(); }
            catch { /* 忽略释放异常 */ }
        }
        _disposed = true;
    }
}

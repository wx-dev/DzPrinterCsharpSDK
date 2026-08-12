using DzPrinter.Core;
using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  DeviceManager（设备管理器）。对应 JS SDK 中 <c>ui</c> 类。
//  JS 中 <c>ui</c> 管理所有设备连接的生命周期：
//    - 设备扫描（discoverPrinters）
//    - 连接/断开管理
//    - 多设备并发连接支持
//    - 连接状态全局监听
//    - 设备类型自动识别（BLE/HID）
//
//  C# 实现策略：
//   - 维护一个连接字典（deviceId → DeviceConnection）
//   - 通过注入的传输层工厂创建连接
//   - 提供单设备连接的便捷方法（connect/disconnect）
//   - 支持多设备并发连接
// =====================================================================

/// <summary>
/// 设备管理器。对应 JS SDK 中的 <c>ui</c>（DeviceManager）类。
/// 管理设备发现、连接、断开等生命周期。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>ui</c> 持有 <c>mConnectionMap</c>（设备连接映射），
/// 提供 <c>discoverPrinters</c>/<c>connectDevice</c>/<c>disconnect</c> 等方法。</para>
/// <para><b>多设备支持</b>：JS SDK 支持同时连接多台打印机；C# 同样支持，
/// 通过 <see cref="GetConnection"/> 获取指定设备的连接实例。</para>
/// </remarks>
public sealed class DeviceManager : IDisposable
{
    /// <summary>日志接口。</summary>
    private static ILogger Log => DzLogger.Current;

    /// <summary>传输层工厂委托。根据设备类型创建对应的传输层实例。</summary>
    private readonly Func<LpaDeviceType, IDeviceTransport> _transportFactory;

    /// <summary>连接字典。deviceId → DeviceConnection。</summary>
    private readonly Dictionary<string, DeviceConnection> _connections = new();

    /// <summary>同步锁。</summary>
    private readonly object _syncRoot = new();

    /// <summary>当前活跃的连接类型。对应 JS <c>mCurrentDeviceType</c>。</summary>
    private LpaDeviceType _activeDeviceType = LpaDeviceType.Auto;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>
    /// 构造 DeviceManager。对应 JS <c>ui.constructor(options)</c>。
    /// </summary>
    /// <param name="transportFactory">
    /// 传输层工厂委托。根据设备类型返回对应的 <see cref="IDeviceTransport"/> 实例。
    /// 宿主应用需提供具体实现（如 WinRT BLE / HidSharp 等）。
    /// </param>
    public DeviceManager(Func<LpaDeviceType, IDeviceTransport> transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        Log.Info("【DeviceManager】constructor() —— transportFactory injected");
    }

    // ============ 事件 ============

    /// <summary>发现新设备事件。对应 JS <c>"deviceFound"</c>。</summary>
    public event Action<PrinterDevice>? DeviceFound;

    /// <summary>设备连接状态变化事件。对应 JS <c>"connectionStateChanged"</c>。</summary>
    public event Action<DeviceInfo?, ConnectionState>? ConnectionStateChanged;

    // ============ 属性 ============

    /// <summary>当前活跃连接数。</summary>
    public int ConnectionCount
    {
        get { lock (_syncRoot) { return _connections.Count; } }
    }

    /// <summary>当前活跃的设备类型。</summary>
    public LpaDeviceType ActiveDeviceType => _activeDeviceType;

    /// <summary>所有已连接设备的信息列表。</summary>
    public IReadOnlyList<DeviceInfo> ConnectedDevices
    {
        get
        {
            lock (_syncRoot)
            {
                return _connections.Values
                    .Where(c => c.IsConnected && c.ConnectedDevice != null)
                    .Select(c => c.ConnectedDevice!)
                    .ToList();
            }
        }
    }

    // ============ 公共方法 ============

    /// <summary>
    /// 发现附近支持的打印机。对应 JS <c>discoverPrinters(options)</c>。
    /// </summary>
    /// <param name="deviceType">设备类型（Auto 表示自动检测）。</param>
    /// <param name="filterSupported">是否仅返回德佟支持的机型。默认 true。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(
        LpaDeviceType deviceType = LpaDeviceType.Auto,
        bool filterSupported = true,
        CancellationToken cancellationToken = default)
    {
        Log.Info($"【DeviceManager】DiscoverAsync() —— type={deviceType}, filterSupported={filterSupported}");

        // 确定要扫描的设备类型列表
        var typesToScan = deviceType == LpaDeviceType.Auto
            ? new[] { LpaDeviceType.WebBle, LpaDeviceType.WebHid }
            : new[] { deviceType };

        var allDevices = new List<PrinterDevice>();
        foreach (var type in typesToScan)
        {
            try
            {
                var transport = _transportFactory(type);
                var connection = CreateConnection(type, transport);
                var devices = await connection.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                allDevices.AddRange(devices);
                // 发现后释放临时连接
                connection.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"【DeviceManager】DiscoverAsync() —— {type} 扫描失败: {ex.Message}");
            }
        }

        // 去重（按 DeviceId）
        var distinct = allDevices
            .GroupBy(d => d.DeviceId)
            .Select(g => g.First())
            .ToList();

        // 过滤支持的机型
        var result = filterSupported
            ? SupportPrinterMatcher.FilterSupported(distinct).ToList()
            : distinct;

        Log.Info($"【DeviceManager】DiscoverAsync() —— found {result.Count} supported printer(s)");

        // 触发设备发现事件
        foreach (var d in result) DeviceFound?.Invoke(d);

        return result;
    }

    /// <summary>
    /// 连接到指定设备。对应 JS <c>connectDevice(options)</c>。
    /// </summary>
    /// <param name="device">目标设备。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接实例。</returns>
    public async Task<DeviceConnection> ConnectAsync(PrinterDevice device,
        CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        Log.Info($"【DeviceManager】ConnectAsync() —— device={device}");

        // 若已有同设备连接，先断开
        await DisconnectAsync(device.DeviceId, cancellationToken).ConfigureAwait(false);

        // 创建新连接
        var transport = _transportFactory(device.DeviceType);
        var connection = CreateConnection(device.DeviceType, transport);

        var info = new DeviceInfo
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            TransportType = device.DeviceType == LpaDeviceType.WebBle
                ? TransportType.BluetoothLowEnergy
                : TransportType.HidUsb,
            Dpi = LpaUtils.GetDeviceDPI(device.ModelName),
        };

        // 订阅状态变化
        connection.DeviceConnected += OnDeviceConnected;
        connection.DeviceDisconnected += OnDeviceDisconnected;

        await connection.ConnectAsync(info, cancellationToken).ConfigureAwait(false);

        lock (_syncRoot)
        {
            _connections[device.DeviceId] = connection;
            _activeDeviceType = device.DeviceType;
        }

        return connection;
    }

    /// <summary>
    /// 断开指定设备。对应 JS <c>disconnect(options)</c>。
    /// </summary>
    public async Task DisconnectAsync(string deviceId,
        CancellationToken cancellationToken = default)
    {
        DeviceConnection? conn;
        lock (_syncRoot)
        {
            if (!_connections.TryGetValue(deviceId, out conn)) return;
            _connections.Remove(deviceId);
        }

        if (conn != null)
        {
            conn.DeviceConnected -= OnDeviceConnected;
            conn.DeviceDisconnected -= OnDeviceDisconnected;
            await conn.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            conn.Dispose();
        }
    }

    /// <summary>
    /// 断开所有设备。对应 JS <c>disconnectAll()</c>。
    /// </summary>
    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        List<DeviceConnection> conns;
        lock (_syncRoot)
        {
            conns = _connections.Values.ToList();
            _connections.Clear();
        }

        foreach (var conn in conns)
        {
            try
            {
                conn.DeviceConnected -= OnDeviceConnected;
                conn.DeviceDisconnected -= OnDeviceDisconnected;
                await conn.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                conn.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"【DeviceManager】DisconnectAllAsync() —— 断开异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取指定设备的连接实例。对应 JS <c>getConnection(deviceId)</c>。
    /// </summary>
    public DeviceConnection? GetConnection(string deviceId)
    {
        lock (_syncRoot)
        {
            return _connections.TryGetValue(deviceId, out var conn) ? conn : null;
        }
    }

    /// <summary>
    /// 获取当前唯一活跃连接（若只有一个连接）。对应 JS <c>getActiveConnection()</c>。
    /// </summary>
    public DeviceConnection? GetActiveConnection()
    {
        lock (_syncRoot)
        {
            return _connections.Values.FirstOrDefault(c => c.IsConnected);
        }
    }

    // ============ 私有方法 ============

    /// <summary>根据设备类型创建对应的连接实例。</summary>
    private static DeviceConnection CreateConnection(LpaDeviceType type, IDeviceTransport transport) =>
        type switch
        {
            LpaDeviceType.WebBle => new BleConnection(transport),
            LpaDeviceType.WebHid => new HidConnection(transport),
            _ => new BleConnection(transport)
        };

    private void OnDeviceConnected(DeviceInfo device)
    {
        Log.Info($"【DeviceManager】OnDeviceConnected() —— {device}");
        ConnectionStateChanged?.Invoke(device, ConnectionState.Connected);
    }

    private void OnDeviceDisconnected(DeviceInfo? device, string? reason)
    {
        Log.Info($"【DeviceManager】OnDeviceDisconnected() —— {device}, reason={reason}");
        ConnectionStateChanged?.Invoke(device, ConnectionState.Disconnected);
    }

    // ============ IDisposable ============

    /// <summary>释放资源。对应 JS <c>quit()</c>。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        DisconnectAllAsync().GetAwaiter().GetResult();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

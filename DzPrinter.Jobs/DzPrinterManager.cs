// =====================================================================
//  DzPrinterManager：打印管理器（Facade）。
//
//  对应 JS SDK 中对外最高层入口：发现设备 → 连接 → 绘制作业 → 打印。
//  与 Printer 模块中的 LPAPI 差异：
//    - LPAPI 绑定 DeviceConnection，语义更贴近 JS uni-app SDK。
//    - DzPrinterManager 是"现代 C# Facade"，直接包装 IDeviceTransport + DrawContext。
//  二者功能等价，长期可以合并。
// =====================================================================

using DzPrinter.Core;
using DzPrinter.Transport;
using ILogger = DzPrinter.Core.ILogger;

namespace DzPrinter.Jobs;

/// <summary>打印结果。与 Printer 模块 LpaResult 类似但不直接耦合。</summary>
public enum PrintJobResult
{
    Ok = 0,
    ErrorNoPrinter = -1,
    ErrorParam = -2,
    ErrorDataSendError = -3,
    ErrorEncode = -4,
}

/// <summary>
/// 高层打印管理器。
/// <para>
/// 典型用法：
/// <code>
/// using var manager = new DzPrinterManager(new WinRtBleTransport());
/// var devices = await manager.DiscoverAsync();
/// await manager.ConnectAsync(devices[0]);
/// using var ctx = manager.CreateDrawContext(new DrawJobOptions { WidthMm=60, HeightMm=40 });
/// ctx.Start();
/// ctx.Canvas.DrawText("Hello", 5, 5, 10);
/// await manager.PrintAsync(ctx);
/// </code>
/// </para>
/// </summary>
public sealed class DzPrinterManager : IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly IDeviceTransport _transport;
    private DeviceInfo? _connected;
    private bool _disposed;

    public DzPrinterManager(IDeviceTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _transport.ConnectionStateChanged += OnState;
    }

    /// <summary>底层传输层。</summary>
    public IDeviceTransport Transport => _transport;

    /// <summary>当前连接的设备。</summary>
    public DeviceInfo? ConnectedDevice => _connected;

    /// <summary>是否已连接。</summary>
    public bool IsConnected => _transport.State == ConnectionState.Connected;

    /// <summary>发现设备。</summary>
    public Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(
        CancellationToken ct = default)
    {
        Log.Info("【DzPrinterManager】DiscoverAsync()");
        return _transport.DiscoverAsync(ct);
    }

    /// <summary>连接到指定设备。</summary>
    public async Task ConnectAsync(DeviceInfo device, CancellationToken ct = default)
    {
        Log.Info($"【DzPrinterManager】ConnectAsync({device})");
        await _transport.ConnectAsync(device, ct).ConfigureAwait(false);
        _connected = device;
    }

    /// <summary>断开连接。</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        Log.Info("【DzPrinterManager】DisconnectAsync()");
        try { await _transport.DisconnectAsync(ct).ConfigureAwait(false); }
        finally { _connected = null; }
    }

    /// <summary>创建绘制作业上下文。</summary>
    public DrawContext CreateDrawContext(DrawJobOptions options) => new(options);

    /// <summary>
    /// 发送打印作业。
    /// </summary>
    /// <param name="context">已 Start/Commit 过的 DrawContext。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<PrintJobResult> PrintAsync(DrawContext context,
        CancellationToken ct = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!IsConnected)
        {
            Log.Warn("【DzPrinterManager】PrintAsync() —— 未连接");
            return PrintJobResult.ErrorNoPrinter;
        }
        try
        {
            var chunks = context.EncodeChunks();
            Log.Info($"【DzPrinterManager】PrintAsync() —— {chunks.Count} 个分片，" +
                     $"共 {chunks.Sum(c => c.Length)} 字节");
            foreach (var chunk in chunks)
            {
                await _transport.SendAsync(chunk, ct).ConfigureAwait(false);
            }
            return PrintJobResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【DzPrinterManager】PrintAsync() 失败: {ex.Message}");
            return PrintJobResult.ErrorDataSendError;
        }
    }

    /// <summary>发送原始字节数据。</summary>
    public async Task<PrintJobResult> SendRawAsync(byte[] data,
        CancellationToken ct = default)
    {
        if (!IsConnected) return PrintJobResult.ErrorNoPrinter;
        try
        {
            await _transport.SendAsync(data, ct).ConfigureAwait(false);
            return PrintJobResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【DzPrinterManager】SendRawAsync() 失败: {ex.Message}");
            return PrintJobResult.ErrorDataSendError;
        }
    }

    private void OnState(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.State == ConnectionState.Disconnected ||
            e.State == ConnectionState.Failed)
        {
            _connected = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _transport.ConnectionStateChanged -= OnState;
        try { _transport.DisconnectAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
        _disposed = true;
    }
}

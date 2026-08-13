using DzPrinter.Core;
using DzPrinter.Printer;
using DzPrinter.Transport;
using ILogger = DzPrinter.Core.ILogger;

namespace DzPrinter.Jobs;

/// <summary>
/// 高层打印管理器。内部委托 <see cref="LPAPI"/> 实现设备发现/连接/发送，
/// 在此之上提供 <see cref="DrawContext"/> 作业管理。
/// </summary>
public sealed class DzPrinterManager : IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly IDeviceTransport _transport;
    private readonly LPAPI _lpapi;
    private bool _disposed;

    public DzPrinterManager(IDeviceTransport transport, PrinterInfo? printerInfo = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _lpapi = new LPAPI(_ => transport, printerInfo);
    }

    /// <summary>底层传输层。</summary>
    public IDeviceTransport Transport => _transport;

    /// <summary>内部 LPAPI 实例。</summary>
    public LPAPI Api => _lpapi;

    /// <summary>是否已连接。</summary>
    public bool IsConnected => _lpapi.IsConnected;

    /// <summary>当前连接的设备。</summary>
    public DeviceInfo? ConnectedDevice => _lpapi.ConnectedDevice;

    /// <summary>发现设备。</summary>
    public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(
        LpaDeviceType deviceType = LpaDeviceType.Auto,
        CancellationToken ct = default) =>
        _lpapi.DiscoverAsync(deviceType, ct);

    /// <summary>连接到指定设备。</summary>
    public Task<LpaResult> ConnectAsync(PrinterDevice device,
        CancellationToken ct = default) =>
        _lpapi.ConnectAsync(device, ct);

    /// <summary>断开连接。</summary>
    public Task DisconnectAsync(CancellationToken ct = default) =>
        _lpapi.DisconnectAsync(ct);

    /// <summary>创建绘制作业上下文。</summary>
    public DrawContext CreateDrawContext(DrawJobOptions options) => new(options);

    /// <summary>
    /// 发送打印作业。
    /// </summary>
    public async Task<LpaResult> PrintAsync(DrawContext context,
        CancellationToken ct = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        var conn = _lpapi.Connection;
        if (conn == null || !conn.IsConnected)
        {
            Log.Warn("【DzPrinterManager】PrintAsync() —— 未连接");
            return LpaResult.ErrorNoPrinter;
        }
        try
        {
            var chunks = context.EncodeChunks();
            Log.Info($"【DzPrinterManager】PrintAsync() —— {chunks.Count} 个分片，" +
                     $"共 {chunks.Sum(c => c.Length)} 字节");
            foreach (var chunk in chunks)
            {
                await conn.SendAsync(chunk, ct).ConfigureAwait(false);
            }
            return LpaResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【DzPrinterManager】PrintAsync() 失败: {ex.Message}");
            return LpaResult.ErrorDataSendError;
        }
    }

    /// <summary>发送原始字节数据。</summary>
    public Task<LpaResult> SendRawAsync(byte[] data,
        CancellationToken ct = default) =>
        _lpapi.SendRawDataAsync(data, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _lpapi.Dispose();
        _disposed = true;
    }
}

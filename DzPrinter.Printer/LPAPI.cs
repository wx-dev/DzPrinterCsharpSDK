using DzPrinter.Core;
using DzPrinter.Drawing;
using DzPrinter.Imaging;
using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  LPAPI（主 API 入口）。对应 JS SDK 中 <c>Ri</c> 类。
//  JS 中 <c>Ri</c> 是整个 SDK 对外暴露的主接口，聚合：
//    - DeviceManager（设备管理与连接）
//    - PrinterCanvas / PrinterCanvasMm（绘图画布）
//    - PrintEncoder（图像 → 协议帧编码）
//    - PrinterInfo（打印参数：浓度/速度/间隙/份数等）
//
//  上层应用通过 LPAPI 完成：
//    1. 扫描/连接打印机
//    2. 在画布上绘制文本/条码/图片/图形
//    3. 将画布转为图像数据并编码为打印指令
//    4. 通过设备连接发送指令到打印机
//
//  C# 实现策略：
//   - 保持与 JS 一致的 API 表面（方法名/参数/返回值语义）
//   - 异步方法使用 Task，对应 JS Promise
//   - 设备连接由 DeviceManager 管理
//   - 画布由 PrinterCanvas / PrinterCanvasMm 提供
//   - 协议编码委托给 PrintEncoder
// =====================================================================

/// <summary>
/// LPAPI 主入口。对应 JS SDK 中的 <c>Ri</c>（LPAPI）类。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>Ri</c> 是整个 SDK 的门面类，聚合设备管理、画布、编码、
/// 打印参数等子模块。上层应用通过 <c>LPAPIFactory.getInstance()</c> 获取单例。</para>
/// <para><b>异步模型</b>：JS 返回 Promise；C# 返回 <see cref="Task{T}"/>。</para>
/// <para><b>画布管理</b>：JS 中画布由 <c>DrawContext</c>（uni-app Canvas 上下文）驱动；
/// C# 中直接使用 <see cref="PrinterCanvas"/>（基于 SkiaSharp），无需宿主 Canvas。</para>
/// </remarks>
public sealed class LPAPI : IDisposable
{
    private static ILogger Log => DzLogger.Current;

    /// <summary>设备管理器。</summary>
    public DeviceManager DeviceManager { get; }

    /// <summary>打印参数。对应 JS <c>mPrinterInfo</c>。</summary>
    public PrinterInfo PrinterInfo { get; } = new();

    /// <summary>当前画布（毫米单位）。对应 JS <c>mCanvas</c>。</summary>
    public PrinterCanvasMm? Canvas { get; private set; }

    /// <summary>当前连接。对应 JS <c>mConnection</c>。</summary>
    public DeviceConnection? Connection => DeviceManager.GetActiveConnection();

    /// <summary>是否已连接。对应 JS <c>isConnected()</c>。</summary>
    public bool IsConnected => Connection?.IsConnected ?? false;

    /// <summary>当前已连接设备。对应 JS <c>getConnectedDevice()</c>。</summary>
    public DeviceInfo? ConnectedDevice => Connection?.ConnectedDevice;

    private bool _disposed;

    /// <summary>
    /// 构造 LPAPI。对应 JS <c>Ri.create(options)</c>。
    /// </summary>
    /// <param name="transportFactory">传输层工厂。</param>
    /// <param name="printerInfo">打印参数（可选）。</param>
    public LPAPI(Func<LpaDeviceType, IDeviceTransport> transportFactory,
        PrinterInfo? printerInfo = null)
    {
        DeviceManager = new DeviceManager(transportFactory);
        if (printerInfo != null)
        {
            PrinterInfo.PrinterWidth = printerInfo.PrinterWidth;
            PrinterInfo.PrinterDpi = printerInfo.PrinterDpi;
            PrinterInfo.GapType = printerInfo.GapType;
            PrinterInfo.GapLength = printerInfo.GapLength;
            PrinterInfo.Darkness = printerInfo.Darkness;
            PrinterInfo.Speed = printerInfo.Speed;
            PrinterInfo.PageCount = printerInfo.PageCount;
        }
        Log.Info($"【LPAPI】constructor() —— printerWidth={PrinterInfo.PrinterWidth}, dpi={PrinterInfo.PrinterDpi}");
    }

    // ============ 设备发现与连接 ============

    /// <summary>
    /// 发现附近支持的打印机。对应 JS <c>discoverPrinters(options)</c>。
    /// </summary>
    public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(
        LpaDeviceType deviceType = LpaDeviceType.Auto,
        CancellationToken cancellationToken = default) =>
        DeviceManager.DiscoverAsync(deviceType, true, cancellationToken);

    /// <summary>
    /// 连接到指定设备。对应 JS <c>connectDevice(options)</c>。
    /// </summary>
    public async Task<LpaResult> ConnectAsync(PrinterDevice device,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await DeviceManager.ConnectAsync(device, cancellationToken).ConfigureAwait(false);
            return LpaResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【LPAPI】ConnectAsync() 失败: {ex.Message}");
            return LpaResult.ErrorConnectFailed;
        }
    }

    /// <summary>
    /// 断开当前连接。对应 JS <c>disconnect(options)</c>。
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var conn = Connection;
        if (conn?.ConnectedDevice != null)
            await DeviceManager.DisconnectAsync(conn.ConnectedDevice.DeviceId, cancellationToken)
                .ConfigureAwait(false);
    }

    // ============ 画布管理 ============

    /// <summary>
    /// 创建新画布。对应 JS <c>createCanvas(options)</c>。
    /// </summary>
    /// <param name="widthMm">画布宽度（毫米）。</param>
    /// <param name="heightMm">画布高度（毫米）。</param>
    /// <param name="orientation">旋转方向：0=横向, 1=纵向。</param>
    public PrinterCanvasMm CreateCanvas(double widthMm, double heightMm, int orientation = 0)
    {
        Log.Info($"【LPAPI】CreateCanvas() —— {widthMm}x{heightMm}mm, orientation={orientation}");
        Canvas = new PrinterCanvasMm();
        Canvas.Dpi = PrinterInfo.PrinterDpi;
        Canvas.StartJob(new DrawOptions
        {
            Width = widthMm,
            Height = heightMm,
            Orientation = orientation,
            PrinterWidth = PrinterInfo.PrinterWidth,
            Dpi = PrinterInfo.PrinterDpi,
        });
        return Canvas;
    }

    /// <summary>
    /// 获取当前画布图像数据。对应 JS <c>getImageData()</c>。
    /// </summary>
    public DzImageData GetImageData()
    {
        if (Canvas == null)
            throw new InvalidOperationException("画布未创建，请先调用 CreateCanvas。");
        return Canvas.GetImageData();
    }

    // ============ 打印 ============

    /// <summary>
    /// 将当前画布内容编码为打印指令分片。对应 JS <c>encodeImageData()</c>。
    /// </summary>
    public List<byte[]> EncodePrintData()
    {
        if (Canvas == null)
            throw new InvalidOperationException("画布未创建，无法编码。");

        var imageData = Canvas.GetImageData();
        var options = BuildPrintOptions(imageData);
        return PrintEncoder.EncodeImageData(imageData, options);
    }

    /// <summary>
    /// 打印当前画布内容。对应 JS <c>printImage(options)</c>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LpaResult> PrintAsync(CancellationToken cancellationToken = default)
    {
        var conn = Connection;
        if (conn == null || !conn.IsConnected)
        {
            Log.Warn("【LPAPI】PrintAsync() —— 设备未连接");
            return LpaResult.ErrorNoPrinter;
        }

        if (Canvas == null)
        {
            Log.Warn("【LPAPI】PrintAsync() —— 画布未创建");
            return LpaResult.ErrorParam;
        }

        try
        {
            var chunks = EncodePrintData();
            Log.Info($"【LPAPI】PrintAsync() —— 发送 {chunks.Count} 个分片，共 {chunks.Sum(c => c.Length)} 字节");

            conn.PrintStatus = PrintStatus.Printing;
            try
            {
                foreach (var chunk in chunks)
                {
                    await conn.SendAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                conn.PrintStatus = PrintStatus.ReadyPrint;
            }
            return LpaResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【LPAPI】PrintAsync() 失败: {ex.Message}");
            return LpaResult.ErrorDataSendError;
        }
    }

    /// <summary>
    /// 发送原始字节数据。对应 JS <c>sendData(data, options)</c>。
    /// </summary>
    public async Task<LpaResult> SendRawDataAsync(byte[] data,
        CancellationToken cancellationToken = default)
    {
        var conn = Connection;
        if (conn == null || !conn.IsConnected) return LpaResult.ErrorNoPrinter;
        try
        {
            await conn.SendAsync(data, cancellationToken).ConfigureAwait(false);
            return LpaResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Error($"【LPAPI】SendRawDataAsync() 失败: {ex.Message}");
            return LpaResult.ErrorDataSendError;
        }
    }

    // ============ 状态查询 ============

    /// <summary>
    /// 查询打印机可打印状态。对应 JS <c>getPrintableStatus(options)</c>。
    /// 发送 <see cref="PrinterCommand.CMD_IS_PRINTABLE"/> 查询帧并解析响应。
    /// </summary>
    public async Task<PrinterStatusCode> GetPrintableStatusAsync(int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        var conn = Connection;
        if (conn == null || !conn.IsConnected) return PrinterStatusCode.DZIP_ENVNOTREADY;

        // 构建查询帧：仅 CMD，无 payload。对应 JS 中的状态查询命令。
        var requestFrame = new ProtocolPacket(PrinterCommand.CMD_IS_PRINTABLE).GetBytes();
        var response = await conn.RequestAsync(requestFrame, timeoutMs, cancellationToken)
            .ConfigureAwait(false);

        // 设备返回原始协议帧，需剥离帧头提取 payload
        var payload = EbvHelper.TryGetPayload(response);
        if (payload == null || payload.Length < 1)
            return PrinterStatusCode.DZIP_ENVNOTREADY;

        return (PrinterStatusCode)payload[0];
    }

    /// <summary>
    /// 查询打印机硬件信息。对应 JS <c>loadPrinterInfo()</c>。
    /// 逐个发送 DPI / 宽度 / 硬件标志 / 电池 / 缓冲区查询命令并解析响应。
    /// 注意：JS SDK 中 <see cref="PrinterCommand.CMD_DEV_HANDSHAKE"/> 是空操作，不使用。
    /// </summary>
    public async Task<PrinterHardwareInfo?> GetPrinterInfoAsync(int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        var conn = Connection;
        if (conn == null || !conn.IsConnected) return null;

        var info = new PrinterHardwareInfo();
        var queryTimeout = Math.Min(timeoutMs, 1500);
        var gotAny = false;

        // 1. DPI (CMD_PRINTER_DPI = 0x71) — 无参数，响应可能 1 或 2 字节
        var dpiPayload = await TryRequestPayloadAsync(conn, PrinterCommand.CMD_PRINTER_DPI,
            queryTimeout, cancellationToken).ConfigureAwait(false);
        if (dpiPayload != null && dpiPayload.Length >= 1)
        {
            info.Dpi = dpiPayload.Length >= 2
                ? EbvHelper.ReadUInt16BigEndian(dpiPayload)
                : dpiPayload[0];
            gotAny = true;
            Log.Info($"【LPAPI】GetPrinterInfoAsync() —— DPI={info.Dpi}");
        }

        // 2. 打印宽度 (CMD_PRINTER_WIDTH = 0x72) — 无参数，响应 [widthHi, widthLo, ...]
        var widthPayload = await TryRequestPayloadAsync(conn, PrinterCommand.CMD_PRINTER_WIDTH,
            queryTimeout, cancellationToken).ConfigureAwait(false);
        if (widthPayload != null && widthPayload.Length >= 2)
        {
            info.PrinterWidth = (widthPayload[0] << 8) | widthPayload[1];
            gotAny = true;
            Log.Info($"【LPAPI】GetPrinterInfoAsync() —— PrinterWidth={info.PrinterWidth}");
        }

        // 3. 硬件标志 + 软件标志 + 电池数量 (CMD_HARDWARE_FLAGS = 0x84)
        // JS: 先 CMD_ENABLE_SETTING[~0x80] 启用设置，再 CMD_HARDWARE_FLAGS[1] 查询
        // 必须逐条发送+等待响应，否则多个响应帧在同一通知到达时传输层只提取第一帧
        var enableData = unchecked((byte)~(byte)PrinterCommand.CMD_ENABLE_SETTING); // 0x7F
        var enableFrame = new ProtocolPacket(PrinterCommand.CMD_ENABLE_SETTING, [enableData]).GetBytes();
        await conn.RequestAsync(enableFrame, queryTimeout, cancellationToken)
            .ConfigureAwait(false); // 启用设置，响应丢弃

        var flagsQueryFrame = new ProtocolPacket(PrinterCommand.CMD_HARDWARE_FLAGS, [1]).GetBytes();
        var flagsResp = await conn.RequestAsync(flagsQueryFrame, queryTimeout, cancellationToken)
            .ConfigureAwait(false);
        var flagsPayload = EbvHelper.TryGetPayload(flagsResp);
        if (flagsPayload != null && flagsPayload.Length >= 4)
        {
            info.HardwareFlags = (HardwareFlags)(
                (flagsPayload[0] << 24) | (flagsPayload[1] << 16) |
                (flagsPayload[2] << 8) | flagsPayload[3]);
            if (flagsPayload.Length >= 8)
            {
                info.SoftwareFlags = (SoftwareFlags)(
                    (flagsPayload[4] << 24) | (flagsPayload[5] << 16) |
                    (flagsPayload[6] << 8) | flagsPayload[7]);
            }
            else
            {
                // JS 默认值: PCPDSF_MOTOR_ANTIDIR | PCPDSF_PRTA_RIGHT | (hwFlags & PCPDSF_RLE5_BITMAP)
                info.SoftwareFlags = SoftwareFlags.PCPDSF_MOTOR_ANTIDIR |
                    (SoftwareFlags)((uint)info.HardwareFlags & (uint)SoftwareFlags.PCPDSF_RLE5_BITMAP);
            }
            info.BatteryCount = flagsPayload.Length > 20 ? flagsPayload[20] & 0xFF : 2;
            gotAny = true;
            Log.Info($"【LPAPI】GetPrinterInfoAsync() —— HardwareFlags={info.HardwareFlags}, " +
                     $"SoftwareFlags={info.SoftwareFlags}, BatteryCount={info.BatteryCount}");
        }
        // 4. 电池详细信息 (CMD_REQ_ADCVALUE = 0x88, data=[ADCEVT_POWER=0x01])
        // 必须在设置模式仍启用时发送（禁用前），否则设备不响应
        // 响应帧 CMD=0x40, payload: [valid, batteryCount, voltLo, voltHi, voltCount, chargeStatus, printable]
        var batteryFrame = new ProtocolPacket(PrinterCommand.CMD_REQ_ADCVALUE,
            [(byte)AdcEvent.ADCEVT_POWER]).GetBytes();
        var batteryResp = await conn.RequestAsync(batteryFrame, queryTimeout, cancellationToken)
            .ConfigureAwait(false);
        var batteryPayload = EbvHelper.TryGetPayload(batteryResp);
        // JS SDK CMD_REQ_ADCVALUE + ADCEVT_POWER 响应格式（与 CMD_0x40 不同！）：
        //   e[0] = 事件类型 (ADCEVT_POWER = 1)
        //   e[7] = 电压高字节, e[8] = 电压低字节 → toShort(e[8], e[7])
        //   e[10] = 充电状态 (> 0 = 充电中)
        //   e[12] = 电池电量 (非零时有效)
        if (batteryPayload != null && batteryPayload.Length > 0 &&
            batteryPayload[0] == (byte)AdcEvent.ADCEVT_POWER)
        {
            if (batteryPayload.Length > 8)
            {
                // toShort(e[8], e[7]) = (e[7] << 8) | e[8]
                info.BatteryVoltage = 0.01 * ((batteryPayload[7] << 8) | batteryPayload[8]);
            }
            if (batteryPayload.Length > 10)
            {
                info.ChargeStatus = batteryPayload[10] > 0;
            }
            gotAny = true;
            Log.Info($"【LPAPI】GetPrinterInfoAsync() —— BatteryVoltage={info.BatteryVoltage}V, " +
                     $"ChargeStatus={info.ChargeStatus}");
        }
        // 禁用设置模式（响应丢弃）—— 必须在电池查询之后
        var disableFrame = new ProtocolPacket(PrinterCommand.CMD_ENABLE_SETTING, [0]).GetBytes();
        await conn.RequestAsync(disableFrame, queryTimeout, cancellationToken)
            .ConfigureAwait(false);

        // 5. 缓冲区大小 (CMD_BUFFER_SIZE = 0x77) — 无参数，响应 popEBV()
        var bufPayload = await TryRequestPayloadAsync(conn, PrinterCommand.CMD_BUFFER_SIZE,
            queryTimeout, cancellationToken).ConfigureAwait(false);
        if (bufPayload != null && bufPayload.Length >= 1)
        {
            int bufVal = bufPayload.Length >= 2 && bufPayload[0] >= ProtocolConstants.EbvThreshold
                ? EbvHelper.ToEbv(bufPayload[1], bufPayload[0])
                : bufPayload[0];
            info.BufferSize = 500 * (bufVal == 1 ? 2 : bufVal);
            gotAny = true;
            Log.Info($"【LPAPI】GetPrinterInfoAsync() —— BufferSize={info.BufferSize}");
        }

        return gotAny ? info : null;
    }

    /// <summary>
    /// 发送单条查询命令并提取 payload。
    /// </summary>
    private async Task<byte[]?> TryRequestPayloadAsync(DeviceConnection conn,
        PrinterCommand cmd, int timeoutMs, CancellationToken cancellationToken)
    {
        var frame = new ProtocolPacket(cmd).GetBytes();
        var resp = await conn.RequestAsync(frame, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
        return EbvHelper.TryGetPayload(resp);
    }

    // ============ 内部方法 ============

    /// <summary>
    /// 根据当前 PrinterInfo 与画布构建打印参数。对应 JS <c>buildPrintOptions()</c>。
    /// </summary>
    private PrintImageOptions BuildPrintOptions(DzImageData imageData) =>
        PrintImageOptions.Create(imageData, PrinterInfo, Canvas?.Base.Orientation ?? 0);

    // ============ IDisposable ============

    /// <summary>释放资源。对应 JS <c>quit()</c>。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        DeviceManager.Dispose();
        Canvas = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 打印机硬件信息。对应 JS <c>getPrinterInfo</c> 返回的信息对象。
/// </summary>
public sealed class PrinterHardwareInfo
{
    /// <summary>硬件能力标志。</summary>
    public HardwareFlags HardwareFlags { get; set; }

    /// <summary>软件能力标志。</summary>
    public SoftwareFlags SoftwareFlags { get; set; }

    /// <summary>打印机缓冲区大小（字节）。</summary>
    public int BufferSize { get; set; }

    /// <summary>打印机 DPI。</summary>
    public int Dpi { get; set; }

    /// <summary>打印机像素宽度。</summary>
    public int PrinterWidth { get; set; }

    /// <summary>电池数量。</summary>
    public int BatteryCount { get; set; }

    /// <summary>电池电压（伏特）。</summary>
    public double BatteryVoltage { get; set; }

    /// <summary>是否正在充电。</summary>
    public bool ChargeStatus { get; set; }

    /// <summary>
    /// 从 CMD_DEV_HANDSHAKE 响应字节解析硬件信息（旧格式，部分型号不支持）。
    /// 响应格式：[hwFlags(4)] [swFlags(4)] [bufferSize(2)] [dpi(1)] [width(2)]
    /// </summary>
    public static PrinterHardwareInfo Parse(byte[] response)
    {
        var info = new PrinterHardwareInfo();
        if (response.Length >= 4)
            info.HardwareFlags = (HardwareFlags)(
                (response[0] << 24) | (response[1] << 16) | (response[2] << 8) | response[3]);
        if (response.Length >= 8)
            info.SoftwareFlags = (SoftwareFlags)(
                (response[4] << 24) | (response[5] << 16) | (response[6] << 8) | response[7]);
        if (response.Length >= 10)
            info.BufferSize = (response[8] << 8) | response[9];
        if (response.Length >= 11)
            info.Dpi = response[10];
        if (response.Length >= 13)
            info.PrinterWidth = (response[11] << 8) | response[12];
        return info;
    }
}

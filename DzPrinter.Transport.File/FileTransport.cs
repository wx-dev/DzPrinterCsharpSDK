// =====================================================================
//  FileTransport：文件输出虚拟传输层。
//
//  设计目标：
//    1. 将发送到打印机的所有原始字节数据写入文件。
//    2. 支持多种输出格式：
//       - RawBinary：原始二进制（.bin），可供后续分析或重放
//       - HexText：十六进制文本（.hex/.txt），带时间戳方便调试
//    3. 完整实现 IDeviceTransport 接口：
//       - 连接 = 打开/创建文件
//       - 断开 = 关闭文件
//       - Send = 写入文件
//       - Discover = 返回虚拟文件打印机设备
//       - Request = 发送并返回模拟响应（握手/状态帧）
//    4. 自动追加时间戳、帧分隔符，便于分析。
//    5. 零平台依赖，适用于调试和测试。
// =====================================================================

using DzPrinter.Core;
using DzPrinter.Transport;
using System.Text;

namespace DzPrinter.Transport.File;

/// <summary>
/// 文件输出格式。
/// </summary>
public enum FileOutputFormat
{
    /// <summary>原始二进制。直接写入原始字节。</summary>
    RawBinary = 0,
    /// <summary>十六进制文本。每行带时间戳，格式："[HH:mm:ss.fff] XX XX XX..."。</summary>
    HexText = 1,
}

/// <summary>
/// 文件传输层配置选项。
/// </summary>
public sealed class FileTransportOptions
{
    /// <summary>
    /// 输出文件路径。
    /// 如果为空，使用临时目录下的时间戳文件名。
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>输出格式。默认 RawBinary。</summary>
    public FileOutputFormat Format { get; set; } = FileOutputFormat.RawBinary;

    /// <summary>
    /// 是否追加到现有文件。
    /// false = 覆盖写入（默认），true = 追加写入。
    /// </summary>
    public bool Append { get; set; } = false;

    /// <summary>
    /// 是否生成 PNG 预览图。
    /// true = 断开连接时，将捕获的协议字节解码为标签图像并另存为 .png 预览（与 OutputPath 同目录同名）。
    /// 解码器支持：RAW BITMAP、REPEAT、ESC J 走纸、RLE5/RLE6/RLEC 压缩反解。
    /// 默认 false。
    /// </summary>
    public bool SavePngPreview { get; set; } = false;

    /// <summary>
    /// PNG 预览图输出路径（可选）。
    /// 为空时使用 OutputPath 同目录同名 + .png。
    /// </summary>
    public string? PngOutputPath { get; set; }

    /// <summary>
    /// PNG 预览背景色（0=黑色底白字，1=白色底黑字）。默认 1（白底黑字）。
    /// </summary>
    public int PngBackground { get; set; } = 1;

    /// <summary>
    /// PNG 缩放倍数。1 = 1:1 实际像素，2 = 放大 2 倍便于查看。默认 2。
    /// </summary>
    public int PngScale { get; set; } = 2;

    /// <summary>
    /// 虚拟设备 ID。用于 DiscoverAsync 返回值和 ConnectAsync 参数。
    /// 默认为 "virtual-file-printer"。
    /// </summary>
    public string VirtualDeviceId { get; set; } = "virtual-file-printer";

    /// <summary>
    /// 虚拟设备名称。用于 DiscoverAsync 返回值，必须在 SupportPrinterMatcher 前缀列表中
    /// （否则会被过滤）。默认 "D60-File"（以 D60 开头，匹配德佟打印机前缀）。
    /// </summary>
    public string VirtualDeviceName { get; set; } = "D60-File";

    /// <summary>
    /// 请求响应延迟（毫秒）。用于模拟 RequestAsync 的响应等待。
    /// 默认 0（立即返回）。
    /// </summary>
    public int RequestDelayMs { get; set; } = 0;
}

/// <summary>
/// 文件输出虚拟传输层。实现 <see cref="IDeviceTransport"/>，
/// 将 SendAsync 接收到的所有字节数据写入指定文件。
/// </summary>
/// <remarks>
/// <para>典型用途：</para>
/// <list type="bullet">
///   <item>调试：将打印数据保存到文件，用十六进制工具逐帧分析</item>
///   <item>测试：无需真实打印机即可验证编码逻辑</item>
///   <item>重放：捕获的原始数据可重新发送到真实打印机</item>
/// </list>
/// </remarks>
public sealed class FileTransport : TransportBase
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly FileTransportOptions _options;

    private FileStream? _fileStream;
    private string _actualPath = string.Empty;

    // PNG 预览：捕获原始字节（仅 RawBinary 模式有效，HexText 解码会失败）
    private MemoryStream? _capturedRaw;

    // 响应等待队列（模拟设备→主机通知）
    private readonly List<byte> _notifyBuffer = new();

    public FileTransport() : this(new FileTransportOptions()) { }

    public FileTransport(FileTransportOptions options)
    {
        _options = options ?? new FileTransportOptions();
    }

    // ============ 可观测属性（供调试/测试） ============

    /// <summary>实际写入的文件路径（连接成功后可用）。</summary>
    public string ActualPath
    {
        get { lock (_sync) return _actualPath; }
    }

    /// <summary>
    /// 已写入的总字节数。
    /// </summary>
    public long BytesWritten
    {
        get
        {
            lock (_sync)
            {
                try { return _fileStream?.Length ?? 0; } catch { return 0; }
            }
        }
    }

    /// <summary>
    /// PNG 预览图的实际保存路径（Disconnect 成功后可用，SavePngPreview=true 时有值）。
    /// </summary>
    public string? ActualPngPath { get; private set; }

    // ============ TransportBase 抽象成员 ============

    /// <inheritdoc />
    public override TransportType TransportType => TransportType.File;

    // ============ IDeviceTransport 方法 ============

    /// <summary>
    /// 发现虚拟文件打印机设备。返回单个虚拟设备，设备名以 D60 前缀开头
    /// 以便通过 SupportPrinterMatcher 过滤。
    /// </summary>
    public override Task<IReadOnlyList<DeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var device = new DeviceInfo
        {
            DeviceId = _options.VirtualDeviceId,
            DeviceName = _options.VirtualDeviceName,
            TransportType = (TransportType)98, // File = 98，接近 Mock=99
            HardwareFlags = 0,
            // RLE5_BITMAP=0x10 | RLE6_BITMAP=0x20 | RLEC_BITMAP=0x80 = 0xB0
            // 此处使用原始 uint 数值避免对 Printer 层的循环引用（Transport ← Printer 单向引用）
            SoftwareFlags = 0x000000B0,
            BufferSize = 4096,
            Dpi = 203,
            PrinterWidth = 384,
            NativeDevice = null,
        };
        return Task.FromResult<IReadOnlyList<DeviceInfo>>(new[] { device });
    }

    /// <summary>
    /// 连接到虚拟设备 = 打开/创建输出文件。
    /// </summary>
    public override async Task ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));

        SetState(ConnectionState.Connecting);
        await Task.Yield();

        try
        {
            var path = ResolveOutputPath();
            var mode = _options.Append ? FileMode.OpenOrCreate : FileMode.Create;
            _fileStream = new FileStream(path, mode, FileAccess.Write, FileShare.Read);
            if (_options.Append)
                _fileStream.Seek(0, SeekOrigin.End);

            // 初始化原始字节捕获（用于 PNG 预览。HexText 模式也捕获，因为编码帧本身就是二进制）
            _capturedRaw = new MemoryStream(4096);
            if (_options.Append && _options.Format == FileOutputFormat.RawBinary)
            {
                // 如果是追加 + 原始二进制，尝试读取现有文件内容到 capture
                try
                {
                    using var existing = System.IO.File.OpenRead(path);
                    existing.CopyTo(_capturedRaw);
                }
                catch { /* 忽略 */ }
            }

            // 写文件头标记（仅 HexText 格式且为新文件时）
            if (_options.Format == FileOutputFormat.HexText && _fileStream.Position == 0)
            {
                var header = Encoding.UTF8.GetBytes(
                    $"# FileTransport capture start @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                    $"# Format: {_options.Format}, Device: {device.DeviceName}\r\n" +
                    $"# Each line: [timestamp] hex-bytes...\r\n");
                await _fileStream.WriteAsync(header, 0, header.Length, cancellationToken);
            }

            lock (_sync)
            {
                _actualPath = path;
                _connectedDevice = device;
            }
            Log.Info($"【FileTransport】ConnectAsync() —— 输出文件: {path}, 格式: {_options.Format}");
            SetState(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            Log.Error($"【FileTransport】ConnectAsync() 失败: {ex.Message}");
            _fileStream?.Dispose();
            _fileStream = null;
            SetState(ConnectionState.Failed, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 断开连接 = 关闭文件。若 SavePngPreview=true，同时生成 PNG 预览。
    /// </summary>
    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(ConnectionState.Disconnecting);
        await Task.Yield();

        // 在锁内完成文件刷盘和 capture 收集
        byte[]? capturedRawBytes = null;
        lock (_sync)
        {
            if (_fileStream != null)
            {
                if (_options.Format == FileOutputFormat.HexText)
                {
                    var footer = Encoding.UTF8.GetBytes(
                        $"\r\n# FileTransport capture end @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}, " +
                        $"total {_fileStream.Length} bytes\r\n");
                    _fileStream.Write(footer, 0, footer.Length);
                }
                _fileStream.Flush();
                _fileStream.Dispose();
                _fileStream = null;
            }
            if (_capturedRaw != null)
            {
                capturedRawBytes = _capturedRaw.ToArray();
                _capturedRaw.Dispose();
                _capturedRaw = null;
            }
            _connectedDevice = null;
        }

        // 生成 PNG（在锁外，避免 IO 阻塞）
        if (_options.SavePngPreview && capturedRawBytes != null && capturedRawBytes.Length > 0
            && !string.IsNullOrEmpty(_actualPath))
        {
            try
            {
                var pngPath = _options.PngOutputPath;
                if (string.IsNullOrWhiteSpace(pngPath))
                {
                    var dir = Path.GetDirectoryName(_actualPath) ?? ".";
                    var fileNoExt = Path.GetFileNameWithoutExtension(_actualPath);
                    pngPath = Path.Combine(dir, fileNoExt + ".png");
                }
                else
                {
                    var dir = Path.GetDirectoryName(pngPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    pngPath = Path.GetFullPath(pngPath);
                }

                var result = PrintPreviewDecoder.DecodeAndSavePng(capturedRawBytes, pngPath,
                    background: Math.Clamp(_options.PngBackground, 0, 1),
                    scale: Math.Clamp(_options.PngScale, 1, 8));

                ActualPngPath = pngPath;
                Log.Info($"【FileTransport】PNG 预览已生成: {pngPath} " +
                         $"({result.PixelWidth}x{result.PixelHeight}, {result.Rows.Count} 行, " +
                         $"{result.Warnings.Count} 条警告)");
            }
            catch (Exception ex)
            {
                Log.Error($"【FileTransport】PNG 预览生成失败: {ex.Message}");
            }
        }

        Log.Info("【FileTransport】DisconnectAsync() —— 文件已关闭");
        SetState(ConnectionState.Disconnected);
    }

    /// <summary>
    /// 发送数据 = 写入文件。
    /// </summary>
    public override async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected)
            throw new InvalidOperationException("FileTransport 未连接");
        if (_fileStream == null)
            throw new InvalidOperationException("文件流未初始化");

        lock (_sync)
        {
            if (State != ConnectionState.Connected)
                throw new InvalidOperationException("FileTransport 未连接");
        }

        switch (_options.Format)
        {
            case FileOutputFormat.RawBinary:
                await _fileStream.WriteAsync(data, cancellationToken);
                // RawBinary：内容直接可解码
                _capturedRaw?.Write(data.Span);
                break;

            case FileOutputFormat.HexText:
                var line = FormatHexLine(data.Span);
                var bytes = Encoding.UTF8.GetBytes(line);
                await _fileStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                // HexText 模式下，仍然把原始帧字节保留给 PNG 预览（与文件内容无关）
                _capturedRaw?.Write(data.Span);
                break;
        }

        await _fileStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 发送数据并返回模拟响应。
    /// 握手帧（CMD_HANDSHAKE）返回伪造的成功响应，其他返回 null。
    /// </summary>
    /// <remarks>
    /// FileTransport 不复用 <see cref="TransportBase.RequestAsyncCore"/>，
    /// 因其请求-响应模式为同步模拟（写文件 + 立即构造 mock 响应），
    /// 与 BLE/HID 的"发送 + 累积分片 + 超时 + 帧提取"模式不同。
    /// </remarks>
    public override async Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(data, cancellationToken).ConfigureAwait(false);

        if (_options.RequestDelayMs > 0)
            await Task.Delay(_options.RequestDelayMs, cancellationToken).ConfigureAwait(false);

        // 模拟握手响应（0x1F CMD_STATUS_OK 0x00 0x00 CRC）
        // 真实打印机握手会返回硬件能力、软件标志等，这里给最小成功响应
        byte[]? response = null;
        if (data.Length >= 2 && data.Span[0] == 0x1F)
        {
            var cmd = data.Span[1];
            // 对任意命令返回一个通用成功状态帧
            response = BuildMockStatusResponse(cmd);
            Receive(response);
        }

        return response;
    }

    /// <summary>模拟设备→主机发回数据，触发 DataReceived 事件。</summary>
    public void Receive(byte[] payload)
    {
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        RaiseDataReceived(payload);
    }

    // ============ 私有方法 ============

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
        {
            var dir = Path.GetDirectoryName(_options.OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.GetFullPath(_options.OutputPath);
        }

        // 默认路径：临时目录 + 时间戳
        var ext = _options.Format switch
        {
            FileOutputFormat.HexText => ".txt",
            _ => ".bin",
        };
        var tmpDir = Path.Combine(Path.GetTempPath(), "DzPrinter");
        Directory.CreateDirectory(tmpDir);
        return Path.Combine(tmpDir, $"print_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
    }

    /// <summary>
    /// 将数据格式化为十六进制行："[HH:mm:ss.fff] XX XX XX ...\r\n"
    /// </summary>
    private static string FormatHexLine(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(256);
        sb.Append('[');
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
        sb.Append("] ");
        sb.Append(data.Length.ToString());
        sb.Append(" bytes: ");
        sb.Append(ByteUtils.ToHexString(data));
        sb.Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// 构建最小成功状态响应帧。
    /// 格式：[0x1F][CMD=47(STATUS)][len=2][payload=2字节状态][CRC=0x88]
    /// 真实帧格式参考 ProtocolPacket，此处仅用于模拟"请求成功"。
    /// </summary>
    private static byte[] BuildMockStatusResponse(byte requestCmd)
    {
        // 简化：返回 [0x1F][CMD=0x2F(47, STATUS_OK)][len=0][CRC=0x88]
        // 这表示收到的命令已成功处理，不携带额外 payload
        var result = new byte[4];
        result[0] = 0x1F;
        result[1] = 0x2F; // CMD_STATUS_OK 占位（47）
        result[2] = 0;    // 0 字节 payload
        result[3] = 0x88; // CRC (与控制帧一致)
        return result;
    }
}

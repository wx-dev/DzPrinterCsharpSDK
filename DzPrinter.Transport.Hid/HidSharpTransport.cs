// =====================================================================
//  Windows HID USB 传输层实现。基于 HidSharp (https://www.zer7.com/software/hidsharp)。
//  对应 JS SDK 中 He (WebHIDAdapter) 与德佟 USB HID 协议发送逻辑。
//
//  实现策略：
//    1. HidDeviceLoader 枚举设备（匹配 VID/PID 或名称）。
//    2. HidStream.Open 打开流（Report ID + 数据）。
//    3. 写入按 MTU 分片（每帧 63 字节 payload + 1 Report ID）。
//    4. 读取通过 HidStream.Read 线程循环收集通知。
// =====================================================================

using DzPrinter.Core;
using HidSharp;
using HidSharp.Reports;

namespace DzPrinter.Transport.Hid;

/// <summary>HID 传输层选项。</summary>
public sealed class HidTransportOptions
{
    /// <summary>如果为非空，按 VID 过滤设备。</summary>
    public int? VendorId { get; set; }
    /// <summary>如果为非空，按 PID 过滤设备。</summary>
    public int? ProductId { get; set; }
    /// <summary>如果为非空，按名称 Contains 过滤设备。</summary>
    public string? NameContains { get; set; }
    /// <summary>HID Report ID。默认 0。</summary>
    public byte ReportId { get; set; } = 0;
    /// <summary>读超时毫秒。</summary>
    public int ReadTimeoutMs { get; set; } = 500;
    /// <summary>写超时毫秒。</summary>
    public int WriteTimeoutMs { get; set; } = 2000;
    /// <summary>HID 报告发送间隔毫秒。默认 20ms。</summary>
    public int SendIntervalMs { get; set; } = 20;
}

/// <summary>
/// Windows HID <see cref="IDeviceTransport"/> 实现。
/// 对应 JS 中 WebHID 适配器。
/// </summary>
public sealed class HidSharpTransport : IDeviceTransport, IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly HidTransportOptions _options;
    private readonly object _sync = new();
    private HidDevice? _hidDevice;
    private HidStream? _hidStream;
    private byte _detectedReportId;
    private Thread? _readThread;
    private CancellationTokenSource? _readCts;
    private Transport.ConnectionState _state = Transport.ConnectionState.Disconnected;
    private Transport.DeviceInfo? _connected;

    private readonly List<byte> _readBuffer = new();

    public HidSharpTransport() : this(new HidTransportOptions()) { }

    public HidSharpTransport(HidTransportOptions options)
    {
        _options = options ?? new HidTransportOptions();
    }

    public Transport.ConnectionState State
    {
        get { lock (_sync) return _state; }
    }

    public Transport.DeviceInfo? ConnectedDevice
    {
        get { lock (_sync) return _connected; }
    }

    public event EventHandler<Transport.DataReceivedEventArgs>? DataReceived;
    public event EventHandler<Transport.ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public Task<IReadOnlyList<Transport.DeviceInfo>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        Log.Info("【HidSharpTransport】DiscoverAsync()");
        var list = new List<Transport.DeviceInfo>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            bool match = true;
            if (_options.VendorId.HasValue && d.VendorID != _options.VendorId.Value) match = false;
            if (_options.ProductId.HasValue && d.ProductID != _options.ProductId.Value) match = false;
            var name = SafeGetName(d);
            if (!string.IsNullOrEmpty(_options.NameContains) &&
                (name == null || !name.Contains(_options.NameContains,
                    StringComparison.OrdinalIgnoreCase))) match = false;
            if (!match) continue;

            int maxOut = 0;
            try { maxOut = d.GetMaxOutputReportLength(); } catch { }
            Log.Info($"【HidSharpTransport】Found: VID={d.VendorID:X4} PID={d.ProductID:X4} " +
                     $"MaxOutput={maxOut} Name={name}");

            list.Add(new Transport.DeviceInfo
            {
                DeviceId = d.DevicePath,
                DeviceName = name ?? $"HID ({d.VendorID:X4}:{d.ProductID:X4})",
                TransportType = Transport.TransportType.HidUsb,
                NativeDevice = d,
            });
        }
        return Task.FromResult<IReadOnlyList<Transport.DeviceInfo>>(list);
    }

    public Task ConnectAsync(Transport.DeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        SetState(Transport.ConnectionState.Connecting);
        try
        {
            HidDevice? hid = device.NativeDevice as HidDevice;
            if (hid == null)
            {
                hid = DeviceList.Local.GetHidDevices()
                    .FirstOrDefault(x => x.DevicePath == device.DeviceId);
            }
            if (hid == null) throw new InvalidOperationException($"找不到 HID 设备：{device}");

            if (!hid.TryOpen(out var stream))
                throw new InvalidOperationException($"无法打开 HID 设备流：{device}");

            stream.ReadTimeout = _options.ReadTimeoutMs;
            stream.WriteTimeout = _options.WriteTimeoutMs;

            // 检测设备输出报告 ID 和属性
            _detectedReportId = _options.ReportId;
            int maxOut = 0, maxIn = 0;
            try { maxOut = hid.GetMaxOutputReportLength(); } catch { }
            try { maxIn = hid.GetMaxInputReportLength(); } catch { }
            Log.Info($"【HidSharpTransport】Device: VID={hid.VendorID:X4} PID={hid.ProductID:X4} " +
                     $"MaxOutput={maxOut} MaxInput={maxIn} Name={SafeGetName(hid)}");

            try
            {
                var rawDesc = hid.GetRawReportDescriptor();
                var desc = new ReportDescriptor(rawDesc);
                var outputIds = ParseOutputReportIds(rawDesc);
                Log.Info($"【HidSharpTransport】ReportsUseID={desc.ReportsUseID}, OutputReportIDs=[{string.Join(", ", outputIds)}]");

                if (outputIds.Count > 0)
                {
                    _detectedReportId = outputIds[0];
                    Log.Info($"【HidSharpTransport】Using output Report ID = {_detectedReportId}");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"【HidSharpTransport】Failed to parse report descriptor: {ex.Message}, using Report ID = {_detectedReportId}");
            }

            lock (_sync)
            {
                _hidDevice = hid;
                _hidStream = stream;
                _connected = device;
            }

            _readCts = new CancellationTokenSource();
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "HidReadThread" };
            _readThread.Start(_readCts.Token);

            SetState(Transport.ConnectionState.Connected);
            Log.Info($"【HidSharpTransport】Connected to {device}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error($"【HidSharpTransport】Connect failed: {ex.Message}");
            SetState(Transport.ConnectionState.Failed, ex.Message);
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(Transport.ConnectionState.Disconnecting);
        try
        {
            _readCts?.Cancel();
            lock (_sync)
            {
                try { _hidStream?.Close(); } catch { /* 忽略 */ }
                _hidStream?.Dispose();
                _hidStream = null;
                _hidDevice = null;
                _connected = null;
            }
            _readCts?.Dispose();
            _readCts = null;
        }
        finally
        {
            SetState(Transport.ConnectionState.Disconnected);
        }
        return Task.CompletedTask;
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        HidStream? stream;
        int max;
        lock (_sync)
        {
            stream = _hidStream;
            max = _hidDevice?.GetMaxOutputReportLength() ?? 0;
        }
        if (stream == null) throw new InvalidOperationException("未连接，无法发送");

        if (max <= 0) max = 64;

        int payloadMax = Math.Max(1, max - 1); // Exclude Report ID
        byte reportId = _detectedReportId;
        int interval = _options.SendIntervalMs;

        // USB 传输层数据包格式：<ReportID> <EBV dataLen> [data...]
        // 例如设备收到的 64 字节 = [0x1E(ReportID=包头)] [EBV(62)] [62字节命令数据]

        // maxData = payloadMax - 1(EBV) = 62, 对应 dz-print 的 max_out_size
        int maxData = payloadMax - 1;
        byte[] fixedEbv = maxData < 192
            ? new byte[] { (byte)maxData }
            : new byte[] { (byte)((maxData >> 8) | 0xC0), (byte)(maxData & 0xFF) };

        int sent = 0;
        int frameNum = 0;
        while (sent < data.Length)
        {
            int len = Math.Min(maxData, data.Length - sent);

            var frame = new byte[max];
            int pos = 0;
            frame[pos++] = reportId; // USB 传输包头
            foreach (var b in fixedEbv) frame[pos++] = b;
            data.Slice(sent, len).CopyTo(frame.AsMemory(pos, len));
            // 剩余字节为 0（new byte[] 默认零填充），构成固定长度包

            if (frameNum < 5)
            {
                var hex = string.Join(" ", frame.Take(Math.Min(24, pos + len)).Select(b => b.ToString("X2")));
                Log.Info($"【HID TX】frame={frameNum} reportId={reportId} max={max} dataLen={len} " +
                         $"fixedLen={maxData} [{hex}...]");
            }

            stream.Write(frame, 0, frame.Length);
            sent += len;
            frameNum++;

            if (sent < data.Length && interval > 0)
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        Log.Info($"【HID TX】发送完成: {frameNum} 帧, {data.Length} 字节");
    }

    public Task<byte[]?> RequestAsync(ReadOnlyMemory<byte> data, int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
        => RequestAsyncImpl(data, timeoutMs, cancellationToken);

    private async Task<byte[]?> RequestAsyncImpl(ReadOnlyMemory<byte> data, int timeoutMs,
        CancellationToken cancellationToken)
    {
        await SendAsync(data, cancellationToken).ConfigureAwait(false);
        lock (_readBuffer) _readBuffer.Clear();
        await Task.Delay(timeoutMs, cancellationToken).ConfigureAwait(false);
        lock (_readBuffer)
        {
            return _readBuffer.Count == 0 ? null : _readBuffer.ToArray();
        }
    }

    public void Dispose()
    {
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { /* 忽略 */ }
    }

    // ============ 私有方法 ============

    private void ReadLoop(object? state)
    {
        var ct = (CancellationToken)(state ?? CancellationToken.None);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                HidStream? stream;
                int max;
                lock (_sync)
                {
                    stream = _hidStream;
                    max = _hidDevice?.GetMaxInputReportLength() ?? 64;
                }
                if (stream == null) break;
                var buf = new byte[max];
                int n;
                try { n = stream.Read(buf, 0, buf.Length); }
                catch (TimeoutException) { continue; }
                catch (IOException) { break; }
                catch (ObjectDisposedException) { break; }
                if (n <= 0) continue;

                // 去掉 Report ID (首字节)
                int start = (n > 1 && buf[0] == _detectedReportId) ? 1 : 0;

                // 解包 USB 传输层
                var payload = UnwrapUsbTransport(buf, start, n - start);
                if (payload.Length > 0)
                {
                    lock (_readBuffer) _readBuffer.AddRange(payload);
                    DataReceived?.Invoke(this, new Transport.DataReceivedEventArgs(payload));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"【HidSharpTransport】ReadLoop exited: {ex.Message}");
        }
    }

    /// <summary>
    /// 解包 USB 传输层响应数据。
    /// 格式1（ReportID=包头，已剥离）：[EBV] [data...] → 剥离 EBV
    /// 格式2（原始数据）：[data...] → 原样返回
    /// 通过检查 EBV 后的首字节是否为有效命令起始（0x1F/0x1B/0x0C）来区分格式1和2。
    /// </summary>
    private static byte[] UnwrapUsbTransport(byte[] buf, int start, int length)
    {
        if (length < 1) return Array.Empty<byte>();

        int pos = start;

        // 格式1/2：尝试解析 EBV，检查 EBV 后的首字节是否为有效命令
        var extracted = ParseEbvAndExtract(buf, pos, length);
        if (extracted != null && extracted.Length > 0)
        {
            byte firstByte = extracted[0];
            // 有效命令起始字节：0x1F(协议帧), 0x1B(ESC), 0x0C(页结束)
            if (firstByte == 0x1F || firstByte == 0x1B || firstByte == 0x0C)
                return extracted;
        }

        // 格式2：原始数据，原样返回
        return buf.AsSpan(start, length).ToArray();
    }

    /// <summary>
    /// 从 pos 位置解析 EBV 长度，提取对应的数据。
    /// </summary>
    private static byte[]? ParseEbvAndExtract(byte[] buf, int pos, int available)
    {
        if (available < 1) return null;

        int dataLen;
        int ebvBytes;
        if (buf[pos] >= 192 && pos + 1 < pos + available)
        {
            dataLen = ((buf[pos] & 0x3F) << 8) | buf[pos + 1];
            ebvBytes = 2;
        }
        else
        {
            dataLen = buf[pos];
            ebvBytes = 1;
        }

        int dataStart = pos + ebvBytes;
        int remaining = pos + available - dataStart;
        int actualLen = Math.Min(dataLen, remaining);
        if (actualLen <= 0) return null;

        return buf.AsSpan(dataStart, actualLen).ToArray();
    }

    /// <summary>
    /// 解析 HID 报告描述符，提取所有 Output 报告对应的 Report ID。
    /// HID 描述符格式：每项 = [prefix(byte)] [data(0-4 bytes)]
    /// prefix = [tag(4bit)][type(2bit)][size(2bit)]；size=3 表示 4 字节。
    /// Global item tag=8 → Report ID；Main item tag=9 → Output report。
    /// </summary>
    private static List<byte> ParseOutputReportIds(byte[] descriptor)
    {
        var ids = new List<byte>();
        byte currentReportId = 0;

        int i = 0;
        while (i < descriptor.Length)
        {
            byte prefix = descriptor[i];
            if (prefix == 0) // long item: next byte is data size
            {
                if (i + 1 >= descriptor.Length) break;
                i += 2 + descriptor[i + 1];
                continue;
            }

            int size = prefix & 0x03;
            if (size == 3) size = 4;
            int type = (prefix >> 2) & 0x03;
            int tag = (prefix >> 4) & 0x0F;

            if (type == 1 && tag == 8 && size >= 1 && i + 1 < descriptor.Length)
            {
                // Global: Report ID
                currentReportId = descriptor[i + 1];
            }
            else if (type == 0 && tag == 9)
            {
                // Main: Output report
                if (!ids.Contains(currentReportId))
                    ids.Add(currentReportId);
            }

            i += 1 + size;
        }

        return ids;
    }

    private static string? SafeGetName(HidDevice d)
    {
        try { return d.GetManufacturer() + " " + d.GetProductName(); }
        catch { return null; }
    }

    private void SetState(Transport.ConnectionState state, string? msg = null)
    {
        lock (_sync) _state = state;
        ConnectionStateChanged?.Invoke(this,
            new Transport.ConnectionStateChangedEventArgs(state, msg));
    }
}

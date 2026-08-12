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

namespace DzPrinter.Transport.Hid;

/// <summary>HID 连接选项。</summary>
public sealed class HidConnectionOptions
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
}

/// <summary>
/// Windows HID <see cref="IDeviceTransport"/> 实现。
/// 对应 JS 中 WebHID 适配器。
/// </summary>
public sealed class HidSharpTransport : IDeviceTransport, IDisposable
{
    private static readonly ILogger Log = DzLogger.Current;

    private readonly HidConnectionOptions _options;
    private readonly object _sync = new();
    private HidDevice? _hidDevice;
    private HidStream? _hidStream;
    private Thread? _readThread;
    private CancellationTokenSource? _readCts;
    private Transport.ConnectionState _state = Transport.ConnectionState.Disconnected;
    private Transport.DeviceInfo? _connected;

    private readonly List<byte> _readBuffer = new();

    public HidSharpTransport() : this(new HidConnectionOptions()) { }

    public HidSharpTransport(HidConnectionOptions options)
    {
        _options = options ?? new HidConnectionOptions();
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

    public Task SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        HidStream? stream;
        int max;
        lock (_sync)
        {
            stream = _hidStream;
            max = _hidDevice?.GetMaxOutputReportLength() ?? 64;
        }
        if (stream == null) throw new InvalidOperationException("未连接，无法发送");

        // max = ReportId(1) + payload(max-1)
        int payloadMax = Math.Max(1, max - 1);
        byte reportId = _options.ReportId;

        int sent = 0;
        while (sent < data.Length)
        {
            int len = Math.Min(payloadMax, data.Length - sent);
            var frame = new byte[1 + len];
            frame[0] = reportId;
            data.Slice(sent, len).CopyTo(frame.AsMemory(1, len));
            stream.Write(frame, 0, frame.Length);
            sent += len;
        }
        return Task.CompletedTask;
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
                byte[] payload;
                if (n > 1 && buf[0] == _options.ReportId)
                {
                    payload = buf.AsSpan(1, n - 1).ToArray();
                }
                else
                {
                    payload = buf.AsSpan(0, n).ToArray();
                }
                lock (_readBuffer) _readBuffer.AddRange(payload);
                DataReceived?.Invoke(this, new Transport.DataReceivedEventArgs(payload));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"【HidSharpTransport】ReadLoop exited: {ex.Message}");
        }
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

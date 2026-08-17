using DzPrinter.Imaging;

namespace DzPrinter.Printer;

/// <summary>
/// 打印作业参数。对应 JS <see cref="PrintEncoder"/> 中合并后的 <c>t</c> 对象
/// （由 <c>encodeImageData(t, e)</c> 将 image 与 options 合并而成）。
/// </summary>
public sealed class PrintImageOptions
{
    /// <summary>页面键（用于多页打印时设备端页匹配）。JS: <c>pageKey</c>。</summary>
    public int PageKey { get; set; }

    /// <summary>位图数据。JS: <c>imageData</c>。</summary>
    public DzImageData ImageData { get; set; }

    /// <summary>打印方向：0=正常, 1=90°, 2=180°, 3=270°。JS: <c>orientation</c>。</summary>
    public int Orientation { get; set; }

    /// <summary>打印机 DPI。JS: <c>printerDPI</c>。默认 203。</summary>
    public int PrinterDpi { get; set; } = ProtocolConstants.PrinterDpiDefault;

    /// <summary>打印机像素宽度。JS: <c>printerWidth</c>。默认 384。</summary>
    public int PrinterWidth { get; set; } = ProtocolConstants.PrinterWidthDefault;

    /// <summary>间隙类型（标签定位）。255 表示不设置。JS: <c>gapType</c>。</summary>
    public int GapType { get; set; } = 255;

    /// <summary>间隙长度（mm）。JS: <c>gapLength</c>。</summary>
    public double GapLength { get; set; }

    /// <summary>打印浓度（1 起算）。255 表示不设置。JS: <c>printDarkness</c>。</summary>
    public int PrintDarkness { get; set; } = 255;

    /// <summary>打印速度（1 起算）。255 表示不设置。JS: <c>printSpeed</c>。</summary>
    public int PrintSpeed { get; set; } = 255;

    /// <summary>二值化阈值（0-255）。JS: <c>threshold</c>。默认 150。</summary>
    public int Threshold { get; set; } = ProtocolConstants.ThresholdDefault;

    /// <summary>顶部留白行数。JS: <c>marginTop</c>。</summary>
    public int MarginTop { get; set; }

    /// <summary>底部留白行数。JS: <c>marginBottom</c>。</summary>
    public int MarginBottom { get; set; }

    /// <summary>
    /// 打印对齐：0=左, 1=居中, 2=右（JS 风格）；内部归一化为软件标志位。
    /// 也可直接传入 <see cref="SoftwareFlags"/> 对齐位。JS: <c>printAlignment</c>。
    /// </summary>
    public int PrintAlignment { get; set; }

    /// <summary>软件能力标志。JS: <c>softwareFlags</c>。默认 RLE5 支持 + 左对齐。</summary>
    public SoftwareFlags SoftwareFlags { get; set; } = SoftwareFlags.PCPDSF_RLE5_BITMAP | SoftwareFlags.PCPDSF_PRTA_LEFT;

    /// <summary>硬件能力标志。JS: <c>hardwareFlags</c>。</summary>
    public HardwareFlags HardwareFlags { get; set; }

    /// <summary>是否启用超级位图（RLEC 等）。JS: <c>enableSuperBitmap</c>。</summary>
    public bool EnableSuperBitmap { get; set; } = true;

    /// <summary>页号（多页打印）。JS: <c>pageNo</c>。</summary>
    public int PageNo { get; set; }

    /// <summary>总页数。JS: <c>PageCount</c>。</summary>
    public int PageCount { get; set; }

    /// <summary>
    /// 从 PrinterInfo 创建打印参数。供 LPAPI 与 DrawContext 共用，消除重复装配。
    /// </summary>
    public static PrintImageOptions Create(DzImageData imageData, PrinterInfo printerInfo, int orientation = 0) => new()
    {
        ImageData = imageData,
        PrinterDpi = printerInfo.PrinterDpi,
        PrinterWidth = printerInfo.PrinterWidth,
        GapType = (int)printerInfo.GapType,
        GapLength = printerInfo.GapLength,
        PrintDarkness = (int)printerInfo.Darkness,
        PrintSpeed = (int)printerInfo.Speed,
        PageCount = printerInfo.PageCount,
        Orientation = orientation,
    };
}

/// <summary>
/// 核心打印编码器：对应 JS SDK 中的 <c>Se</c> 类（原映射 <c>Ke</c>）。
/// 将 <see cref="DzImageData"/> 转换为打印机指令流：行数据 → RLE 压缩 → 协议帧。
/// </summary>
/// <remarks>
/// <para><b>核心流程</b>（对应 JS <c>Se.print</c>）：</para>
/// <list type="number">
///   <item><see cref="Start"/>：发送页面参数帧（PAGE_START/WIDTH/GAP/DARKNESS/SPEED）</item>
///   <item><see cref="Encode"/>：逐行扫描像素，按方向旋转，二值化后调用 <see cref="PrintRow"/></item>
///   <item><see cref="End"/>：收尾走纸 + 页结束符 0x0C</item>
/// </list>
/// <para><b>RLE 自动选优</b>（对应 JS <c>Se.pushPrint</c>）：计算 RLEC/RLE5X/RLE5D/RLE6X/RLE6D
/// 五种压缩的输出大小，按优先级选取最小者；都不优于原始位图时发送未压缩帧。</para>
/// <para><b>重要</b>：位图数据帧（RLE/raw bitmap/repeat）<em>不带 CRC</em>，
/// 仅控制帧（PAGE_START 等）带 CRC 0x88；走纸使用原始 ESC/J，页结束使用 0x0C。
/// 这些均与 JS 逐字节一致。</para>
/// </remarks>
public sealed class PrintEncoder
{
    // 对齐常量（对应 JS Te=1024=左, Oe=1536=掩码, 512=居中）
    private const int AlignLeft = 1024;
    private const int AlignCenter = 512;
    private const int AlignMask = 1536;

    // 行动作状态：1=走纸累积，2=位图打印累积
    private const int LineActionFeed = 1;
    private const int LineActionPrint = 2;

    private static PrintEncoder? _instance;

    private readonly PackageBufferList _bufferList = new();

    private int _threshold = ProtocolConstants.ThresholdDefault;
    private int _orientation;
    private int _lineAction;
    private SoftwareFlags _softwareFlags = SoftwareFlags.PCPDSF_RLE5_BITMAP;
    private HardwareFlags _hardwareFlags;
    private bool _supportSuperBitmap = true;
    private int _printerWidth;
    private int _lineCount;
    private int _lineBytes;
    private int _byteWidth;
    private byte[] _lineData = Array.Empty<byte>();
    private int _prevBytes;
    private byte[] _prevData = Array.Empty<byte>();

    // 统计字段（对应 JS mSum*/mRLE*Saved）
    public int SumLines { get; private set; }
    public int SumPrints { get; private set; }
    public int SumRle5X { get; private set; }
    public int SumRle5D { get; private set; }
    public int SumRle6X { get; private set; }
    public int SumRle6D { get; private set; }
    public int SumRleC { get; private set; }
    public int SumRepeats { get; private set; }
    public int Rle5XSaved { get; private set; }
    public int Rle5DSaved { get; private set; }
    public int Rle6XSaved { get; private set; }
    public int Rle6DSaved { get; private set; }
    public int RleCSaved { get; private set; }

    /// <summary>是否横向（orientation 0 或 2）。对应 JS <c>Se.IsLandscape</c>。</summary>
    public bool IsLandscape => _orientation == 0 || _orientation == 2;

    /// <summary>单例入口。对应 JS <c>Se.instance</c>。</summary>
    public static PrintEncoder Instance => _instance ??= new PrintEncoder();

    /// <summary>
    /// 静态便捷入口：编码图像数据为字节分片列表。对应 JS <c>Se.encodeImageData(t, e)</c>。
    /// </summary>
    public static List<byte[]> EncodeImageData(DzImageData image, PrintImageOptions? options = null)
    {
        var opts = options ?? new PrintImageOptions { ImageData = image };
        opts.ImageData = image;
        return Instance.Print(opts);
    }

    /// <summary>
    /// 更新已编码分片中的 pageKey。对应 JS <c>Se.updatePageKey(t, e)</c>。
    /// 用于多页打印时复用已编码数据，仅替换 pageKey。
    /// </summary>
    public static void UpdatePageKey(List<byte[]> chunks, int newPageKey)
    {
        if (chunks.Count == 0) return;
        var first = chunks[0];
        if (first.Length <= 0 || first[0] != ProtocolConstants.HostToDeviceDataStart) return;
        if (first[1] != (byte)PrinterCommand.CMD_PAGE_START) return;
        if (first[2] != 2) return; // 仅单字节长度=2 的包

        // toShort(first[4], first[3]) = first[3]<<8 | first[4]
        var oldKey = (first[3] << 8) | first[4];
        if (oldKey == newPageKey) return;

        var bytes = EbvHelper.GetBytesFromShort(newPageKey, false);
        first[3] = bytes[0];
        first[4] = bytes[1];
        DzProtocolLog.Info($"---- updatePageKey: from -> to [{oldKey}] => [{newPageKey}]");
    }

    /// <summary>主入口：编码一个打印作业。对应 JS <c>Se.print(t)</c>。</summary>
    public List<byte[]> Print(PrintImageOptions options)
    {
        if (!Start(options)) return new List<byte[]>();
        DzProtocolLog.Info("---- start to image encode:");
        Encode(options);
        DzProtocolLog.Info("---- stop to image encode:");
        return End(options);
    }

    /// <summary>重置编码器状态。对应 JS <c>Se.reset(t)</c>。</summary>
    private void Reset(PrintImageOptions options)
    {
        var o = options;

        // 方向归一化
        if (o.Orientation >= 360) o.Orientation %= 360;
        if (o.Orientation > 3) o.Orientation /= 90;
        _orientation = o.Orientation;

        _printerWidth = o.PrinterWidth > 0 ? o.PrinterWidth : ProtocolConstants.PrinterWidthDefault;

        // 阈值
        _threshold = (o.Threshold > 0 && o.Threshold < 255)
            ? o.Threshold
            : ProtocolConstants.ThresholdDefault;

        _bufferList.Reset();
        _softwareFlags = o.SoftwareFlags;
        _hardwareFlags = o.HardwareFlags;
        _supportSuperBitmap = o.EnableSuperBitmap;

        _lineAction = LineActionFeed;
        _byteWidth = (_printerWidth + 7) / 8;
        _lineCount = o.MarginTop > 0 ? o.MarginTop : 0;
        _lineBytes = 0;
        _lineData = Array.Empty<byte>();
        _prevBytes = 0;
        _prevData = Array.Empty<byte>();

        SumLines = SumPrints = SumRle5X = SumRle5D = SumRle6X = SumRle6D = SumRleC = SumRepeats = 0;
        Rle5XSaved = Rle5DSaved = Rle6XSaved = Rle6DSaved = RleCSaved = 0;
    }

    /// <summary>发送页面起始参数帧。对应 JS <c>Se.start(e)</c>。</summary>
    private bool Start(PrintImageOptions options)
    {
        var img = options.ImageData;
        if (!img.IsValid)
        {
            DzProtocolLog.Warn("---- PrintPackage.start --> imageData is null or invalid.");
            return false;
        }

        Reset(options);
        DzProtocolLog.Info($"========== startPage pageKey: {options.PageKey} ==========");
        DzProtocolLog.Info($"---- width: {img.Width}, height: {img.Height}, orientation: {_orientation}");
        DzProtocolLog.Info($"---- printerDPI: {options.PrinterDpi}, printerWidth: {options.PrinterWidth}");
        DzProtocolLog.Info($"---- threshold: {options.Threshold}, supportSuperBitmap: {_supportSuperBitmap}");

        var s = new PackageBuffer();

        // 页面打印尺寸（横向取宽，纵向取高）
        var printDimension = IsLandscape ? img.Width : img.Height;

        // CMD_PAGE_START：2 字节大端 pageKey
        var pageKeyBytes = EbvHelper.GetBytesFromShort(options.PageKey, false);
        s.PushPackage(PrinterCommand.CMD_PAGE_START, pageKeyBytes);

        // CMD_PAGE_WIDTH：字节宽度 EBV
        s.PushPackage(PrinterCommand.CMD_PAGE_WIDTH,
            EbvHelper.FromEbv((printDimension + 7) / 8));

        // CMD_GAP_TYPE
        var gapType = options.GapType;
        if (gapType >= 0 && gapType <= 8)
            s.PushPackage(PrinterCommand.CMD_GAP_TYPE, new byte[] { (byte)gapType });

        // CMD_GAP_LEN（单位 0.01mm，即 floor(100 * mm)）
        if (options.GapLength > 0 && gapType > 0 && gapType <= 4)
        {
            var gapMm = (int)(100 * options.GapLength);
            if (gapMm > ProtocolConstants.MaxEbvValue) gapMm = ProtocolConstants.MaxEbvValue;
            s.PushPackage(PrinterCommand.CMD_GAP_LEN, EbvHelper.FromEbv(gapMm));
        }

        // CMD_DARKNESS（1 起算，发送 darkness-1；255=Unset 跳过）
        if (options.PrintDarkness >= 1 && options.PrintDarkness < 255)
            s.PushPackage(PrinterCommand.CMD_DARKNESS, new byte[] { (byte)(options.PrintDarkness - 1) });

        // CMD_SPEED（1 起算，发送 speed-1；255=Unset 跳过）
        if (options.PrintSpeed >= 1 && options.PrintSpeed < 255)
            s.PushPackage(PrinterCommand.CMD_SPEED, new byte[] { (byte)(options.PrintSpeed - 1) });

        _bufferList.Push(s.GetAllBytes());
        return true;
    }

    /// <summary>逐行编码像素。对应 JS <c>Se.encode(t)</c>。</summary>
    private void Encode(PrintImageOptions options)
    {
        var img = options.ImageData;
        var w = img.Width;
        var h = img.Height;
        // 实际打印宽度（受打印机物理宽度限制）
        var n = IsLandscape ? Math.Min(w, _printerWidth) : Math.Min(h, _printerWidth);
        _byteWidth = (n + 7) / 8;

        var threshold = _threshold;
        var data = img.Data;

        // 对齐偏移：超出打印机宽度的部分根据对齐方式裁剪
        var align = options.PrintAlignment != 0 ? options.PrintAlignment : (int)GetPrintAlignment();
        var offset = 0;
        var longer = IsLandscape ? w : h;
        if (longer > n)
        {
            var masked = align & AlignMask;
            offset = masked == AlignCenter ? (longer - n) / 2
                   : masked == AlignLeft ? 0
                   : longer - n; // 右对齐
        }

        var row = new byte[_byteWidth];

        if (_orientation == 1)
        {
            // 90° 旋转：逐列从底到顶
            for (var x = 0; x < w; x++)
            {
                Array.Clear(row, 0, _byteWidth);
                var mask = 0x80;
                var idx = 0;
                for (var y = 0; y < n; y++)
                {
                    var pixel = GetImageGrayValue(data, 4 * ((h - y - offset - 1) * w + x));
                    if (pixel <= threshold) row[idx] |= (byte)mask;
                    if (mask == 1) { mask = 0x80; idx++; } else mask >>= 1;
                }
                PrintRow(row);
            }
        }
        else if (_orientation == 2)
        {
            // 180° 旋转：逐行从底到顶，像素右到左
            for (var y = 0; y < h; y++)
            {
                Array.Clear(row, 0, _byteWidth);
                var mask = 0x80;
                var idx = 0;
                var pixelBase = 4 * ((h - y) * w - offset - 1);
                for (var x = 0; x < n; x++, pixelBase -= 4)
                {
                    var pixel = GetImageGrayValue(data, pixelBase);
                    if (pixel <= threshold) row[idx] |= (byte)mask;
                    if (mask == 1) { mask = 0x80; idx++; } else mask >>= 1;
                }
                PrintRow(row);
            }
        }
        else if (_orientation == 3)
        {
            // 270° 旋转：逐列从顶到底，像素右到左
            for (var x = 0; x < w; x++)
            {
                Array.Clear(row, 0, _byteWidth);
                var mask = 0x80;
                var idx = 0;
                for (var y = 0; y < n; y++)
                {
                    var pixel = GetImageGrayValue(data, 4 * ((y + offset) * w + (w - x - 1)));
                    if (pixel <= threshold) row[idx] |= (byte)mask;
                    if (mask == 1) { mask = 0x80; idx++; } else mask >>= 1;
                }
                PrintRow(row);
            }
        }
        else
        {
            // 默认 0°：逐行从顶到底，像素左到右
            for (var y = 0; y < h; y++)
            {
                Array.Clear(row, 0, _byteWidth);
                var mask = 0x80;
                var idx = 0;
                var pixelBase = 4 * (w * y + offset);
                for (var x = 0; x < n; x++, pixelBase += 4)
                {
                    var pixel = GetImageGrayValue(data, pixelBase);
                    if (pixel <= threshold) row[idx] |= (byte)mask;
                    if (mask == 1) { mask = 0x80; idx++; } else mask >>= 1;
                }
                PrintRow(row);
            }
        }
    }

    /// <summary>收尾：输出剩余走纸/位图，追加页结束符。对应 JS <c>Se.end(t)</c>。</summary>
    private List<byte[]> End(PrintImageOptions options)
    {
        var marginBottom = options.MarginBottom;
        switch (_lineAction)
        {
            case LineActionFeed:
                PushLine(_lineCount + marginBottom);
                break;
            case LineActionPrint:
                PushPrint();
                PushLine(marginBottom);
                break;
            default:
                return new List<byte[]>();
        }

        _lineAction = 0;
        // 页结束原始字节 0x0C（对应 JS Le=[12]）
        _bufferList.Push(ProtocolConstants.PageEndBytes);
        return _bufferList.ToByteArrayList();
    }

    /// <summary>
    /// 处理一行位图：裁剪尾部 0 字节，按状态机决定走纸或打印。
    /// 对应 JS <c>Se.printRow(t)</c>。
    /// </summary>
    private void PrintRow(byte[] row)
    {
        // 找到最后一个非零字节
        var last = row.Length - 1;
        while (last >= 0 && row[last] == 0) last--;
        if (last < 0)
        {
            // 全零行 → 走纸
            PrintLine(1);
            return;
        }

        var effLen = last + 1;
        switch (_lineAction)
        {
            case LineActionFeed:
                PushLine(_lineCount);
                break;
            case LineActionPrint:
                // 与上一行完全相同 → 重复，不发包
                if (_lineBytes == effLen && ArrayEquals(_lineData, row, effLen))
                {
                    _lineCount++;
                    return;
                }
                PushPrint();
                break;
        }

        _lineData = (byte[])row.Clone();
        _lineBytes = effLen;
        _lineCount = 1;
        _lineAction = LineActionPrint;
    }

    /// <summary>累积走纸行。对应 JS <c>Se.printLine(t)</c>。</summary>
    private void PrintLine(int count)
    {
        switch (_lineAction)
        {
            case LineActionFeed:
                _lineCount += count;
                return;
            case LineActionPrint:
                PushPrint();
                break;
            default:
                return;
        }
        _lineData = Array.Empty<byte>();
        _lineBytes = 0;
        _lineCount = count;
        _lineAction = LineActionFeed;
    }

    /// <summary>发送走纸指令（原始 ESC J n）。对应 JS <c>Se.pushLine(t)</c>。</summary>
    private void PushLine(int count)
    {
        if (count <= 0) return;
        SumLines += count;
        _prevData = Array.Empty<byte>();
        _prevBytes = 0;

        // ESC J n = [27, 74, n]，n 上限 255，超过分多次发送
        while (count >= ProtocolConstants.FeedLinesPerPacket)
        {
            _bufferList.Push(new byte[] { 27, 74, (byte)ProtocolConstants.FeedLinesPerPacket });
            count -= ProtocolConstants.FeedLinesPerPacket;
        }
        if (count > 0)
            _bufferList.Push(new byte[] { 27, 74, (byte)count });
    }

    /// <summary>
    /// 核心：对当前行计算所有 RLE 压缩大小，自动选最优发送。
    /// 对应 JS <c>Se.pushPrint()</c>。<b>位图帧不带 CRC</b>。
    /// </summary>
    private void PushPrint()
    {
        if (_lineCount <= 0) return;

        // 裁剪前导 0 字节
        var offset = 0;
        while (offset < _lineBytes && _lineData[offset] == 0) offset++;
        var effLen = _lineBytes - offset;

        // 计算各压缩方案输出长度（码数或字节数）
        byte[] rleC = Array.Empty<byte>(), rle5X = Array.Empty<byte>(),
              rle5D = Array.Empty<byte>(), rle6X = Array.Empty<byte>(),
              rle6D = Array.Empty<byte>();
        int lenC = 0, len5X = 0, len5D = 0, len6X = 0, len6D = 0;

        if (_supportSuperBitmap)
        {
            if ((_softwareFlags & SoftwareFlags.PCPDSF_RLEC_BITMAP) != 0)
            {
                rleC = new byte[_byteWidth + 4];
                lenC = RleEncoder.CalcRLEC(_lineData, _lineBytes, rleC, _byteWidth);
            }
            if ((_softwareFlags & SoftwareFlags.PCPDSF_RLE5_BITMAP) != 0)
            {
                rle5X = new byte[_byteWidth + 4];
                len5X = RleEncoder.CalcRLE5X(_lineData, _lineBytes, rle5X, _byteWidth);
                if (_prevData.Length > 0)
                {
                    rle5D = new byte[_byteWidth + 4];
                    len5D = RleEncoder.CalcRLE5D(_lineData, _lineBytes, _prevData, _prevBytes, rle5D, _byteWidth);
                }
            }
            if ((_softwareFlags & SoftwareFlags.PCPDSF_RLE6_BITMAP) != 0)
            {
                rle6X = new byte[_byteWidth + 4];
                len6X = RleEncoder.CalcRLE6X(_lineData, _lineBytes, rle6X, _byteWidth);
                if (_prevData.Length > 0)
                {
                    rle6D = new byte[_byteWidth + 4];
                    len6D = RleEncoder.CalcRLE6D(_lineData, _lineBytes, _prevData, _prevBytes, rle6D, _byteWidth);
                }
            }
        }

        // 各方案完整帧大小（不含 CRC，因为位图帧不带 CRC）
        // 原始位图：[1F][2B][EBV(offset)][EBV(effLen)][data] = 2 + EBV(offset) + EBV(effLen) + effLen
        var rawSize = (offset >= 192 ? 4 : 3) + (effLen >= 192 ? 2 : 1) + effLen;
        // 失败方案给一个大值（byteWidth+100）确保不被选中
        var big = _byteWidth + 100;

        var sizeC = lenC <= 0 ? big : lenC + (lenC >= 192 ? 4 : 3);
        var size5X = len5X <= 0 ? big : (5 * len5X + 7) / 8 + (len5X >= 192 ? 4 : 3);
        var size5D = len5D <= 0 ? big : (5 * len5D + 7) / 8 + (len5D >= 192 ? 4 : 3);
        var size6X = len6X <= 0 ? big : (6 * len6X + 7) / 8 + (len6X >= 192 ? 4 : 3);
        var size6D = len6D <= 0 ? big : (6 * len6D + 7) / 8 + (len6D >= 192 ? 4 : 3);

        // 选优（与 JS 条件链完全一致）
        if (size5D < rawSize && size5D < sizeC && size5D < size5X && size5D < size6X && size5D <= size6D)
        {
            SumRle5D++; Rle5DSaved += rawSize - size5D;
            PushRle5(PrinterCommand.CMD_BITMAP_P_RLED, rle5D, len5D);
        }
        else if (size6D < rawSize && size6D < sizeC && size6D < size5X && size6D < size6X)
        {
            SumRle6D++; Rle6DSaved += rawSize - size6D;
            PushRle6(PrinterCommand.CMD_BITMAP_P_RLE6D, rle6D, len6D);
        }
        else if (size5X < rawSize && size5X < sizeC && size5X <= size6X)
        {
            SumRle5X++; Rle5XSaved += rawSize - size5X;
            PushRle5(PrinterCommand.CMD_BITMAP_P_RLEX, rle5X, len5X);
        }
        else if (size6X < rawSize && size6X < sizeC)
        {
            SumRle6X++; Rle6XSaved += rawSize - size6X;
            PushRle6(PrinterCommand.CMD_BITMAP_P_RLE6X, rle6X, len6X);
        }
        else if (sizeC < rawSize)
        {
            SumRleC++; RleCSaved += rawSize - sizeC;
            PushRlec(PrinterCommand.CMD_BITMAP_P_RLEC, rleC, lenC);
        }
        else
        {
            // 原始位图帧：[1F][2B][EBV(offset)][EBV(effLen)][lineData[offset..lineBytes]]
            // JS 中 header 数组可动态扩展，最坏 2+2+2=6 字节
            SumPrints++;
            var header = new byte[6];
            header[0] = ProtocolConstants.HostToDeviceDataStart;
            header[1] = (byte)PrinterCommand.CMD_BITMAP_PRINT;
            var hdrLen = WriteEbvInline(header, 2, offset);
            hdrLen = WriteEbvInline(header, hdrLen, effLen);
            _bufferList.Push2(header, 0, hdrLen, _lineData, offset, _lineBytes);
        }

        // 行数 > 1 时发送重复指令
        if (_lineCount > 1) PushRepeat(_lineCount - 1);

        _prevData = (byte[])_lineData.Clone();
        _prevBytes = _lineBytes;
    }

    /// <summary>发送重复行指令。对应 JS <c>Se.pushRepeat(t)</c>。</summary>
    private void PushRepeat(int count)
    {
        if (count <= 0) return;
        SumRepeats += count;

        // [1F][2E][EBV(count-1)]，单包上限 16383；EBV 最坏 2 字节 → header 最长 4 字节
        // 注意：JS 数组按实际长度推送，C# 须按 hdrLen 截断，避免多推尾部 0
        while (count > ProtocolConstants.RepeatLinesPerPacket)
        {
            var header = new byte[4];
            header[0] = ProtocolConstants.HostToDeviceDataStart;
            header[1] = (byte)PrinterCommand.CMD_BITMAP_REPEAT;
            var hdrLen = WriteEbvInline(header, 2, ProtocolConstants.RepeatLinesPerPacket);
            _bufferList.Push(header, 0, hdrLen);
            count -= ProtocolConstants.RepeatLinesPerPacket + 1;
        }
        if (count > 0)
        {
            var header = new byte[4];
            header[0] = ProtocolConstants.HostToDeviceDataStart;
            header[1] = (byte)PrinterCommand.CMD_BITMAP_REPEAT;
            var hdrLen = WriteEbvInline(header, 2, count - 1);
            _bufferList.Push(header, 0, hdrLen);
        }
    }

    /// <summary>发送 RLEC 帧。对应 JS <c>Se.pushRLEC(t, e, i)</c>。</summary>
    private void PushRlec(PrinterCommand cmd, byte[] data, int len)
    {
        if (len <= 0) return;
        var header = new byte[4]; // [1F, cmd, EBV(len)]，EBV 最坏 2 字节
        header[0] = ProtocolConstants.HostToDeviceDataStart;
        header[1] = (byte)cmd;
        var hdrLen = WriteEbvInline(header, 2, len);
        _bufferList.Push2(header, 0, hdrLen, data, 0, len);
    }

    /// <summary>发送 RLE5 帧。对应 JS <c>Se.pushRLE5(t, e, i)</c>。</summary>
    private void PushRle5(PrinterCommand cmd, byte[] data, int codeCount)
    {
        if (codeCount <= 0) return;
        var header = new byte[4];
        header[0] = ProtocolConstants.HostToDeviceDataStart;
        header[1] = (byte)cmd;
        var hdrLen = WriteEbvInline(header, 2, codeCount);
        var packedLen = (5 * codeCount + 7) / 8;
        _bufferList.Push2(header, 0, hdrLen, data, 0, packedLen);
    }

    /// <summary>发送 RLE6 帧。对应 JS <c>Se.pushRLE6(t, e, i)</c>。</summary>
    private void PushRle6(PrinterCommand cmd, byte[] data, int codeCount)
    {
        if (codeCount <= 0) return;
        var header = new byte[4];
        header[0] = ProtocolConstants.HostToDeviceDataStart;
        header[1] = (byte)cmd;
        var hdrLen = WriteEbvInline(header, 2, codeCount);
        var packedLen = (6 * codeCount + 7) / 8;
        _bufferList.Push2(header, 0, hdrLen, data, 0, packedLen);
    }

    /// <summary>获取当前打印对齐标志位。对应 JS <c>Se.getPrintAlignment()</c>。</summary>
    public SoftwareFlags GetPrintAlignment() => _softwareFlags & SoftwareFlags.PCPDSF_PRTA_MASK;

    /// <summary>
    /// 计算像素灰度值。对应 JS <c>Se.getImageGrayValue(t, e)</c>。
    /// alpha &gt; 0 时：<c>(19661*R + 38666*G + 7209*B) &gt;&gt; 16</c>（等价 0.299/0.587/0.114）；
    /// 否则返回 255（透明→白）。
    /// </summary>
    public static int GetImageGrayValue(byte[] data, int index)
    {
        if (index + 3 >= data.Length) return 255;
        if (data[index + 3] <= 0) return 255;
        return (19661 * data[index] + 38666 * data[index + 1] + 7209 * data[index + 2]) >> 16;
    }

    /// <summary>内联 EBV 写入（对应 JS <c>Se.pushEBV(t, e, i)</c>），返回写入后的偏移。</summary>
    private static int WriteEbvInline(byte[] buffer, int offset, int value)
    {
        if (value >= ProtocolConstants.EbvThreshold)
        {
            buffer[offset] = (byte)((value >> 8) | 0xC0);
            buffer[offset + 1] = (byte)(value & 0xFF);
            return offset + 2;
        }
        buffer[offset] = (byte)value;
        return offset + 1;
    }

    private static bool ArrayEquals(byte[] a, byte[] b, int length)
    {
        for (var i = 0; i < length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}

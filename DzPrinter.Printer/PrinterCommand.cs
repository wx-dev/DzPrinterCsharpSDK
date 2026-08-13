// ReSharper disable InconsistentNaming
namespace DzPrinter.Printer;

/// <summary>
/// 打印机指令常量（对应 JS SDK 中 <c>xe</c> 类的 <c>CMD_*</c> 字段）。
/// 所有值与原始协议逐字节一致，勿随意修改。
/// </summary>
public enum PrinterCommand : byte
{
    CMD_NULL = 0,
    CMD_MCU_GETID = 10,
    CMD_MANUSHIPTIME = 17,

    // —— 页面相关 ——
    CMD_PAGE_START = 32,   // 页面开始（携带 pageKey）
    CMD_PAGE_PRINT = 33,   // 原始位图行打印
    CMD_PAGE_LINE = 34,   // 空行走纸
    CMD_PAGE_CONTROL = 35,
    CMD_PAGE_PARAM = 37,
    CMD_PAGE_HEIGHT = 38,
    CMD_PAGE_WIDTH = 39,   // 页面字节宽度
    CMD_PAGE_END = 40,

    // —— 位图压缩打印（与原 JS 完全一致）——
    CMD_BITMAP_P_RLEC = 41,   // RLEC 压缩
    CMD_SUB_ROM_UPGRADE = 41,   // 复用值 41
    CMD_BITMAP_PRINT = 43,   // 未压缩位图打印
    CMD_BITMAP_P_RLEX = 44,   // RLE5 单图压缩
    CMD_BITMAP_P_RLED = 45,   // RLE5 差分压缩
    CMD_BITMAP_REPEAT = 46,   // 重复上一行
    CMD_BITMAP_P_RLE6X = 60,   // RLE6 单图压缩
    CMD_BITMAP_P_RLE6D = 61,   // RLE6 差分压缩
    CMD_0x40 = 64,

    // —— 打印参数 ——
    CMD_GAP_TYPE = 66,
    CMD_DARKNESS = 67,
    CMD_SPEED = 68,
    CMD_GAP_LEN = 69,
    CMD_MOTORMODE = 71,
    CMD_AUTOPOWEROFF = 72,
    CMD_LANGUAGE = 73,
    CMD_SET_GENFLAGS = 77,
    CMD_COMMIT_PARAM = 79,
    CMD_CAP_GAPTYPE = 82,
    CMD_CAP_MOTORMODE = 87,
    CMD_CAP_LANGUAGE = 89,
    CMD_DEV_DISCOVERY = 90,
    CMD_POSITIONING = 92,
    CMD_ADDRESS_READ = 97,

    // —— 设备信息 ——
    CMD_PRINTER_DPI = 113,
    CMD_PRINTER_WIDTH = 114,
    CMD_PRINT_COUNTER = 115,
    CMD_MANUFACTURER = 117,
    CMD_BUFFER_STATE = 118,
    CMD_BUFFER_SIZE = 119,
    CMD_DEVICE_TYPE = 120,
    CMD_DEVICE_NAME = 121,
    CMD_DEVICE_VERSION = 122,
    CMD_BUFFER_SIZE2 = 123,
    CMD_SOFTWARE_VERSION = 124,
    CMD_DEVICE_DMINFO = 125,
    CMD_DEBUG_BUFFER = 127,
    CMD_ENABLE_SETTING = 128,
    CMD_PERIPHERALFLAGS = 131,
    CMD_HARDWARE_FLAGS = 132,
    CMD_REQ_ADCVALUE = 136,
    CMD_DEV_HANDSHAKE = 158,
    CMD_MANU_TOOLKIT = 159,

    CMD_IS_PRINTABLE = 112,

    // —— 外设类型子字段 ——
    CMD_PERIPHERALTYPE_FLAGS = 1,
    CMD_PERIPHERALTYPE_SPISPEED = 2,
}

/// <summary>
/// 打印机状态码（对应 JS 中 <c>xe.DZIP_*</c>）。
/// <see cref="DZIP_PRINTABLE"/>.. <see cref="DZIP_PAGENOTREADY"/> 视为可继续打印。
/// </summary>
public enum PrinterStatusCode : byte
{
    DZIP_PRINTABLE = 0,
    DZIP_ISPRINTING = 1,
    DZIP_ISROTATING = 2,
    DZIP_NOJOB = 10,
    DZIP_PAGENOTREADY = 11,
    DZIP_JOBCANCELED = 12,
    DZIP_ENVNOTREADY = 20,
    DZIP_VOLTOOLOW = 30,
    DZIP_VOLTOOHIGH = 31,
    DZIP_TPHNOTFOUND = 32,
    DZIP_TPHTOOHOT = 33,
    DZIP_COVEROPENED = 34,
    DZIP_NO_PAPER = 35,
    DZIP_RIBBONCANOPENED = 36,
    DZIP_NO_RIBBON = 37,
    DZIP_UNMATCHED_RIBBON = 38,
    DZIP_TPHTOOCOLD = 39,
    DZIP_USEDUP_RIBBON = 40,
    DZIP_USEDUP_RIBBON2 = 41,
    DZIP_NO_LABEL = 42,
    DZIP_UNMATCHED_LABEL = 43,
    DZIP_USEDUP_LABEL = 44,
    DZIP_NO_RIBBON2 = 45,
    DZIP_UNMATCHED_RIBBON2 = 46,
    DZIP_LABELCANOPENED = 50,
}

/// <summary>
/// 客户端类型（对应 JS <c>xe.DZCT_*</c>）。
/// </summary>
public enum ClientType : byte
{
    DZCT_ANDROID_APP = 0,
    DZCT_ANDROID_BLE = 6,
    DZCT_ANDIOS_DJGW = 12,
    DZCT_ANDROID_USB = 16,
}

/// <summary>
/// 软件能力/配置标志位（对应 JS <c>xe.PCPDSF_*</c>）。
/// RLE 位图压缩能力由 <see cref="PCPDSF_RLE5_BITMAP"/> / <see cref="PCPDSF_RLE6_BITMAP"/>
/// / <see cref="PCPDSF_RLEC_BITMAP"/> 三位决定，掩码 <see cref="PCPDSF_MASK_BITMAP"/>。
/// </summary>
[Flags]
public enum SoftwareFlags : uint
{
    None = 0,
    PCPDSF_MOTOR_ANTIDIR = 0x00000001,
    PCPDSF_RLE5_BITMAP = 0x00000010,   // 支持 RLE5 压缩
    PCPDSF_RLE6_BITMAP = 0x00000020,   // 支持 RLE6 压缩
    PCPDSF_MASK_BITMAP = 0x000000F0,   // 位图压缩能力掩码
    PCPDSF_RLEC_BITMAP = 0x00000080,   // 支持 RLEC 压缩
    PCPDSF_BT_HARD_FC = 0x00000100,   // 蓝牙硬件流控
    PCPDSF_PRTA_RIGHT = 0x00000000,   // 右对齐
    PCPDSF_PRTA_CENTER = 0x00000200,   // 居中对齐
    PCPDSF_PRTA_LEFT = 0x00000400,   // 左对齐
    PCPDSF_PRTA_MASK = 0x00000600,   // 对齐掩码
    PCPDSF_ROTATE_180 = 0x00004000,   // 旋转 180°
    PCPDSF_NO_AUTO_OUT = 0x10000000,
}

/// <summary>
/// 硬件能力标志位（对应 JS <c>xe.PCPDHF_*</c>）。
/// </summary>
[Flags]
public enum HardwareFlags : uint
{
    None = 0,
    PCPDHF_SUPER_BITMAP = 0x00000010,
    PCPDHF_GRAY_BITMAP = 0x00000020,
    PCPDHF_UHFRFID_WRITOR = 0x00000002,
    PCPDHF_HFRFID_WRITOR = 0x00000004,
    PCPDHF_NFCRFID_WRITOR = 0x00000006,
    PCPDHF_MSKRFID_WRITOR = 0x00000006,
    PCPDHF_HAS_BEEP = 0x00004000,
    PCPDHF_BLUETOOTH_2 = 0x00020000,
}

/// <summary>
/// 适配器标志位（对应 JS <c>xe.PCPDAF_*</c>）。
/// </summary>
[Flags]
public enum AdapterFlags : uint
{
    None = 0,
    PCPDAF_BT_HARD_FC = 0x00000001,
    PCPDAF_STRING_GBK = 0x00000002,
    PCPDAF_RLEC_BITMAP = 0x00000080,
    PCPDAF_HAS_WIFI = 0x00001000,
}

/// <summary>
/// ADC 事件类型（对应 JS <c>xe.ADCEVT_*</c>）。
/// </summary>
[Flags]
public enum AdcEvent : byte
{
    None = 0,
    ADCEVT_POWER = 0x01,
    ADCEVT_TPHTM = 0x02,
}

/// <summary>
/// 数据发送模式（对应 JS <c>EDataSendMode</c>）。
/// 决定打印队列是按"可打印信号"还是按"页键(PageKey)"推进。
/// </summary>
[Flags]
public enum DataSendMode : byte
{
    None = 0,
    Printable = 1,
    PageKey = 2,
}

/// <summary>
/// 打印任务状态（对应 JS <c>EPrintStatus</c>）。
/// </summary>
public enum PrintStatus : byte
{
    None = 0,
    Connected = 2,
    Checking = 3,
    ReadyPrint = 4,
    Sending = 5,
    Printing = 6,
    Paused = 7,
    Cancel = 8,
}

/// <summary>
/// 打印对齐方式（由 <see cref="SoftwareFlags"/> 对齐位映射而来）。
/// 原 JS 中以 <c>Te=1024</c>(左)、<c>Oe=1536</c>(掩码)、<c>512</c>(居中) 表示。
/// </summary>
public enum PrintAlignment : byte
{
    /// <summary>右对齐（默认）。</summary>
    Right = 0,
    /// <summary>居中对齐。</summary>
    Center = 2,
    /// <summary>左对齐。</summary>
    Left = 4,
}

/// <summary>
/// 协议层静态常量集合（对应 JS 中分散在 <c>be</c> / <c>Se</c> / <c>xe</c> 上的静态字段）。
/// </summary>
public static class ProtocolConstants
{
    /// <summary>主机→设备 数据起始符（0x1F）。JS: <c>be.HOST_TO_DEVICE_DATA_START</c>。</summary>
    public const byte HostToDeviceDataStart = 0x1F;

    /// <summary>设备→主机 数据起始符（0x1F）。JS: <c>be.DEVICE_TO_HOST_DATA_START</c>。</summary>
    public const byte DeviceToHostDataStart = 0x1F;

    /// <summary>
    /// 固定 CRC 结果（0x88）。JS: <c>be.FIXED_PACKAGE_CRC_RESULT</c>。
    /// <b>发送时</b>统一填此值（设备同时接受计算 CRC 与固定值，见 <see cref="EbvHelper.CalcCrc"/>）。
    /// </summary>
    public const byte FixedPackageCrcResult = 0x88;

    /// <summary>EBV 编码阈值：值 &lt; 192 用单字节，否则双字节。JS: 多处 <c>192</c> 判断。</summary>
    public const int EbvThreshold = 192;

    /// <summary>EBV 可表达的最大值（14 位，0x3FFF）。JS: <c>Se.MAX_EBV_VALUE</c>。</summary>
    public const int MaxEbvValue = 16383;

    /// <summary>二值化阈值默认值。JS: <c>Se.THRESHOLD_DEFAULT</c>。</summary>
    public const int ThresholdDefault = 150;

    /// <summary>默认打印 DPI。JS: <c>Se.PRINTER_DPI_DEFAULT</c>。</summary>
    public const int PrinterDpiDefault = 203;

    /// <summary>默认打印机像素宽度。JS: <c>Se.PRINTER_WIDTH_DEFAULT</c>。</summary>
    public const int PrinterWidthDefault = 384;

    /// <summary>PackageBuffer 默认容量。JS: <c>ve.BUFFER_LENGTH_DEFAULT</c>。</summary>
    public const int PackageBufferDefaultLength = 1000;

    /// <summary>走纸指令单包最大行数（ESC J n 中 n 上限 255）。JS: <c>pushLine</c>。</summary>
    public const int FeedLinesPerPacket = 255;

    /// <summary>重复行指令单包最大计数值。JS: <c>pushRepeat</c>。</summary>
    public const int RepeatLinesPerPacket = 16383;

    /// <summary>
    /// 页结束原始字节序列（ESC/POS 形式走纸 0x0C）。JS: <c>Le=[12]</c>。
    /// 注意：这不是协议帧，而是直接拼接到字节流的原始字节。
    /// </summary>
    public static readonly byte[] PageEndBytes = { 0x0C };
}

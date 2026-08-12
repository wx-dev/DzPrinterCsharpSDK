namespace DzPrinter.Barcode;

/// <summary>
/// 条码类型枚举。对应 JS SDK 中 <c>t.BarcodeType</c>。
/// 数值与 JS 完全一致，用于协议帧中标识条码类型。
/// </summary>
public enum BarcodeType
{
    /// <summary>UPC-A（12 位数字）。</summary>
    UPC_A = 20,

    /// <summary>UPC-E（压缩 8 位）。</summary>
    UPC_E = 21,

    /// <summary>EAN-13（13 位数字）。</summary>
    EAN13 = 22,

    /// <summary>EAN-8（8 位数字）。</summary>
    EAN8 = 23,

    /// <summary>Code39（字母数字）。</summary>
    CODE39 = 24,

    /// <summary>ITF25（交叉 25 码）。</summary>
    ITF25 = 25,

    /// <summary>CODABAR（库德巴码）。</summary>
    CODABAR = 26,

    /// <summary>Code93。</summary>
    CODE93 = 27,

    /// <summary>Code128。</summary>
    CODE128 = 28,

    /// <summary>ISBN（图书编号，基于 EAN-13）。</summary>
    ISBN = 29,

    /// <summary>扩展 Code39（ECODE39）。</summary>
    ECODE39 = 30,

    /// <summary>ITF14（14 位交叉 25 码）。</summary>
    ITF14 = 31,

    /// <summary>中国邮政码（ChinaPost）。</summary>
    ChinaPost = 32,

    /// <summary>矩阵 25 码（Matrix25）。</summary>
    Matrix25 = 33,

    /// <summary>工业 25 码（Industrial25）。</summary>
    Industrial25 = 34,

    /// <summary>GS1-128（同 EAN128）。</summary>
    GS1_128 = 35,

    /// <summary>EAN-128（GS1-128 别名，与 GS1_128 同值）。</summary>
    EAN128 = 35,

    /// <summary>自动选择最合适的条码类型。</summary>
    AUTO = 60
}

/// <summary>
/// 二维码纠错等级。对应 JS SDK 中 <c>t.EccLevel</c>。
/// </summary>
public enum EccLevel
{
    /// <summary>L（约 7% 纠错）。</summary>
    Low = 0,

    /// <summary>M（约 15% 纠错）。</summary>
    Middle = 1,

    /// <summary>Q（约 25% 纠错）。</summary>
    Quality = 2,

    /// <summary>H（约 30% 纠错）。</summary>
    High = 3
}

/// <summary>
/// 二维码类型常量。对应 JS SDK 中 <c>de</c> 冻结对象。
/// </summary>
public static class TwoDBarcodeKind
{
    /// <summary>自动选择。</summary>
    public const string Auto = "auto";

    /// <summary>QR 码。</summary>
    public const string QRCode = "qrcode";

    /// <summary>PDF417 码。</summary>
    public const string PDF417 = "pdf417";

    /// <summary>DataMatrix 码。</summary>
    public const string DMCode = "dataMatrix";

    /// <summary>GridMatrix 码。</summary>
    public const string GMCode = "gridMatrix";
}

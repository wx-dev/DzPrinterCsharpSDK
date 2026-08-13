namespace DzPrinter.Printer;

// =====================================================================
//  打印机相关枚举。对应 JS SDK 中通过 (function(t){...})(t.Enum||...)
//  或 Object.freeze({...}) 模式定义的所有打印机相关枚举。
//  数值与 JS 完全一致，用于协议帧、设备管理与 API 调用。
// =====================================================================

/// <summary>
/// 设备类型。对应 JS <c>t.LPA_DeviceType</c>。
/// </summary>
public enum LpaDeviceType
{
    /// <summary>自动检测。JS: AUTO=0。</summary>
    Auto = 0,

    /// <summary>Web BLE。JS: WEB_BLE=100。</summary>
    WebBle = 100,

    /// <summary>Web HID。JS: WEB_HID=101。</summary>
    WebHid = 101
}

/// <summary>
/// 设备连接类型。对应 JS <c>t.LPA_DeviceConnectType</c>。
/// </summary>
public enum LpaDeviceConnectType
{
    /// <summary>无。JS: None=0。</summary>
    None = 0,

    /// <summary>连接。JS: Connect=1。</summary>
    Connect = 1,

    /// <summary>断开。JS: Disconnect=2。</summary>
    Disconnect = 2
}

/// <summary>
/// GATT 属性（位标志）。对应 JS <c>t.LPA_GattProperty</c>。
/// </summary>
[Flags]
public enum LpaGattProperty
{
    /// <summary>广播。JS: BROADCAST=1。</summary>
    Broadcast = 1,

    /// <summary>读。JS: READ=2。</summary>
    Read = 2,

    /// <summary>无响应写。JS: WRITE_NO_RESPONSE=4。</summary>
    WriteNoResponse = 4,

    /// <summary>写。JS: WRITE=8。</summary>
    Write = 8,

    /// <summary>通知。JS: NOTIFY=16。</summary>
    Notify = 16,

    /// <summary>指示。JS: INDICATE=32。</summary>
    Indicate = 32
}

/// <summary>
/// 特征属性（位标志）。对应 JS <c>t.CharacteristicProperty</c>。
/// 与 <see cref="LpaGattProperty"/> 值相同。
/// </summary>
[Flags]
public enum CharacteristicProperty
{
    /// <summary>广播。JS: BROADCAST=1。</summary>
    Broadcast = 1,

    /// <summary>读。JS: READ=2。</summary>
    Read = 2,

    /// <summary>无响应写。JS: WRITE_NO_RESPONSE=4。</summary>
    WriteNoResponse = 4,

    /// <summary>写。JS: WRITE=8。</summary>
    Write = 8,

    /// <summary>通知。JS: NOTIFY=16。</summary>
    Notify = 16,

    /// <summary>指示。JS: INDICATE=32。</summary>
    Indicate = 32
}

/// <summary>
/// 值类型。对应 JS <c>t.ValueType</c>。
/// </summary>
public enum ValueType
{
    /// <summary>二进制。JS: Binary=1。</summary>
    Binary = 1,

    /// <summary>十六进制。JS: Hex16=2。</summary>
    Hex16 = 2,

    /// <summary>Base64。JS: Base64=3。</summary>
    Base64 = 3
}

/// <summary>
/// 作业动作（位标志）。对应 JS <c>t.LPA_JobAction</c>。
/// </summary>
[Flags]
public enum LpaJobAction
{
    /// <summary>原始数据。JS: RawData=1。</summary>
    RawData = 1,

    /// <summary>预览。JS: Preview=2。</summary>
    Preview = 2,

    /// <summary>图像。JS: Image=32。</summary>
    Image = 32,

    /// <summary>图像数据。JS: ImageData=33。</summary>
    ImageData = 33,

    /// <summary>透明。JS: Transparent=128。</summary>
    Transparent = 128,

    /// <summary>透明预览。JS: TransPrev=130。</summary>
    TransPrev = 130,

    /// <summary>打印。JS: Print=4096。</summary>
    Print = 4096
}

/// <summary>
/// 作业名称常量。对应 JS <c>t.LPA_JobName</c>。
/// JS 中是字符串常量对象，C# 用静态类。
/// </summary>
public static class LpaJobName
{
    /// <summary>透明作业名。JS: JOB_NAME_TRANS。</summary>
    public const string JobNameTrans = "#!#transparent#!#";

    /// <summary>预览作业名。JS: JOB_NAME_PREV。</summary>
    public const string JobNamePrev = "#!#preview#!#";

    /// <summary>原始数据作业名。JS: JOB_NAME_DATA。</summary>
    public const string JobNameData = "#!#rawdata#!#";

    /// <summary>图像数据作业名。JS: JOB_NAME_DATA_IMAGE。</summary>
    public const string JobNameDataImage = "#!#imagedata#!#";
}

/// <summary>
/// API 操作结果。对应 JS <c>t.LPA_Result</c>。
/// </summary>
public enum LpaResult
{
    /// <summary>异步等待中。JS: ASYNC_WAIT=-1。</summary>
    AsyncWait = -1,

    /// <summary>成功。JS: OK=0。</summary>
    Ok = 0,

    /// <summary>参数错误。JS: ERROR_PARAM=1。</summary>
    ErrorParam = 1,

    /// <summary>无打印机。JS: ERROR_NO_PRINTER=2。</summary>
    ErrorNoPrinter = 2,

    /// <summary>已断开。JS: ERROR_DISCONNECTED=3。</summary>
    ErrorDisconnected = 3,

    /// <summary>连接失败。JS: ERROR_CONNECT_FAILED=4。</summary>
    ErrorConnectFailed = 4,

    /// <summary>获取服务失败。JS: ERROR_GET_SERVICE=5。</summary>
    ErrorGetService = 5,

    /// <summary>获取特征失败。JS: ERROR_GET_CHARACTERISTIC=6。</summary>
    ErrorGetCharacteristic = 6,

    /// <summary>打开适配器失败。JS: ERROR_OPEN_ADAPTER=7。</summary>
    ErrorOpenAdapter = 7,

    /// <summary>数据发送错误。JS: ERROR_DATA_SEND_ERROR=8。</summary>
    ErrorDataSendError = 8,

    /// <summary>数据接收错误。JS: ERROR_DATA_RECEIVE_ERROR=9。</summary>
    ErrorDataReceiveError = 9,

    /// <summary>正在打印。JS: ERROR_IS_PRINTING=10。</summary>
    ErrorIsPrinting = 10,

    /// <summary>响应超时。JS: ERROR_RESPONSE_TIMEOUT=11。</summary>
    ErrorResponseTimeout = 11,

    /// <summary>打印已取消。JS: ERROR_PRINTER_CANCELED=12。</summary>
    ErrorPrinterCanceled = 12,

    /// <summary>作业创建失败。JS: ERROR_JOB_CREATE=13。</summary>
    ErrorJobCreate = 13,

    /// <summary>作业已取消。JS: ERROR_JOB_CANCELED=14。</summary>
    ErrorJobCanceled = 14,

    /// <summary>获取图像数据失败。JS: ERROR_GET_IMAGE_DATA=15。</summary>
    ErrorGetImageData = 15,

    /// <summary>打印机不可用。JS: ERROR_PRINTER_NOT_AVAILABLE=16。</summary>
    ErrorPrinterNotAvailable = 16,

    /// <summary>数据解析错误。JS: ERROR_DATA_PARSE=17。</summary>
    ErrorDataParse = 17,

    /// <summary>未实现。JS: ERROR_NO_IMPLEMENT=18。</summary>
    ErrorNoImplement = 18,

    /// <summary>不支持。JS: ERROR_UN_SUPPORTED=19。</summary>
    ErrorUnSupported = 19,

    /// <summary>通知特征失败。JS: ERROR_NOTIFY_CHARACTERISTIC=21。</summary>
    ErrorNotifyCharacteristic = 21,

    /// <summary>读取特征失败。JS: ERROR_READ_CHARACTERISTIC=22。</summary>
    ErrorReadCharacteristic = 22,

    /// <summary>写入特征失败。JS: ERROR_WRITE_CHARACTERISTIC=23。</summary>
    ErrorWriteCharacteristic = 23,

    /// <summary>认证失败。JS: ERROR_AUTH_FAILED=24。</summary>
    ErrorAuthFailed = 24,

    /// <summary>已取消。JS: ERROR_CANCEL=25。</summary>
    ErrorCancel = 25,

    /// <summary>其他错误。JS: ERROR_OTHER=100。</summary>
    ErrorOther = 100,

    /// <summary>异常。JS: ERROR_EXCEPTION=101。</summary>
    ErrorException = 101,

    /// <summary>无桥接。JS: ERROR_NO_BRIDGE=102。</summary>
    ErrorNoBridge = 102
}

/// <summary>
/// 标签间隙类型。对应 JS <c>t.LPA_GapType</c>。
/// </summary>
public enum LpaGapType
{
    /// <summary>未设置。JS: Unset=255。</summary>
    Unset = 255,

    /// <summary>无。JS: None=0。</summary>
    None = 0,

    /// <summary>孔洞。JS: Hole=1。</summary>
    Hole = 1,

    /// <summary>间隙。JS: Gap=2。</summary>
    Gap = 2,

    /// <summary>黑标。JS: Black=3。</summary>
    Black = 3,

    /// <summary>透明。JS: Trans=4。</summary>
    Trans = 4
}

/// <summary>
/// 打印速度。对应 JS <c>t.LPA_PrintSpeed</c>。
/// </summary>
public enum LpaPrintSpeed
{
    /// <summary>未设置。JS: Unset=255。</summary>
    Unset = 255,

    /// <summary>最小。JS: Min=1。</summary>
    Min = 1,

    /// <summary>低。JS: Low=2。</summary>
    Low = 2,

    /// <summary>正常。JS: Normal=3。</summary>
    Normal = 3,

    /// <summary>高。JS: High=4。</summary>
    High = 4,

    /// <summary>最大。JS: Max=5。</summary>
    Max = 5
}

/// <summary>
/// 打印浓度。对应 JS <c>t.LPA_PrintDarkness</c>。
/// </summary>
public enum LpaPrintDarkness
{
    /// <summary>未设置。JS: Unset=255。</summary>
    Unset = 255,

    /// <summary>最小。JS: Min=1。</summary>
    Min = 1,

    /// <summary>低。JS: Low=4。</summary>
    Low = 4,

    /// <summary>正常。JS: Normal=6。</summary>
    Normal = 6,

    /// <summary>高。JS: High=10。</summary>
    High = 10,

    /// <summary>最大。JS: Max=15。</summary>
    Max = 15
}

/// <summary>
/// 1D 条码类型。对应 JS <c>t.BarcodeType</c>。
/// </summary>
public enum BarcodeType
{
    /// <summary>UPC-A。JS: UPC_A=20。</summary>
    UpcA = 20,

    /// <summary>UPC-E。JS: UPC_E=21。</summary>
    UpcE = 21,

    /// <summary>EAN-13。JS: EAN13=22。</summary>
    Ean13 = 22,

    /// <summary>EAN-8。JS: EAN8=23。</summary>
    Ean8 = 23,

    /// <summary>Code39。JS: CODE39=24。</summary>
    Code39 = 24,

    /// <summary>ITF25。JS: ITF25=25。</summary>
    Itf25 = 25,

    /// <summary>Codabar。JS: CODABAR=26。</summary>
    Codabar = 26,

    /// <summary>Code93。JS: CODE93=27。</summary>
    Code93 = 27,

    /// <summary>Code128。JS: CODE128=28。</summary>
    Code128 = 28,

    /// <summary>ISBN。JS: ISBN=29。</summary>
    Isbn = 29,

    /// <summary>扩展 Code39。JS: ECODE39=30。</summary>
    ECode39 = 30,

    /// <summary>ITF-14。JS: ITF14=31。</summary>
    Itf14 = 31,

    /// <summary>中国邮政。JS: ChinaPost=32。</summary>
    ChinaPost = 32,

    /// <summary>Matrix25。JS: Matrix25=33。</summary>
    Matrix25 = 33,

    /// <summary>Industrial25。JS: Industrial25=34。</summary>
    Industrial25 = 34,

    /// <summary>GS1-128。JS: GS1_128=35。</summary>
    GS1_128 = 35,

    /// <summary>EAN-128（同 GS1_128）。JS: EAN128=35。</summary>
    EAN128 = 35,

    /// <summary>自动。JS: AUTO=60。</summary>
    Auto = 60
}

/// <summary>
/// 2D 条码类型。对应 JS <c>t.Barcode2DType</c>（Object.freeze 字符串常量）。
/// </summary>
public static class Barcode2DType
{
    /// <summary>自动。JS: Auto="auto"。</summary>
    public const string Auto = "auto";

    /// <summary>QR 码。JS: QRCode="qrcode"。</summary>
    public const string QRCode = "qrcode";

    /// <summary>PDF417。JS: PDF417="pdf417"。</summary>
    public const string PDF417 = "pdf417";

    /// <summary>DataMatrix。JS: DMCode="dataMatrix"。</summary>
    public const string DMCode = "dataMatrix";

    /// <summary>GridMatrix。JS: GMCode="gridMatrix"。</summary>
    public const string GMCode = "gridMatrix";
}

/// <summary>
/// 绘制类型。对应 JS <c>t.DrawType</c>（Object.freeze 字符串常量）。
/// </summary>
public static class DrawType
{
    /// <summary>文本。JS: text="text"。</summary>
    public const string Text = "text";

    /// <summary>1D 条码。JS: barcode="barcode"。</summary>
    public const string Barcode = "barcode";

    /// <summary>QR 码。JS: qrcode="qrcode"。</summary>
    public const string QRCode = "qrcode";

    /// <summary>PDF417。JS: pdf417="pdf417"。</summary>
    public const string PDF417 = "pdf417";

    /// <summary>DataMatrix。JS: dataMatrix="dataMatrix"。</summary>
    public const string DataMatrix = "dataMatrix";

    /// <summary>DataMatrix（下划线别名）。JS: data_matrix="datamatrix"。</summary>
    public const string DataMatrixAlt = "datamatrix";

    /// <summary>GridMatrix。JS: gridMatrix="gridMatrix"。</summary>
    public const string GridMatrix = "gridMatrix";

    /// <summary>GridMatrix（下划线别名）。JS: grid_matrix="gridmatrix"。</summary>
    public const string GridMatrixAlt = "gridmatrix";

    /// <summary>图像。JS: image="image"。</summary>
    public const string Image = "image";

    /// <summary>矩形。JS: rect="rect"。</summary>
    public const string Rect = "rect";

    /// <summary>矩形（别名）。JS: rectangle="rectangle"。</summary>
    public const string Rectangle = "rectangle";

    /// <summary>椭圆。JS: ellipse="ellipse"。</summary>
    public const string Ellipse = "ellipse";

    /// <summary>圆。JS: circle="circle"。</summary>
    public const string Circle = "circle";

    /// <summary>直线。JS: line="line"。</summary>
    public const string Line = "line";

    /// <summary>表格。JS: table="table"。</summary>
    public const string Table = "table";

    /// <summary>弧形文本。JS: arcText="arcText"。</summary>
    public const string ArcText = "arcText";

    /// <summary>弧形文本（下划线别名）。JS: arc_text="arctext"。</summary>
    public const string ArcTextAlt = "arctext";

    /// <summary>HTML。JS: html="html"。</summary>
    public const string Html = "html";
}

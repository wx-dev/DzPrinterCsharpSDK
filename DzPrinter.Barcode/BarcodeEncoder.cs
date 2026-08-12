namespace DzPrinter.Barcode;

/// <summary>
/// 条码编码统一入口（门面）。整合 1D 与 2D 条码的创建 API。
/// </summary>
/// <remarks>
/// JS SDK 中没有等价的"统一入口"类：1D 走 <c>_t.create1DBarcode(e)</c>，
/// 2D 走 <c>ue.create2DBarcode(t)</c>。本类为 C# 端为方便上层调用而引入的门面，
/// 内部全部转调 <see cref="Barcode1DCreator"/> 与 <see cref="Barcode2DCreator"/>，
/// 不引入任何额外算法或副作用，仅做转发与请求分发。
/// 使用方式：
/// <list type="bullet">
///   <item>已知条码维度：直接调用 <see cref="Create1D"/> / <see cref="Create2D"/> / <see cref="CreateQR"/> 等。</item>
///   <item>请求对象类型已知：调用 <see cref="Create(Barcode1DRequest)"/> 或 <see cref="Create(Barcode2DRequest)"/> 重载。</item>
///   <item>运行期多态：调用 <see cref="Create(object)"/>，按运行时类型分发。</item>
/// </list>
/// </remarks>
public static class BarcodeEncoder
{
    // ---------------------------------------------------------------------
    // 1D 转发
    // ---------------------------------------------------------------------

    /// <summary>
    /// 创建 1D 条码。转调 <see cref="Barcode1DCreator.Create1DBarcode"/>。
    /// 对应 JS <c>_t.create1DBarcode(e)</c>。
    /// </summary>
    /// <param name="request">1D 条码创建请求。</param>
    /// <returns>编码结果；文本为空或编码失败时返回 null（与 JS 返回 undefined 对应）。</returns>
    public static BarcodeEncodeResult? Create1D(Barcode1DRequest request)
        => Barcode1DCreator.Create1DBarcode(request);

    /// <summary>
    /// 注册 1D 自定义编码器。转调 <see cref="Barcode1DCreator.RegisterBarcodeCreator"/>。
    /// 对应 JS <c>_t.registerBarcodeCreator(t, e)</c>。
    /// </summary>
    public static void Register1DEncoder(BarcodeType type, IBarcodeEncoder encoder)
        => Barcode1DCreator.RegisterBarcodeCreator(type, encoder);

    // ---------------------------------------------------------------------
    // 2D 转发
    // ---------------------------------------------------------------------

    /// <summary>
    /// 创建 2D 条码（通用入口）。转调 <see cref="Barcode2DCreator.Create2DBarcode"/>。
    /// 对应 JS <c>ue.create2DBarcode(t)</c>。根据 <see cref="Barcode2DRequest.BarcodeType"/>
    /// 查找已注册的 2D 编码器；未注册时回退到 QR 码。
    /// </summary>
    public static BitMatrix? Create2D(Barcode2DRequest request)
        => Barcode2DCreator.Create2DBarcode(request);

    /// <summary>
    /// 创建 QR 码。转调 <see cref="Barcode2DCreator.CreateQRCode"/>。
    /// 对应 JS <c>ue.createQRCode(t)</c>。
    /// </summary>
    public static BitMatrix? CreateQR(Barcode2DRequest request)
        => Barcode2DCreator.CreateQRCode(request);

    /// <summary>
    /// 创建 PDF417 码。转调 <see cref="Barcode2DCreator.CreatePDF417"/>。
    /// 对应 JS <c>ue.createPDF417(t)</c>。
    /// </summary>
    public static BitMatrix? CreatePDF417(Barcode2DRequest request)
        => Barcode2DCreator.CreatePDF417(request);

    /// <summary>
    /// 创建 DataMatrix 码。转调 <see cref="Barcode2DCreator.CreateDataMatrix"/>。
    /// 对应 JS <c>ue.createDataMatrix(t)</c>。
    /// </summary>
    public static BitMatrix? CreateDataMatrix(Barcode2DRequest request)
        => Barcode2DCreator.CreateDataMatrix(request);

    /// <summary>
    /// 创建 GridMatrix 码。转调 <see cref="Barcode2DCreator.CreateGridMatrix"/>。
    /// 对应 JS <c>ue.createGridMatrix(t)</c>。
    /// </summary>
    public static BitMatrix? CreateGridMatrix(Barcode2DRequest request)
        => Barcode2DCreator.CreateGridMatrix(request);

    /// <summary>
    /// 注册 2D 自定义编码器。转调 <see cref="Barcode2DCreator.SetEncoder"/>。
    /// 对应 JS <c>ue.setEncoder(t, e)</c>。
    /// </summary>
    /// <returns>是否注册成功（类型为空或编码器为 null 时返回 false）。</returns>
    public static bool Register2DEncoder(string? type, IBarcode2DEncoder? encoder)
        => Barcode2DCreator.SetEncoder(type, encoder);

    // ---------------------------------------------------------------------
    // 统一分发入口
    // ---------------------------------------------------------------------

    /// <summary>
    /// 强类型分发：1D 请求转 <see cref="Create1D"/>。
    /// 便于上层使用统一 <c>Create</c> 方法名而无需关心具体维度。
    /// </summary>
    public static BarcodeEncodeResult? Create(Barcode1DRequest request)
        => Create1D(request);

    /// <summary>
    /// 强类型分发：2D 请求转 <see cref="Create2D"/>。
    /// 便于上层使用统一 <c>Create</c> 方法名而无需关心具体维度。
    /// </summary>
    public static BitMatrix? Create(Barcode2DRequest request)
        => Create2D(request);

    /// <summary>
    /// 运行期多态分发。按请求对象的运行时类型分发到对应的具体创建方法。
    /// 用于上层持有 <see cref="object"/> 引用、或序列化反序列化后类型不确定的场景。
    /// </summary>
    /// <param name="request">
    /// 请求对象，需为 <see cref="Barcode1DRequest"/> 或 <see cref="Barcode2DRequest"/>；
    /// 其他类型（含 null）返回 null。
    /// </param>
    /// <returns>
    /// 成功时为 <see cref="BarcodeEncodeResult"/>（1D）或 <see cref="BitMatrix"/>（2D）；
    /// 类型未知或编码失败时返回 null。
    /// </returns>
    public static object? Create(object? request)
    {
        // 注意：使用模式匹配按运行时类型分发，避免使用反射以保持简单与高性能。
        // JS 无对应方法，本分发为 C# 端为方便统一调用而设计。
        return request switch
        {
            Barcode1DRequest r1 => Create1D(r1),
            Barcode2DRequest r2 => Create2D(r2),
            _ => null
        };
    }
}

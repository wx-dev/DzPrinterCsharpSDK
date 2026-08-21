using DzPrinter.Core;

namespace DzPrinter.Barcode;

/// <summary>
/// 自定义 2D 条码编码器接口。对应 JS 中 <c>ue.setEncoder</c> 接受的带 <c>encode(t)</c> 方法的对象。
/// 实现类用于 PDF417/DataMatrix/GridMatrix 等 2D 条码的编码。
/// </summary>
public interface IBarcode2DEncoder
{
    /// <summary>
    /// 编码生成 2D 条码矩阵。对应 JS <c>encoder.encode(t)</c>。
    /// </summary>
    /// <param name="request">创建请求（含文本、类型等）。</param>
    /// <returns>位矩阵；失败返回 null。</returns>
    BitMatrix? Encode(Barcode2DRequest request);
}

/// <summary>
/// 2D 条码创建器（统一入口）。对应 JS SDK 中 <c>ue</c> 类。
/// 提供 QR/PDF417/DataMatrix/GridMatrix 等 2D 条码的创建与编码器注册。
/// </summary>
public static class Barcode2DCreator
{
    /// <summary>
    /// 已注册的 2D 编码器映射。对应 JS <c>ue.barcodeCreatorMap = {}</c>。
    /// 键为类型字符串（大写），值为编码器实例。
    /// </summary>
    private static readonly Dictionary<string, IBarcode2DEncoder> s_encoderMap = new();

    /// <summary>
    /// 静态构造：注册内置 2D 编码器。
    /// </summary>
    static Barcode2DCreator()
    {
        s_encoderMap[TwoDBarcodeKind.PDF417.ToUpperInvariant()] = new Pdf417Encoder();
        s_encoderMap[TwoDBarcodeKind.DMCode.ToUpperInvariant()] = new DataMatrixEncoder();
        s_encoderMap[TwoDBarcodeKind.GMCode.ToUpperInvariant()] = new GridMatrixEncoder();
    }

    /// <summary>
    /// 获取指定类型的 2D 编码器。对应 JS <c>ue.getEncoder(t)</c>。
    /// </summary>
    /// <param name="type">类型字符串（不区分大小写）。</param>
    public static IBarcode2DEncoder? GetEncoder(string? type)
    {
        if (type == null) return null;
        var upper = type.ToUpperInvariant();
        s_encoderMap.TryGetValue(upper, out var encoder);
        DzLogger.Debug($"---- getBarcode2DEncoder[{upper}]: {encoder != null}");
        return encoder;
    }

    /// <summary>
    /// 注册 2D 编码器。对应 JS <c>ue.setEncoder(t, e)</c>。
    /// </summary>
    /// <returns>是否注册成功（JS 中编码器需有 encode 方法；C# 中由接口保证）。</returns>
    public static bool SetEncoder(string? type, IBarcode2DEncoder? encoder)
    {
        if (string.IsNullOrEmpty(type) || encoder == null) return false;
        var upper = type!.ToUpperInvariant();
        DzLogger.Debug($"---- setBarcode2DEncoder[{upper}]:");
        s_encoderMap[upper] = encoder;
        return true;
    }

    /// <summary>
    /// 检查并注册编码模块。对应 JS <c>ue.checkAndRegisterEncodeModule(t)</c>。
    /// JS 实现通过全局变量自动发现；C# 中在静态构造函数已注册内置编码器，
    /// 此方法直接查找已注册的编码器。
    /// </summary>
    public static IBarcode2DEncoder? CheckAndRegisterEncodeModule(string? type)
    {
        if (string.IsNullOrEmpty(type)) return null;
        return GetEncoder(type);
    }

    /// <summary>
    /// 创建 QR 码。对应 JS <c>ue.createQRCode(t)</c>。
    /// 直接转调 <see cref="QrMatrix.Create"/>。
    /// </summary>
    public static BitMatrix? CreateQRCode(Barcode2DRequest request) => QrMatrix.Create(request);

    /// <summary>
    /// 创建 2D 条码（通用入口）。对应 JS <c>ue.create2DBarcode(t)</c>。
    /// 流程：根据 barcodeType 查找编码器 → 未找到则尝试自动注册 → 仍无编码器则返回 null。
    /// </summary>
    public static BitMatrix? Create2DBarcode(Barcode2DRequest request)
    {
        var type = request.BarcodeType;
        IBarcode2DEncoder? encoder = null;
        if (!string.IsNullOrEmpty(type))
        {
            encoder = GetEncoder(type);
            if (encoder == null)
                encoder = CheckAndRegisterEncodeModule(type);
        }

        // JS: null !== t.text && void 0 !== t.text || (t.text = t.content)
        if (request.Text == null)
            request.Text = request.Content;

        if (encoder != null)
            return encoder.Encode(request);

        DzLogger.Warn($"---- create2DBarcode: no encoder for type [{type}], returning null");
        return null;
    }

    /// <summary>创建 PDF417 码。对应 JS <c>ue.createPDF417(t)</c>。</summary>
    public static BitMatrix? CreatePDF417(Barcode2DRequest request)
    {
        if (string.IsNullOrEmpty(request.BarcodeType))
            request.BarcodeType = TwoDBarcodeKind.PDF417;
        return Create2DBarcode(request);
    }

    /// <summary>创建 DataMatrix 码。对应 JS <c>ue.createDataMatrix(t)</c>。</summary>
    public static BitMatrix? CreateDataMatrix(Barcode2DRequest request)
    {
        if (string.IsNullOrEmpty(request.BarcodeType))
            request.BarcodeType = TwoDBarcodeKind.DMCode;
        return Create2DBarcode(request);
    }

    /// <summary>创建 GridMatrix 码。对应 JS <c>ue.createGridMatrix(t)</c>。</summary>
    public static BitMatrix? CreateGridMatrix(Barcode2DRequest request)
    {
        if (string.IsNullOrEmpty(request.BarcodeType))
            request.BarcodeType = TwoDBarcodeKind.GMCode;
        return Create2DBarcode(request);
    }

    // 注：JS 中 ue 类还包含 drawQrcode(e, i) 方法，用于将 QR 码绘制到 canvas。
    // 该方法依赖 Drawing 模块的 c 类（PrinterJob），属于更高层集成代码，
    // 不属于 Barcode 模块职责，故此处不实现。绘制功能由上层 Jobs 模块负责。
}

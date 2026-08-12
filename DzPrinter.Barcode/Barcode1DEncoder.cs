namespace DzPrinter.Barcode;

/// <summary>
/// 1D 条码编码器基类。对应 JS SDK 中 <c>m</c> 类。
/// 所有 1D 条码编码器均继承自本类，重写 <see cref="Encode"/> 方法。
/// </summary>
internal abstract class Barcode1DEncoder
{
    /// <summary>原始输入数据。对应 JS <c>m.data</c>。</summary>
    protected string Data { get; set; }

    /// <summary>显示文本。对应 JS <c>m.text</c>。</summary>
    protected string Text { get; set; }

    /// <summary>编码选项。对应 JS <c>m.options</c>。</summary>
    protected BarcodeEncodeOptions Options { get; set; }

    protected Barcode1DEncoder(string data, BarcodeEncodeOptions options)
    {
        Data = data ?? string.Empty;
        Text = options?.Text ?? data ?? string.Empty;
        Options = options ?? new BarcodeEncodeOptions();
    }

    /// <summary>
    /// 编码生成条码模块序列。对应 JS <c>m.encode(t, e)</c>。
    /// </summary>
    public abstract BarcodeEncodeResult Encode();

    /// <summary>
    /// 校验数据合法性。对应 JS <c>m.valid()</c>，默认实现返回 true。
    /// </summary>
    public virtual bool Valid() => true;
}

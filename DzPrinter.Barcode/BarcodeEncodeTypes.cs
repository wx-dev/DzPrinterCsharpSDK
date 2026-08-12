namespace DzPrinter.Barcode;

/// <summary>
/// 条码编码选项。对应 JS SDK 中传入各 1D 编码器构造函数的 <c>e</c> 对象。
/// 字段与 JS 中使用到的属性保持一致。
/// </summary>
public sealed class BarcodeEncodeOptions
{
    /// <summary>是否显示文本（用于渲染层）。对应 JS <c>e.displayValue</c>。</summary>
    public bool DisplayValue { get; set; } = true;

    /// <summary>是否使用扁平编码（无分隔符）。对应 JS <c>e.flat</c>。</summary>
    public bool Flat { get; set; }

    /// <summary>两侧静区宽度（模块数）。对应 JS <c>e.quietZones</c>。</summary>
    public int QuietZones { get; set; }

    /// <summary>是否显示守护空白。对应 JS <c>e.guardWhitespace</c>。</summary>
    public bool GuardWhitespace { get; set; }

    /// <summary>Code39 Mod43 校验。对应 JS <c>e.mod43</c>。</summary>
    public bool Mod43 { get; set; }

    /// <summary>是否显示首尾字符。对应 JS <c>e.showStartEndChar</c>。</summary>
    public bool ShowStartEndChar { get; set; }

    /// <summary>是否生成校验位。对应 JS <c>e.checkDigit</c>。</summary>
    public bool CheckDigit { get; set; }

    /// <summary>是否按 EAN-128（GS1-128）编码（前置 FNC1）。对应 JS <c>e.ean128</c>。</summary>
    public bool Ean128 { get; set; }

    /// <summary>显示文本覆盖。对应 JS <c>e.text</c>。</summary>
    public string? Text { get; set; }
}

/// <summary>
/// 单段条码项。对应 JS 中 <c>{data, text}</c> 对象。
/// </summary>
public sealed class BarcodeItem
{
    /// <summary>条码模块序列（'0'/'1' 字符串）。</summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>该项的可显示文本。</summary>
    public string Text { get; set; } = string.Empty;

    public BarcodeItem() { }

    public BarcodeItem(string data, string text)
    {
        Data = data;
        Text = text;
    }
}

/// <summary>
/// 条码编码结果。对应 JS 各编码器 <c>encode()</c> 返回的对象。
/// </summary>
public sealed class BarcodeEncodeResult
{
    /// <summary>分段条码项列表。对应 JS <c>items</c>。</summary>
    public List<BarcodeItem> Items { get; set; } = new();

    /// <summary>整体显示文本。对应 JS <c>text</c>。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>编码选项。对应 JS <c>options</c>。</summary>
    public BarcodeEncodeOptions? Options { get; set; }

    /// <summary>
    /// 拼接后的条码模块序列。对应 JS 中 <c>i.data = i.items.map(t=>t.data).join("")</c> 后处理。
    /// </summary>
    public string Data { get; set; } = string.Empty;
}

namespace DzPrinter.Drawing;

// =====================================================================
//  枚举定义。对应 JS SDK 中通过 (function(t){t[t.X=N]="X"...})(t.Enum||...)
//  模式定义的所有绘图相关枚举。数值与 JS 完全一致，用于协议帧与配置项。
// =====================================================================

/// <summary>
/// 条码文本位置。对应 JS <c>t.BarcodeTextPos</c>。
/// </summary>
public enum BarcodeTextPos
{
    /// <summary>顶部。</summary>
    Top = 0,

    /// <summary>底部。</summary>
    Bottom = 1,

    /// <summary>左侧。</summary>
    Left = 2,

    /// <summary>右侧。</summary>
    Right = 3
}

/// <summary>
/// 文本装饰线。对应 JS <c>t.TextDecoration</c>。
/// </summary>
public enum TextDecoration
{
    /// <summary>无。</summary>
    None = 0,

    /// <summary>下划线。</summary>
    UnderLine = 1,

    /// <summary>删除线。</summary>
    ThroughLine = 2,

    /// <summary>上划线。</summary>
    OverLine = 3
}

/// <summary>
/// 字体样式（位标志）。对应 JS <c>t.FontStyle</c>。
/// </summary>
[Flags]
public enum FontStyle
{
    /// <summary>常规。</summary>
    Regular = 0,

    /// <summary>粗体（JS: BOLD=1）。</summary>
    Bold = 1,

    /// <summary>斜体（JS: ITALIC=2）。</summary>
    Italic = 2,

    /// <summary>下划线（JS: UNDERLINE=4）。</summary>
    Underline = 4,

    /// <summary>删除线（JS: STRIKEOUT=8）。</summary>
    Strikeout = 8
}

/// <summary>
/// 对齐方式。对应 JS <c>t.Alignment</c>。
/// </summary>
/// <remarks>
/// JS 中 <c>Unset=255</c> 用于表示"未设置"，与其他对齐值（0-3）区分。
/// </remarks>
public enum Alignment
{
    /// <summary>起始（左/上）。JS: Start=0。</summary>
    Start = 0,

    /// <summary>居中。JS: Center=1。</summary>
    Center = 1,

    /// <summary>结束（右/下）。JS: End=2。</summary>
    End = 2,

    /// <summary>拉伸/两端对齐。JS: Stretch=3。</summary>
    Stretch = 3,

    /// <summary>未设置。JS: Unset=255。</summary>
    Unset = 255
}

/// <summary>
/// 旋转模式。对应 JS <c>t.RotateMode</c>。
/// </summary>
public enum RotateMode
{
    /// <summary>自动。JS: Auto=0。</summary>
    Auto = 0,

    /// <summary>旋转画布。JS: RotateCanvas=1。</summary>
    RotateCanvas = 1,

    /// <summary>旋转内容。JS: RotateContent=2。</summary>
    RotateContent = 2
}

/// <summary>
/// 反色模式（位标志）。对应 JS <c>t.AntiColorMode</c>。
/// </summary>
[Flags]
public enum AntiColorMode
{
    /// <summary>无反色。JS: None=0。</summary>
    None = 0,

    /// <summary>反前景色。JS: AntiColor=1。</summary>
    AntiColor = 1,

    /// <summary>反背景色。JS: AntiBackground=2。</summary>
    AntiBackground = 2,

    /// <summary>整块填充。JS: FillFull=4。</summary>
    FillFull = 4
}

/// <summary>
/// 边框对齐方式（位标志）。对应 JS <c>t.BorderAlign</c>。
/// 水平对齐占低 4 位，垂直对齐占高 4 位。
/// </summary>
[Flags]
public enum BorderAlign
{
    /// <summary>无。JS: None=0。</summary>
    None = 0,

    /// <summary>左对齐。JS: Left=1。</summary>
    Left = 1,

    /// <summary>水平内对齐。JS: HInner=2。</summary>
    HInner = 2,

    /// <summary>右对齐。JS: Right=4。</summary>
    Right = 4,

    /// <summary>水平外对齐。JS: HOuter=8。</summary>
    HOuter = 8,

    /// <summary>顶对齐。JS: Top=16。</summary>
    Top = 16,

    /// <summary>垂直内对齐。JS: VInner=32。</summary>
    VInner = 32,

    /// <summary>底对齐。JS: Bottom=64。</summary>
    Bottom = 64,

    /// <summary>垂直外对齐。JS: VOuter=128。</summary>
    VOuter = 128,

    /// <summary>内对齐（水平+垂直内）。JS: Inner=34 (=HInner|VInner)。</summary>
    Inner = HInner | VInner,

    /// <summary>外对齐（水平+垂直外）。JS: Outer=136 (=HOuter|VOuter)。</summary>
    Outer = HOuter | VOuter
}

/// <summary>
/// 自动换行模式。对应 JS <c>t.WrapMode</c>。
/// </summary>
public enum WrapMode
{
    /// <summary>不换行。JS: None=0。</summary>
    None = 0,

    /// <summary>按字符换行。JS: Char=1。</summary>
    Char = 1,

    /// <summary>按单词换行。JS: Word=2。</summary>
    Word = 2
}

/// <summary>
/// 尺度单位。对应 JS <c>t.ScaleUnit</c>。
/// </summary>
public enum ScaleUnit
{
    /// <summary>自动（按上下文）。JS: Auto=0。</summary>
    Auto = 0,

    /// <summary>毫米。JS: MM=1。</summary>
    MM = 1,

    /// <summary>像素。JS: Pix=2。</summary>
    Pix = 2
}

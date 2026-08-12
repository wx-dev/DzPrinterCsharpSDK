namespace DzPrinter.Imaging;

/// <summary>
/// 图像处理模式。对应 JS <c>ge.COLOR_MODE_*</c> 静态常量。
/// 值与 JS 严格一致，勿随意修改。
/// </summary>
public enum ColorMode
{
    /// <summary>灰度处理。JS: <c>ge.COLOR_MODE_GRAY = 1</c>。</summary>
    Gray = 1,

    /// <summary>黑白二值处理。JS: <c>ge.COLOR_MODE_BLACK_WHITE = 2</c>。</summary>
    BlackWhite = 2,

    /// <summary>半色调抖动处理。JS: <c>ge.COLOR_MODE_HALFTONE = 3</c>。</summary>
    Halftone = 3,
}

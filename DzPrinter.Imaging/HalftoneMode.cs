namespace DzPrinter.Imaging;

/// <summary>
/// 半色调抖动算法选择。对应 JS <c>ge.processHalftoneImage</c> 第三参数 <c>i</c>。
/// 值与 JS <c>switch</c> 分支严格一致：1=Floyd, 2=Burkers, 4=Stucki, 5=Jarvis，
/// <c>default</c>（含 3 及任何未列值）= Sierra。
/// </summary>
public enum HalftoneMode
{
    /// <summary>Floyd-Steinberg 抖动（误差分散 7/3/5/1 / 16）。JS: <c>case 1</c>。</summary>
    Floyd = 1,

    /// <summary>Burkers 抖动。JS: <c>case 2</c>。</summary>
    Burkers = 2,

    /// <summary>Sierra 抖动（默认）。JS: <c>default</c> 分支。</summary>
    Sierra = 3,

    /// <summary>Stucki 抖动。JS: <c>case 4</c>。</summary>
    Stucki = 4,

    /// <summary>Jarvis 抖动。JS: <c>case 5</c>。</summary>
    Jarvis = 5,
}

namespace DzPrinter.Imaging;

/// <summary>
/// 像素遍历回调的返回值。对应 JS <see cref="ImageProcessor.LoopImagePixels"/> 回调返回的数组。
/// <para>语义与 JS 完全一致：</para>
/// <list type="bullet">
///   <item><see cref="Length"/> = 0 → 不修改像素</item>
///   <item><see cref="Length"/> = 1 → 灰度值，R=G=B=<see cref="R"/>（对应 JS <c>[v]</c>）</item>
///   <item><see cref="Length"/> = 3 → RGB 三通道（对应 JS <c>[r,g,b]</c>）</item>
///   <item><see cref="Length"/> = 4 → RGBA 四通道（对应 JS <c>[r,g,b,a]</c>）</item>
/// </list>
/// </summary>
public readonly struct PixelResult
{
    /// <summary>红通道（或灰度值当 <see cref="Length"/>=1）。</summary>
    public byte R { get; init; }

    /// <summary>绿通道。</summary>
    public byte G { get; init; }

    /// <summary>蓝通道。</summary>
    public byte B { get; init; }

    /// <summary>Alpha 通道（仅当 <see cref="Length"/>=4 时有效）。</summary>
    public byte A { get; init; }

    /// <summary>有效分量数（0/1/3/4）。控制写入语义。</summary>
    public byte Length { get; init; }

    /// <summary>空结果，不修改像素。对应 JS 返回 <c>[]</c> / <c>null</c>。</summary>
    public static PixelResult None => default;

    /// <summary>灰度结果（R=G=B=v）。对应 JS 返回 <c>[v]</c>。</summary>
    public static PixelResult Gray(byte v) => new() { R = v, G = v, B = v, Length = 1 };

    /// <summary>RGB 结果。对应 JS 返回 <c>[r,g,b]</c>。</summary>
    public static PixelResult Rgb(byte r, byte g, byte b) => new() { R = r, G = g, B = b, Length = 3 };

    /// <summary>RGBA 结果。对应 JS 返回 <c>[r,g,b,a]</c>。</summary>
    public static PixelResult Rgba(byte r, byte g, byte b, byte a) => new() { R = r, G = g, B = b, A = a, Length = 4 };
}

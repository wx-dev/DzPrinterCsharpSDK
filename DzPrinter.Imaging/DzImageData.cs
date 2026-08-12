namespace DzPrinter.Imaging;

/// <summary>
/// 简单的 RGBA 位图数据载体。对应 JS <c>imageData</c>（<c>{width, height, data}</c>）。
/// 数据布局为紧密排列的 RGBA 四字节像素（与 HTML Canvas <c>ImageData</c> 一致）。
/// </summary>
/// <remarks>
/// 本类型是 Imaging 模块的" canonical home"（规范归属）。
/// <see cref="DzPrinter.Protocol.PrintEncoder"/> 等协议层通过项目引用使用此类型。
/// </remarks>
public readonly record struct DzImageData(int Width, int Height, byte[] Data)
{
    /// <summary>数据是否有效（非空、尺寸合理、数据长度 ≥ width*height*4）。</summary>
    public bool IsValid => Width > 0 && Height > 0 && Data != null && Data.Length >= Width * Height * 4;

    /// <summary>像素总数。</summary>
    public int PixelCount => Width * Height;

    /// <summary>以 <see cref="Span{Byte}"/> 形式访问底层数据，便于高效像素遍历。</summary>
    public Span<byte> DataSpan => Data.AsSpan();
}

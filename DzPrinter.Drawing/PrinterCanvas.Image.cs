using DzPrinter.Imaging;
using SkiaSharp;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvas 图像绘制（partial）。对应 JS <c>c</c> 类的：
//    drawImage / putImageData / drawImageResizeLabel
//    inverseColors / horizontalFlip / getImageData
// =====================================================================

public sealed partial class PrinterCanvas
{
    // ============ drawImage ============

    /// <summary>
    /// 绘制图像。对应 JS <c>c.drawImage(t)</c>。
    /// 支持缩放、裁剪、对齐。
    /// </summary>
    /// <remarks>
    /// JS 中 <c>t.image</c> 或 <c>t.img</c> 是 HTMLImageElement / HTMLCanvasElement / ImageData；
    /// C# 中接受 <see cref="SKBitmap"/> / <see cref="SKImage"/> / <see cref="DzImageData"/>。
    /// </remarks>
    public bool DrawImage(DrawOptions opt)
    {
        // JS: const e = t.image || t.img; i = e ? e.dzSrc || e : void 0
        var imgObj = opt.Image;
        if (imgObj == null) return false;

        // 解析图像源 → SKBitmap
        SKBitmap? srcBitmap = null;
        bool ownsSrc = false;
        try
        {
            if (imgObj is SKBitmap sb) srcBitmap = sb;
            else if (imgObj is SKImage si) { srcBitmap = SKBitmap.FromImage(si); ownsSrc = true; }
            else if (imgObj is DzImageData dz) { srcBitmap = DzImageDataToSKBitmap(dz); ownsSrc = true; }
            else if (imgObj is byte[] bytes)
            {
                srcBitmap = SKBitmap.Decode(bytes);
                ownsSrc = true;
            }
            if (srcBitmap == null) return false;

            var srcW = srcBitmap.Width;
            var srcH = srcBitmap.Height;

            // JS: if (i.width && i.height) ... 按比例缩放
            if ((opt.Width ?? 0) != 0 && (opt.Height ?? 0) != 0 && srcW > 0 && srcH > 0)
            {
                // JS: if (i.width/i.height > t.width/t.height) → 按宽度缩放，垂直对齐
                if ((double)srcW / srcH > (opt.Width ?? 0) / (opt.Height ?? 0))
                {
                    // JS: "number" != typeof t.verticalAlignment && "number" == typeof t.alignment && (t.verticalAlignment = t.alignment)
                    if (!opt.VerticalAlignment.HasValue && opt.Alignment.HasValue)
                        opt.VerticalAlignment = opt.Alignment;

                    var e_y = opt.Y ?? 0;
                    var s = (opt.Width ?? 0) * srcH / srcW;  // 新高度
                    switch (opt.VerticalAlignment ?? Alignment.Unset)
                    {
                        case Alignment.Start: opt.Height = s; break;
                        case Alignment.End: opt.Y = e_y + (opt.Height ?? 0) - s; opt.Height = s; break;
                        case Alignment.Stretch: break;
                        default: opt.Y = e_y + 0.5 * ((opt.Height ?? 0) - s); opt.Height = s; break;
                    }
                }
                else
                {
                    // JS: 否则按高度缩放，水平对齐
                    if (!opt.HorizontalAlignment.HasValue && opt.Alignment.HasValue)
                        opt.HorizontalAlignment = opt.Alignment;

                    var e_x = opt.X ?? 0;
                    var s = srcW * (opt.Height ?? 0) / srcH;  // 新宽度
                    switch (opt.HorizontalAlignment ?? Alignment.Unset)
                    {
                        case Alignment.Start: opt.Width = s; break;
                        case Alignment.End: opt.X = e_x + (opt.Width ?? 0) - s; opt.Width = s; break;
                        case Alignment.Stretch: break;
                        default: opt.X = e_x + 0.5 * ((opt.Width ?? 0) - s); opt.Width = s; break;
                    }
                }
            }
            else
            {
                // JS: 仅宽度或仅高度 → 按比例计算另一维
                if ((opt.Width ?? 0) != 0)
                    opt.Height = (opt.Width ?? 0) * srcH / srcW;
                else if ((opt.Height ?? 0) != 0)
                    opt.Width = srcW * (opt.Height ?? 0) / srcH;
            }

            var dstX = Math.Ceiling(opt.X ?? 0);
            var dstY = Math.Ceiling(opt.Y ?? 0);
            var dstW = opt.Width.HasValue ? Math.Floor(opt.Width.Value) : srcW;
            var dstH = opt.Height.HasValue ? Math.Floor(opt.Height.Value) : srcH;

            _ctx!.Save();
            var rotation = GetItemRotation(opt);
            SetRotation(rotation, new SKPoint((float)dstX, (float)dstY), new SKSize((float)dstW, (float)dstH));

            // JS: t.swidth && t.sheight ? ctx.drawImage(i, sx, sy, sw, sh, x, y, w, h)
            //                        : (t.width || t.height) ? ctx.drawImage(i, x, y, w, h)
            //                        : ctx.drawImage(i, x, y);
            if ((opt.Swidth ?? 0) != 0 && (opt.Sheight ?? 0) != 0)
            {
                var srcRect = new SKRect(
                    (float)(opt.Sx ?? 0), (float)(opt.Sy ?? 0),
                    (float)((opt.Sx ?? 0) + (opt.Swidth ?? 0)),
                    (float)((opt.Sy ?? 0) + (opt.Sheight ?? 0)));
                var dstRect = new SKRect((float)dstX, (float)dstY, (float)(dstX + dstW), (float)(dstY + dstH));
                _ctx.DrawBitmap(srcBitmap, srcRect, dstRect);
            }
            else if (opt.Width.HasValue || opt.Height.HasValue)
            {
                var dstRect = new SKRect((float)dstX, (float)dstY, (float)(dstX + dstW), (float)(dstY + dstH));
                _ctx.DrawBitmap(srcBitmap, dstRect);
            }
            else
            {
                _ctx.DrawBitmap(srcBitmap, (float)dstX, (float)dstY);
            }

            _ctx.Restore();
            return true;
        }
        finally
        {
            if (ownsSrc) srcBitmap?.Dispose();
        }
    }

    // ============ putImageData ============

    /// <summary>
    /// 写入像素数据到画布。对应 JS <c>c.putImageData(t)</c>。
    /// </summary>
    public bool PutImageData(DrawOptions opt)
    {
        if (opt.PixelData is not { } data || data.Data == null || !data.IsValid) return false;
        var x = (int)(opt.X ?? 0);
        var y = (int)(opt.Y ?? 0);

        // 将 DzImageData (RGBA) 写入 SKBitmap，再绘制到画布
        using var bmp = DzImageDataToSKBitmap(data);
        _ctx!.DrawBitmap(bmp, x, y);
        return true;
    }

    // ============ drawImageResizeLabel ============

    /// <summary>
    /// 九宫格缩放绘制图像到标签。对应 JS <c>c.drawImageResizeLabel(t, e)</c>。
    /// </summary>
    /// <param name="opt">包含 img/imageWidth/imageHeight/left/top/right/bottom/fullOfLabel/relativeScale/tileMode 的选项。</param>
    /// <param name="extraScale">外部传入的额外缩放因子（PrinterCanvasMm 传 DPM/20）。</param>
    public bool DrawImageResizeLabel(DrawOptions opt, double extraScale = 0)
    {
        var imgObj = opt.Image;
        if (imgObj == null || _width <= 0 || _height <= 0) return false;

        SKBitmap? img = null;
        bool ownsImg = false;
        try
        {
            if (imgObj is SKBitmap sb) img = sb;
            else if (imgObj is DzImageData dz) { img = DzImageDataToSKBitmap(dz); ownsImg = true; }
            else if (imgObj is byte[] bytes) { img = SKBitmap.Decode(bytes); ownsImg = true; }
            if (img == null) return false;

            var s = img.Width;
            var n = img.Height;
            if (s <= 0 || n <= 0) return false;

            // JS: 计算基础缩放比例 r
            double r;
            double o, c;
            if ((double)s / n < _width / _height)
            {
                // 按高度缩放
                c = _height;
                o = s * _height / s;  // JS: o = s * this.height / s  注意 s 被自身除（JS Bug：应为 / n）
                r = _height / n;
            }
            else
            {
                // 按宽度缩放
                o = _width;
                c = _width * n / s;
                r = _width / s;
            }

            // JS: let h = r; t.fullOfLabel ? h = r : t.relativeScale && t.relativeScale > 0 ? h = t.relativeScale * r : e && (h = e)
            var h = r;
            if (opt.FullOfLabel) h = r;
            else if (opt.RelativeScale.HasValue && opt.RelativeScale > 0) h = opt.RelativeScale.Value * r;
            else if (extraScale > 0) h = extraScale;

            var d = Math.Floor(opt.Left ?? 0);
            var u = Math.Floor(opt.Top ?? 0);
            var l = Math.Ceiling(opt.Right ?? 0);
            var G = Math.Ceiling(opt.Bottom ?? 0);
            var p = l - d;
            var m = G - u;
            var f = s - l;
            var P = n - G;
            var R = o / (d + f + 1);
            var C = c / (u + P + 1);
            var A = Math.Min(R, C);
            if (h > A)
            {
                h = (opt.RelativeScale.HasValue && opt.RelativeScale > 0) ? A : r;
            }

            var I = Math.Floor(d * h);
            var y = Math.Floor(u * h);
            var b = Math.Floor(f * h);
            var v = Math.Floor(P * h);
            var E = _width - b;
            var _h = _height - v;

            // JS: 9 宫格绘制（4 角 + 4 边 + 中间）
            // 4 角
            _ctx!.DrawBitmap(img, new SKRect(0, 0, (float)d, (float)u),
                             new SKRect(0, 0, (float)I, (float)y));
            _ctx.DrawBitmap(img, new SKRect((float)l, 0, (float)f + (float)l, (float)u),
                             new SKRect((float)E, 0, (float)E + (float)b, (float)y));
            _ctx.DrawBitmap(img, new SKRect((float)l, (float)G, (float)f + (float)l, (float)P + (float)G),
                             new SKRect((float)E, (float)_h, (float)E + (float)b, (float)_h + (float)v));
            _ctx.DrawBitmap(img, new SKRect(0, (float)G, (float)d, (float)P + (float)G),
                             new SKRect(0, (float)_h, (float)I, (float)_h + (float)v));

            if (opt.TileMode)
            {
                // JS: 平铺中间区域
                var tW = E - I;  // 中间宽度
                var tH = _h - y; // 中间高度
                // 上边
                if (tW > 0)
                {
                    for (var x = I; x < E; x += tW)
                    {
                        var drawW = E - x < tW ? E - x : tW;
                        var srcW = p * drawW / tW;
                        _ctx.DrawBitmap(img, new SKRect((float)d, 0, (float)(d + srcW), (float)u),
                                         new SKRect((float)x, 0, (float)(x + drawW), (float)y));
                        _ctx.DrawBitmap(img, new SKRect((float)d, (float)G, (float)(d + srcW), (float)(G + u)),
                                         new SKRect((float)x, (float)_h, (float)(x + drawW), (float)(_h + v)));
                    }
                }
                // 左右边
                if (tH > 0)
                {
                    for (var yy = y; yy < _h; yy += tH)
                    {
                        var drawH = _h - yy < tH ? _h - yy : tH;
                        var srcH = m * drawH / tH;
                        _ctx.DrawBitmap(img, new SKRect(0, (float)u, (float)d, (float)(u + srcH)),
                                         new SKRect(0, (float)yy, (float)I, (float)(yy + drawH)));
                        _ctx.DrawBitmap(img, new SKRect((float)l, (float)u, (float)(l + f), (float)(u + srcH)),
                                         new SKRect((float)E, (float)yy, (float)(E + b), (float)(yy + drawH)));
                    }
                }
            }
            else
            {
                // JS: 拉伸中间区域
                var tW = E - I;
                var tH = _h - y;
                _ctx.DrawBitmap(img, new SKRect((float)d, 0, (float)(d + p), (float)u),
                                 new SKRect((float)I, 0, (float)(I + tW), (float)y));
                _ctx.DrawBitmap(img, new SKRect((float)l, (float)u, (float)(l + f), (float)(u + m)),
                                 new SKRect((float)E, (float)y, (float)(E + b), (float)(y + tH)));
                _ctx.DrawBitmap(img, new SKRect((float)d, (float)G, (float)(d + p), (float)(G + P)),
                                 new SKRect((float)I, (float)_h, (float)(I + tW), (float)(_h + v)));
                _ctx.DrawBitmap(img, new SKRect(0, (float)u, (float)d, (float)(u + m)),
                                 new SKRect(0, (float)y, (float)I, (float)(y + tH)));
            }
            return true;
        }
        finally
        {
            if (ownsImg) img?.Dispose();
        }
    }

    // ============ inverseColors / horizontalFlip / getImageData ============

    /// <summary>
    /// 反转画布颜色（RGB 取反，Alpha 不变）。对应 JS <c>c.inverseColors()</c>。
    /// </summary>
    public bool InverseColors()
    {
        var image = GetImageData();
        if (!image.IsValid) return false;
        var data = image.Data;
        for (var t = 0; t < data.Length; t += 4)
        {
            data[t] = (byte)(255 - data[t]);
            data[t + 1] = (byte)(255 - data[t + 1]);
            data[t + 2] = (byte)(255 - data[t + 2]);
        }
        // 写回画布
        using var bmp = DzImageDataToSKBitmap(image);
        _ctx!.DrawBitmap(bmp, 0, 0);
        return true;
    }

    /// <summary>
    /// 水平翻转画布。对应 JS <c>c.horizontalFlip()</c>。
    /// </summary>
    /// <remarks>
    /// <b>JS Bug 保留</b>：JS 实现 <c>i.data[t*s*4+4*n+0]=e.data[t*s*4+4*(s-n)+0]</c> 中
    /// <c>4*(s-n)</c> 当 n=0 时指向 <c>4*s</c>（越界到下一行首像素），而非 <c>4*(s-1)</c>。
    /// 此处保留 JS 原始索引计算以逐字节匹配输出。
    /// </remarks>
    public bool HorizontalFlip()
    {
        var e = GetImageData();
        if (!e.IsValid) return false;
        var w = e.Width;
        var h = e.Height;
        var srcData = e.Data;
        var dstData = new byte[srcData.Length];

        for (var t = 0; t < h; t++)
        {
            for (var n = 0; n < w; n++)
            {
                // JS: i.data[t*s*4+4*n+0] = e.data[t*s*4+4*(s-n)+0]
                // 注意：s 即 width；当 n=0 时 4*(s-0)=4*s 越界到下一行。
                // C# 中需防止越界 → 用 (s-n) 但 n=0 时改为 (s-1) 以避免越界（与 JS 行为差异说明）。
                // 为保真：JS 中越界访问在浏览器会读取 undefined（视为 0），此处用 0 模拟。
                var srcIdxBase = t * w * 4 + 4 * (w - n);
                var dstIdxBase = t * w * 4 + 4 * n;
                // JS 越界访问：n=0 时 srcIdxBase = t*w*4 + 4*w = (t+1)*w*4 → 下一行第 0 像素
                //              最后一行 t=h-1 时 srcIdxBase = h*w*4 → 越界，JS 读 undefined → 0
                byte r = 0, g = 0, b = 0, a = 0;
                if (srcIdxBase + 3 < srcData.Length)
                {
                    r = srcData[srcIdxBase + 0];
                    g = srcData[srcIdxBase + 1];
                    b = srcData[srcIdxBase + 2];
                    a = srcData[srcIdxBase + 3];
                }
                dstData[dstIdxBase + 0] = r;
                dstData[dstIdxBase + 1] = g;
                dstData[dstIdxBase + 2] = b;
                dstData[dstIdxBase + 3] = a;
            }
        }

        var dst = new DzImageData(w, h, dstData);
        using var bmp = DzImageDataToSKBitmap(dst);
        _ctx!.DrawBitmap(bmp, 0, 0);
        return true;
    }

    /// <summary>
    /// 获取画布像素数据。对应 JS <c>c.getImageData()</c>。
    /// </summary>
    public DzImageData GetImageData()
    {
        if (_bitmap == null)
            throw new InvalidOperationException("画布未初始化，请先调用 StartJob。");
        var w = _bitmap.Width;
        var h = _bitmap.Height;

        // 从 SKBitmap 提取 RGBA 像素数据。
        // SkiaSharp 2.88 的 SKBitmap 没有 ReadPixels 方法，
        // 改为创建 RGBA 格式的 SKBitmap，通过 SKCanvas 绘制转换格式，再读取 Bytes。
        using var rgbaBmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(rgbaBmp))
        {
            canvas.DrawBitmap(_bitmap, 0, 0);
        }
        var data = rgbaBmp.Bytes;

        return new DzImageData(w, h, data);
    }

    // ============ 辅助：DzImageData ↔ SKBitmap ============

    /// <summary>将 <see cref="DzImageData"/> (RGBA) 转换为 <see cref="SKBitmap"/>。</summary>
    private static SKBitmap DzImageDataToSKBitmap(DzImageData dz)
    {
        var bmp = new SKBitmap(dz.Width, dz.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        // 直接复制字节（DzImageData 为 RGBA 紧密排列）
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(dz.Data, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            // SKBitmap.SetPixels 接受源数据指针
            bmp.InstallPixels(
                new SKImageInfo(dz.Width, dz.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                ptr);
            // InstallPixels 不复制数据，需显式复制以避免持有外部引用
            var copy = new SKBitmap(dz.Width, dz.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using (var canvas = new SKCanvas(copy))
            {
                canvas.DrawBitmap(bmp, 0, 0);
            }
            bmp.Dispose();
            return copy;
        }
        finally
        {
            handle.Free();
        }
    }
}

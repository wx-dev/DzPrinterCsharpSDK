using System.Globalization;
using System.Text.RegularExpressions;

namespace DzPrinter.Imaging;

/// <summary>
/// 图像处理器：对应 JS SDK 中的 <c>ge</c> 类。
/// 提供灰度转换、黑白二值化、半色调抖动（Floyd-Steinberg / Burkers / Sierra / Stucki / Jarvis）
/// 等像素级处理算法。所有方法均在 <see cref="DzImageData.Data"/> 上就地修改。
/// </summary>
/// <remarks>
/// <para><b>保真声明</b>：本类逐行翻译 JS <c>ge</c> 类，包括以下 JS 原有行为（部分为 Bug），
/// 均原样保留以确保与 JS SDK 输出逐字节一致：</para>
/// <list type="bullet">
///   <item><see cref="LoopImagePixels"/> 回调的第 4 参数（alpha）实际传入 <c>data[0]</c>
///   （首个像素的红通道），而非当前像素的 alpha —— JS 源码笔误，原样保留。</item>
///   <item><see cref="ParseRgba"/> 短色值正则 <c>^\[0-9a-fA-F]{3,4}$</c> 中的 <c>\[</c>
///   匹配字面量 <c>[</c>，故 <c>#RGB</c> / <c>#RGBA</c> 短格式永不匹配，回退到默认值。</item>
///   <item><see cref="ProcessGrayImage"/> / <see cref="ProcessBlackWhiteImage"/> 使用
///   <c>0.3R + 0.59G + 0.11B</c>（十进制近似），而 <see cref="ToGray"/> 与
///   <see cref="DzPrinter.Printer.PrintEncoder.GetImageGrayValue"/> 使用定点整数
///   <c>19661R + 38666G + 7209B &gt;&gt; 16</c>（即 0.299/0.587/0.114）。两者并存。</item>
///   <item><see cref="ToHalfTone_Jarvis"/> 最后一个误差分散项使用 <c>4*i+2</c>（而非
///   <c>4*(i+2)</c>），指向错误像素。JS Bug，原样保留。</item>
///   <item><see cref="ToHalfTone_Stucki"/> 使用 Jarvis 系数（和 48）配合 1/42 除数。</item>
///   <item><see cref="ResetPixelAtIndex"/> 使用 <c>(int)Math.Floor(delta + 0.5)</c>
///   以精确匹配 JS <c>Math.round</c>（半值向 +∞ 取整）。</item>
/// </list>
/// </remarks>
public static class ImageProcessor
{
    /// <summary>二值化阈值默认值。JS: <c>Se.THRESHOLD_DEFAULT = 150</c>。</summary>
    public const int DefaultThreshold = 150;

    // ============= LoopImagePixels =============

    /// <summary>
    /// 遍历图像每个像素，调用回调并就地写回结果。对应 JS <c>ge.loopImagePixels(t, e)</c>。
    /// </summary>
    /// <param name="image">输入图像（将被就地修改）。</param>
    /// <param name="callback">回调 <c>(r, g, b, a) => PixelResult</c>。
    /// <b>注意</b>：第 4 参数为 <c>data[0]</c>（首个像素红通道），非当前像素 alpha（JS Bug 保留）。</param>
    /// <returns>同一图像引用（便于链式调用）。</returns>
    public static DzImageData LoopImagePixels(DzImageData image, Func<byte, byte, byte, byte, PixelResult> callback)
    {
        if (!image.IsValid) return image;
        var data = image.Data;
        for (int i = 0; i < data.Length; i += 4)
        {
            // JS: s = e(i[t], i[t+1], i[t+2], i[0])  —— 第 4 参数是 data[0] 而非 data[i+3]
            var result = callback(data[i], data[i + 1], data[i + 2], data[0]);
            if (result.Length <= 0) continue;

            if (result.Length <= 1)
            {
                // JS: s.length <= 1 → s = [s[0], s[0], s[0]]
                data[i] = data[i + 1] = data[i + 2] = result.R;
            }
            else
            {
                data[i] = result.R;
                data[i + 1] = result.G;
                data[i + 2] = result.B;
                if (result.Length > 3) data[i + 3] = result.A;
            }
        }
        return image;
    }

    // ============= SetRGB =============

    /// <summary>
    /// 在数据数组指定偏移写入颜色分量。对应 JS <c>ge.setRGB(t, e, i)</c>。
    /// <para><c>values</c> 长度 ≥3 写 RGB（&gt;3 额外写 A）；长度 1-2 写 R=G=B=values[0]；0 或 null 不写。</para>
    /// </summary>
    public static void SetRGB(Span<byte> data, int index, byte[]? values)
    {
        if (index >= data.Length) return;
        if (values == null || values.Length == 0) return;

        if (values.Length >= 3)
        {
            data[index] = values[0];
            data[index + 1] = values[1];
            data[index + 2] = values[2];
            if (values.Length > 3) data[index + 3] = values[3];
        }
        else
        {
            data[index] = data[index + 1] = data[index + 2] = values[0];
        }
    }

    // ============= ToGray =============

    /// <summary>
    /// 定点整数灰度公式：<c>(19661*R + 38666*G + 7209*B) &gt;&gt; 16 &amp; 255</c>。
    /// 对应 JS <c>ge.toGray(t, e, i)</c>。等价于 0.299R + 0.587G + 0.114B。
    /// </summary>
    public static int ToGray(int r, int g, int b) => (19661 * r + 38666 * g + 7209 * b) >> 16 & 255;

    // ============= ResetPixelAtIndex =============

    /// <summary>
    /// 在指定偏移累加误差增量（仅当当前值在 (5, 250) 范围内）。
    /// 对应 JS <c>ge.resetPixelAtIndex(t, e, i)</c>。
    /// <para>R/G/B 三通道统一设为新值 <c>(old + round(delta)) &amp; 255</c>。</para>
    /// </summary>
    /// <returns>true 表示已修改；false 表示越界或值越界。</returns>
    public static bool ResetPixelAtIndex(Span<byte> data, int index, double delta)
    {
        if (data.Length <= index) return false;
        if (data[index] < 5 || data[index] > 250) return false;

        // JS: Math.round(i) —— 半值向 +∞ 取整。C# 用 Floor(x+0.5) 精确匹配（含负数）。
        int rounded = (int)Math.Floor(delta + 0.5);
        int newVal = (data[index] + rounded) & 255;
        data[index] = data[index + 1] = data[index + 2] = (byte)newVal;
        return true;
    }

    // ============= ParseRgba =============

    // JS 短色值正则：/^\[0-9a-fA-F]{3,4}$/ —— \[ 匹配字面 '['，故短格式永不命中。
    private static readonly Regex BuggyShortHexRegex =
        new(@"^\[0-9a-fA-F]{3,4}$", RegexOptions.Compiled);

    // JS 长色值正则：/^([0-9A-Fa-f]{2}){3,4}$/
    private static readonly Regex LongHexRegex =
        new(@"^([0-9A-Fa-f]{2}){3,4}$", RegexOptions.Compiled);

    /// <summary>
    /// 解析颜色字符串为字节数组。对应 JS <c>ge.parseRgba(t, e)</c>。
    /// <para>支持 <c>#RRGGBB</c> / <c>#RRGGBBAA</c> / <c>RRGGBB</c> / <c>RRGGBBAA</c>。
    /// <c>#RGB</c> / <c>#RGBA</c> 短格式因 JS 正则 Bug 永不命中（保留行为）。</para>
    /// </summary>
    /// <param name="color">颜色字符串（可带 # 前缀）。null 或空返回默认。</param>
    /// <param name="defaultGray">默认灰度值。当 color 为空或无法解析时返回 [d,d,d]。</param>
    /// <returns>
    /// 长度 3（RGB）或 4（RGBA）的字节数组；color 为空且无默认时返回 null（对应 JS <c>[]</c>）。
    /// </returns>
    public static byte[]? ParseRgba(string? color, byte? defaultGray = null)
    {
        if (string.IsNullOrEmpty(color))
            return defaultGray.HasValue
                ? new byte[] { defaultGray.Value, defaultGray.Value, defaultGray.Value }
                : null;

        var t = color;
        if (t[0] == '#') t = t.Substring(1);

        // 短格式（Bug：正则永不匹配，此分支为死代码，保留以对应 JS 结构）
        if (BuggyShortHexRegex.IsMatch(t))
        {
            var len = t.Length > 3 ? 4 : 3;
            var result = new byte[len];
            result[0] = byte.Parse(t.AsSpan(0, 1), NumberStyles.HexNumber);
            result[1] = byte.Parse(t.AsSpan(1, 1), NumberStyles.HexNumber);
            result[2] = byte.Parse(t.AsSpan(2, 1), NumberStyles.HexNumber);
            if (len > 3) result[3] = byte.Parse(t.AsSpan(3, 1), NumberStyles.HexNumber);
            return result;
        }

        // 长格式：6 或 8 位十六进制
        if (LongHexRegex.IsMatch(t))
        {
            var len = t.Length / 2;
            var result = new byte[len];
            for (int i = 0; i < len; i++)
                result[i] = byte.Parse(t.AsSpan(i * 2, 2), NumberStyles.HexNumber);
            return result;
        }

        // 无法解析 → 默认值
        return defaultGray.HasValue
            ? new byte[] { defaultGray.Value, defaultGray.Value, defaultGray.Value }
            : null;
    }

    // ============= ProcessGrayImage =============

    /// <summary>
    /// 灰度转换。对应 JS <c>ge.processGrayImage(t)</c>。
    /// 使用 <c>0.3R + 0.59G + 0.11B</c>（十进制近似，与 <see cref="ToGray"/> 定点公式不同）。
    /// </summary>
    public static DzImageData ProcessGrayImage(DzImageData image)
    {
        if (!image.IsValid) return image;
        return LoopImagePixels(image, (r, g, b, _) =>
        {
            // JS: Math.round(.3*t + .59*e + .11*i)
            int gray = (int)Math.Floor(0.3 * r + 0.59 * g + 0.11 * b + 0.5);
            return PixelResult.Gray((byte)gray);
        });
    }

    // ============= ProcessBlackWhiteImage =============

    /// <summary>
    /// 黑白二值化。对应 JS <c>ge.processBlackWhiteImage(t, e, i)</c>。
    /// 灰度 ≤ 阈值 → 指定颜色；否则 → 白色 [255,255,255]。
    /// </summary>
    /// <param name="image">输入图像（就地修改）。</param>
    /// <param name="threshold">阈值，默认 <see cref="DefaultThreshold"/> (=150)。0 视为默认。</param>
    /// <param name="color">"黑"像素颜色（如 <c>#000000</c>）。空则用 0（黑色）。</param>
    public static DzImageData ProcessBlackWhiteImage(DzImageData image, int threshold = DefaultThreshold, string? color = null)
    {
        if (!image.IsValid) return image;

        // JS: const n = e || 150; —— 0 视为默认
        var thr = threshold == 0 ? DefaultThreshold : threshold;
        // JS: parseRgba(i || "", 0) —— 空色默认 0（黑）；defaultGray=0 保证非空返回
        var colorBytes = ParseRgba(color, 0)!;

        return LoopImagePixels(image, (r, g, b, _) =>
        {
            // JS: Math.round(.3*t + .59*e + .11*i) <= s ? n : [255,255,255]
            int gray = (int)Math.Floor(0.3 * r + 0.59 * g + 0.11 * b + 0.5);
            if (gray <= thr)
            {
                // 返回 colorBytes（长度 3 或 4）
                return colorBytes.Length >= 3
                    ? (colorBytes.Length > 3
                        ? PixelResult.Rgba(colorBytes[0], colorBytes[1], colorBytes[2], colorBytes[3])
                        : PixelResult.Rgb(colorBytes[0], colorBytes[1], colorBytes[2]))
                    : PixelResult.Gray(colorBytes[0]);
            }
            return PixelResult.Rgb(255, 255, 255);
        });
    }

    // ============= ProcessHalftoneImage =============

    /// <summary>
    /// 半色调抖动处理。对应 JS <c>ge.processHalftoneImage(t, e, i, s)</c>。
    /// 先做灰度转换，再按 <paramref name="mode"/> 选择抖动算法。
    /// </summary>
    /// <param name="image">输入图像（就地修改）。</param>
    /// <param name="threshold">阈值，默认 150。0 视为默认。</param>
    /// <param name="mode">抖动算法，默认 <see cref="HalftoneMode.Sierra"/>。</param>
    /// <param name="color">"亮"像素颜色。空则使用二值灰度。</param>
    public static DzImageData ProcessHalftoneImage(
        DzImageData image,
        int threshold = DefaultThreshold,
        HalftoneMode mode = HalftoneMode.Sierra,
        string? color = null)
    {
        if (!image.IsValid) return image;

        var thr = threshold == 0 ? DefaultThreshold : threshold;
        var colorBytes = ParseRgba(color);

        // JS: switch(this.processGrayImage(t), i) —— 逗号运算符：先灰度，再 switch mode
        ProcessGrayImage(image);

        switch (mode)
        {
            case HalftoneMode.Floyd: ToHalfTone_Floyd(image, thr, colorBytes); break;
            case HalftoneMode.Burkers: ToHalfTone_Burkers(image, thr, colorBytes); break;
            case HalftoneMode.Stucki: ToHalfTone_Stucki(image, thr, colorBytes); break;
            case HalftoneMode.Jarvis: ToHalfTone_Jarvis(image, thr, colorBytes); break;
            default: ToHalfTone_Sierra(image, thr, colorBytes); break;
        }
        return image;
    }

    // ============= ImageProcess（统一入口） =============

    /// <summary>
    /// 统一图像处理入口。对应 JS <c>ge.imageProcess(t)</c>。
    /// 按 <paramref name="mode"/> 分派到对应处理函数。
    /// </summary>
    /// <returns>处理后的图像；未知模式返回 null（对应 JS <c>void 0</c>）。</returns>
    public static DzImageData? ImageProcess(
        DzImageData image,
        ColorMode mode,
        int threshold = DefaultThreshold,
        HalftoneMode halftoneMode = HalftoneMode.Sierra,
        string? color = null)
    {
        if (!image.IsValid) return image;
        return mode switch
        {
            ColorMode.Gray => ProcessGrayImage(image),
            ColorMode.BlackWhite => ProcessBlackWhiteImage(image, threshold, color),
            ColorMode.Halftone => ProcessHalftoneImage(image, threshold, halftoneMode, color),
            _ => null,
        };
    }

    // =====================================================================
    //  半色调抖动算法（5 种）
    //  对应 JS ge.toHalfTone_Floyd / Burkers / Sierra / Stucki / Jarvis。
    //  所有算法假定输入已为灰度图（R=G=B=灰度值），由 ProcessHalftoneImage 保证。
    // =====================================================================

    /// <summary>
    /// 在抖动循环中写入当前像素的输出值。对应 JS：
    /// <c>setRGB(r, h, u &gt; 0 || c.length &lt;= 0 ? [u] : c)</c>。
    /// <para>u &gt; 0（白）或未指定颜色 → 写 [u,u,u]；否则写指定颜色。</para>
    /// </summary>
    private static void SetBinaryOrColor(Span<byte> data, int index, int binaryValue, byte[]? color)
    {
        if (binaryValue > 0 || (color?.Length ?? 0) <= 0)
        {
            // [u] → R=G=B=u（长度 1，由 SetRGB 展开为三通道相等）
            data[index] = data[index + 1] = data[index + 2] = (byte)binaryValue;
        }
        else
        {
            SetRGB(data, index, color);
        }
    }

    /// <summary>Floyd-Steinberg 抖动。对应 JS <c>ge.toHalfTone_Floyd(t, e, i)</c>。</summary>
    public static void ToHalfTone_Floyd(DzImageData image, int threshold, byte[]? color)
    {
        if (!image.IsValid) return;
        int w = image.Width, h = image.Height;
        var r = image.Data;
        int a = 4 * w;
        const double o = 0.0625;  // 1/16
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int idx = y * a + 4 * x;
                int d = r[idx];
                int u = d >= threshold ? 255 : 0;
                int l = d - u;
                if (x < w - 1) ResetPixelAtIndex(r, 4 * (x + 1) + y * a, 7 * l * o);
                if (x >= 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 1) * a, 3 * l * o);
                if (y < h - 1) ResetPixelAtIndex(r, 4 * x + (y + 1) * a, 5 * l * o);
                if (x < w - 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 1) * a, 1 * l * o);
                SetBinaryOrColor(r, idx, u, color);
            }
    }

    /// <summary>Burkers 抖动。对应 JS <c>ge.toHalfTone_Burkers(t, e, i)</c>。</summary>
    public static void ToHalfTone_Burkers(DzImageData image, int threshold, byte[]? color)
    {
        if (!image.IsValid) return;
        int w = image.Width, h = image.Height;
        var r = image.Data;
        int a = 4 * w;
        const double o = 0.03125;  // 1/32
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int idx = y * a + 4 * x;
                int d = r[idx];
                int u = d >= threshold ? 255 : 0;
                int l = d - u;
                if (x < w - 1) ResetPixelAtIndex(r, 4 * (x + 1) + y * a, 8 * l * o);
                if (x < w - 2) ResetPixelAtIndex(r, 4 * (x + 2) + y * a, 4 * l * o);
                if (x >= 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 2) + (y + 1) * a, 2 * l * o);
                if (x >= 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 1) * a, 4 * l * o);
                if (y < h - 1) ResetPixelAtIndex(r, 4 * x + (y + 1) * a, 8 * l * o);
                if (x < w - 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 1) * a, 4 * l * o);
                if (x < w - 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 2) + (y + 1) * a, 2 * l * o);
                SetBinaryOrColor(r, idx, u, color);
            }
    }

    /// <summary>Sierra 抖动（默认）。对应 JS <c>ge.toHalfTone_Sierra(t, e, i)</c>。</summary>
    public static void ToHalfTone_Sierra(DzImageData image, int threshold, byte[]? color)
    {
        if (!image.IsValid) return;
        int w = image.Width, h = image.Height;
        var r = image.Data;
        int a = 4 * w;
        const double o = 1.0 / 32;  // JS: 1/32
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int idx = y * a + 4 * x;
                int d = r[idx];
                int u = d >= threshold ? 255 : 0;
                int l = d - u;
                // JS 使用相对索引 h+4, h+8, h+a-8 等（等价于绝对索引）
                if (x < w - 1) ResetPixelAtIndex(r, idx + 4, 5 * l * o);
                if (x < w - 2) ResetPixelAtIndex(r, idx + 8, 3 * l * o);
                if (x >= 2 && y < h - 1) ResetPixelAtIndex(r, idx + a - 8, 2 * l * o);
                if (x >= 1 && y < h - 1) ResetPixelAtIndex(r, idx + a - 4, 4 * l * o);
                if (y < h - 1) ResetPixelAtIndex(r, idx + a, 5 * l * o);
                if (x < w - 1 && y < h - 1) ResetPixelAtIndex(r, idx + a + 4, 4 * l * o);
                if (x < w - 2 && y < h - 1) ResetPixelAtIndex(r, idx + a + 8, 2 * l * o);
                if (x >= 1 && y < h - 2) ResetPixelAtIndex(r, idx + 2 * a - 4, 2 * l * o);
                if (y < h - 2) ResetPixelAtIndex(r, idx + 2 * a, 3 * l * o);
                if (x < w - 1 && y < h - 2) ResetPixelAtIndex(r, idx + 2 * a + 4, 2 * l * o);
                SetBinaryOrColor(r, idx, u, color);
            }
    }

    /// <summary>Stucki 抖动。对应 JS <c>ge.toHalfTone_Stucki(t, e, i)</c>。</summary>
    /// <remarks>
    /// 注意：JS 使用 Jarvis 系数（7/5/3/5/7/5/3/1/3/5/3/1，和 48）配合 1/42 除数，
    /// 与标准 Stucki 不同。此处保留 JS 行为。
    /// </remarks>
    public static void ToHalfTone_Stucki(DzImageData image, int threshold, byte[]? color)
    {
        if (!image.IsValid) return;
        int w = image.Width, h = image.Height;
        var r = image.Data;
        int a = 4 * w;
        const double o = 0.0238095238095238;  // JS 字面量（≈ 1/42）
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int idx = y * a + 4 * x;
                int d = r[idx];
                int u = d >= threshold ? 255 : 0;
                int l = d - u;
                if (x < w - 1) ResetPixelAtIndex(r, 4 * (x + 1) + y * a, 7 * l * o);
                if (x < w - 2) ResetPixelAtIndex(r, 4 * (x + 2) + y * a, 5 * l * o);
                if (x >= 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 2) + (y + 1) * a, 3 * l * o);
                if (x >= 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 1) * a, 5 * l * o);
                if (y < h - 1) ResetPixelAtIndex(r, 4 * x + (y + 1) * a, 7 * l * o);
                if (x < w - 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 1) * a, 5 * l * o);
                if (x < w - 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 2) + (y + 1) * a, 3 * l * o);
                if (x >= 2 && y < h - 2) ResetPixelAtIndex(r, 4 * (x - 2) + (y + 2) * a, 1 * l * o);
                if (x >= 1 && y < h - 2) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 2) * a, 3 * l * o);
                if (y < h - 2) ResetPixelAtIndex(r, 4 * x + (y + 2) * a, 5 * l * o);
                if (x < w - 1 && y < h - 2) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 2) * a, 3 * l * o);
                if (x < w - 2 && y < h - 2) ResetPixelAtIndex(r, 4 * (x + 2) + (y + 2) * a, 1 * l * o);
                SetBinaryOrColor(r, idx, u, color);
            }
    }

    /// <summary>Jarvis 抖动。对应 JS <c>ge.toHalfTone_Jarvis(t, e, i)</c>。</summary>
    /// <remarks>
    /// 注意：JS 最后一项使用 <c>4*i+2+(t+2)*a</c>（应为 <c>4*(i+2)+(t+2)*a</c>），
    /// 指向错误像素。此处保留 JS Bug 以确保逐字节一致。
    /// </remarks>
    public static void ToHalfTone_Jarvis(DzImageData image, int threshold, byte[]? color)
    {
        if (!image.IsValid) return;
        int w = image.Width, h = image.Height;
        var r = image.Data;
        int a = 4 * w;
        const double o = 0.0208333333333333;  // JS 字面量（≈ 1/48）
        for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                int idx = y * a + 4 * x;
                int d = r[idx];
                int u = d >= threshold ? 255 : 0;
                int l = d - u;
                if (x < w - 1) ResetPixelAtIndex(r, 4 * (x + 1) + y * a, 7 * l * o);
                if (x < w - 2) ResetPixelAtIndex(r, 4 * (x + 2) + y * a, 5 * l * o);
                if (x >= 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 2) + (y + 1) * a, 3 * l * o);
                if (x >= 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 1) * a, 5 * l * o);
                if (y < h - 1) ResetPixelAtIndex(r, 4 * x + (y + 1) * a, 7 * l * o);
                if (x < w - 1 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 1) * a, 5 * l * o);
                if (x < w - 2 && y < h - 1) ResetPixelAtIndex(r, 4 * (x + 2) + (y + 1) * a, 3 * l * o);
                if (x >= 2 && y < h - 2) ResetPixelAtIndex(r, 4 * (x - 2) + (y + 2) * a, 1 * l * o);
                if (x >= 1 && y < h - 2) ResetPixelAtIndex(r, 4 * (x - 1) + (y + 2) * a, 3 * l * o);
                if (y < h - 2) ResetPixelAtIndex(r, 4 * x + (y + 2) * a, 5 * l * o);
                if (x < w - 1 && y < h - 2) ResetPixelAtIndex(r, 4 * (x + 1) + (y + 2) * a, 3 * l * o);
                // JS Bug: 4*i+2 而非 4*(i+2)，指向 (y+2, x) 的 G 通道偏移 +2 处
                if (x < w - 2 && y < h - 2) ResetPixelAtIndex(r, 4 * x + 2 + (y + 2) * a, 1 * l * o);
                SetBinaryOrColor(r, idx, u, color);
            }
    }
}

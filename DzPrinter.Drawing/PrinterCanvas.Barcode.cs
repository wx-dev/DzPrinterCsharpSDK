using SkiaSharp;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvas 条码绘制（partial）。对应 JS <c>c</c> 类的：
//    draw1DBarcode / draw2DBarcode
// =====================================================================

public sealed partial class PrinterCanvas
{
    // ============ draw1DBarcode ============

    /// <summary>
    /// 绘制 1D 条码。对应 JS <c>c.draw1DBarcode(e)</c>。
    /// </summary>
    /// <remarks>
    /// 流程：
    /// <list type="number">
    ///   <item>规范化坐标/尺寸/旋转。</item>
    ///   <item>处理 textHeight（缺省回退到 fontHeight）。</item>
    ///   <item>解析 textFlag（BarcodeTextPos）：决定文本显示位置（上下/单端）。</item>
    ///   <item>根据 PixPerUnit 计算模块宽度 f，必要时按 autoScaleLevel 放大。</item>
    ///   <item>根据对齐方式计算条码水平偏移 m。</item>
    ///   <item>遍历每段 datas，绘制条（'1' 为前景，'0' 为背景）与文本。</item>
    /// </list>
    /// </remarks>
    public bool Draw1DBarcode(DrawOptions opt)
    {
        // JS: 默认值填充
        opt.X ??= 0;
        opt.Y ??= 0;
        opt.Width ??= 0;
        opt.Height ??= 0;

        var items = opt.Datas;
        if (items == null || items.Count == 0) return false;

        // JS: e.padding && this.processPadding(e)
        if (opt.Padding != null) ProcessPadding(opt);

        var rotation = GetItemRotation(opt);
        var rotateMode = GetRotateMode(opt.RotateMode);
        if (rotation > 0 && IsPortrait(rotation) && rotateMode == RotateMode.RotateContent
            && (opt.Width ?? 0) > 0 && (opt.Height ?? 0) > 0)
        {
            var rect = new Rect(opt.X ?? 0, opt.Y ?? 0, opt.Width ?? 0, opt.Height ?? 0);
            RectUtils.Rotate90(rect);
            opt.X = rect.X; opt.Y = rect.Y; opt.Width = rect.Width; opt.Height = rect.Height;
        }

        // JS: textHeight 缺省回退到 fontHeight
        if (!opt.TextHeight.HasValue && opt.FontHeight.HasValue)
            opt.TextHeight = opt.FontHeight;

        var a = (opt.TextHeight ?? 0) > 0 ? opt.TextHeight!.Value : 0.0;
        var o = GetHorizontalAlignment(opt.HorizontalAlignment, Alignment.Center);
        var h = opt.AutoScaleLevel ?? AutoScaleLevelDefault;

        // JS: textFlag 缺省回退到 flag
        if (!opt.TextFlag.HasValue && opt.Flag.HasValue)
            opt.TextFlag = opt.Flag;

        // JS: const d = "number" != typeof e.textFlag || e.topText ? 2 : e.textFlag;
        var d = (opt.TextFlag == null || opt.TopText) ? 2 : opt.TextFlag.Value;
        a = d > 0 ? a : 0;
        var u = opt.TopText ? a : 0.0;

        // JS: 计算总宽度 g
        var totalData = string.Concat(items.Select(t => t.Data ?? ""));
        var g = totalData.Length * _pixPerUnit;
        opt.Width = (opt.Width ?? 0) < g ? g : opt.Width;

        // JS: 对齐值范围校验
        if (o > Alignment.Stretch || o < Alignment.Start) o = Alignment.Center;

        var p = opt.Width ?? 0;
        var m = 0.0;
        var f = p / totalData.Length;  // 每模块宽度
        if (h > 0 && f / _pixPerUnit < h && o <= Alignment.End)
        {
            // JS: 放大到 1*pixPerUnit
            f = 1 * _pixPerUnit;
            p = totalData.Length * f;
            switch (o)
            {
                case Alignment.Start: break;
                case Alignment.End: m = (opt.Width ?? 0) - p; break;
                default: m = 0.5 * ((opt.Width ?? 0) - p); break;
            }
        }

        // JS: 高度计算
        var P = a > 0 ? BarTextMargin * f : 0.0;
        var R = MinBarHeight;
        if (a > 0 && opt.TopText) R = 2 * (a + P) + MinBarHeight;
        else if (a > 0) R = a + P + MinBarHeight;

        if ((opt.Height ?? 0) <= 0)
        {
            if (a <= 0) a = 25;
            opt.Height = 3 * a;
        }
        else if ((opt.Height ?? 0) < R)
        {
            opt.Height = R;
        }

        _ctx!.Save();
        SetRotation(rotation, new SKPoint((float)(opt.X ?? 0), (float)(opt.Y ?? 0)),
                    new SKSize((float)(opt.Width ?? 0), (float)(opt.Height ?? 0)));

        var C = a + P;
        var A = 0.5 * a + P + 1;
        var I = opt.TextAlign ?? opt.TextAlignment;
        var y = f;
        var b = (opt.X ?? 0) + m;
        var v = (opt.Y ?? 0) + u + P;
        var E = v;
        var _ = opt.Y ?? 0;

        // JS: 顶部文本（topText 且段数 ≥7 时绘制 EAN/UPC 顶部数字）
        if (opt.TopText && items.Count >= 7)
        {
            var s = string.Concat(items.Skip(1).Take(5).Select(t => t.Data ?? "")).Length * y;
            u += P;
            DrawTextCore(new DrawOptions
            {
                Text = opt.Text,
                X = (opt.X ?? 0) + items[0].Data.Length * y,
                Y = opt.Y ?? 0,
                Width = s,
                Height = a,
                FontHeight = a,
                FontStyle = opt.FontStyle,
                FontName = opt.FontName,
                Color = opt.Color,
                AutoReturn = WrapMode.None,
                HorizontalAlignment = I ?? Alignment.Center,
                CharSpace = opt.CharSpace
            });
        }

        foreach (var s in items)
        {
            var dataStr = s.Data ?? "";
            if ((opt.Height ?? 0) > C)
            {
                // JS: 注意 i 重用为内层变量（s.text?C:C-A），n = e.height - i - u
                var innerI = !string.IsNullOrEmpty(s.Text) ? C : C - A;
                var n = (opt.Height ?? 0) - innerI - u;
                if (d == 1)
                {
                    E = opt.Y ?? 0;
                    v = E + innerI;
                }
                else
                {
                    v = (opt.Y ?? 0) + u;
                    E = v + n;
                    _ = E + P;
                }
                _borderAlign = BorderAlign.None;

                for (var t = 0; t < dataStr.Length; t++, b += y)
                {
                    if (dataStr[t] == '1')
                    {
                        if (!string.IsNullOrEmpty(opt.BgColor))
                        {
                            DrawRectCore(new DrawOptions
                            {
                                X = b,
                                Y = E,
                                Width = y,
                                Height = innerI,
                                Color = opt.BgColor,
                                Fill = true
                            });
                        }
                        DrawRectCore(new DrawOptions
                        {
                            X = b,
                            Y = v,
                            Width = y,
                            Height = n,
                            Color = opt.Color,
                            Fill = true
                        });
                    }
                    else if (!string.IsNullOrEmpty(opt.BgColor))
                    {
                        DrawRectCore(new DrawOptions
                        {
                            X = b,
                            Y = opt.Y,
                            Width = y,
                            Height = opt.Height,
                            Color = opt.BgColor,
                            Fill = true
                        });
                    }
                }
            }

            // JS: 绘制段文本
            if (!string.IsNullOrEmpty(s.Text))
            {
                var n = items.Count > 2 && s.Text!.Length > 1 ? DockCharMargin * y : 0;
                var r = dataStr.Length * y;
                DrawTextCore(new DrawOptions
                {
                    Text = s.Text,
                    X = b - r + n,
                    Y = _,
                    Width = r - 2 * n,
                    Height = a,
                    FontHeight = a,
                    FontStyle = opt.FontStyle,
                    FontName = opt.FontName,
                    Color = opt.Color,
                    AutoReturn = WrapMode.None,
                    HorizontalAlignment = I ?? Alignment.Center,
                    CharSpace = opt.CharSpace
                });
            }
        }

        _ctx.Restore();
        return true;
    }

    // ============ draw2DBarcode ============

    /// <summary>
    /// 绘制 2D 条码。对应 JS <c>c.draw2DBarcode(e)</c>。
    /// </summary>
    /// <remarks>
    /// 流程：
    /// <list type="number">
    ///   <item>规范化坐标/尺寸（缺省 height = width）。</item>
    ///   <item>计算模块像素 I（水平）与 y（垂直），含静区。</item>
    ///   <item>根据反色模式绘制背景填充（FillFull）。</item>
    ///   <item>绘制静区边框（zoneSize &gt; 0）。</item>
    ///   <item>遍历位矩阵，绘制每个模块（1→前景，0→背景）。</item>
    /// </list>
    /// </remarks>
    public bool Draw2DBarcode(DrawOptions opt)
    {
        opt.X ??= 0;
        opt.Y ??= 0;
        opt.Width ??= 0;
        opt.Height ??= opt.Width;

        var matrix = opt.Data;
        if (matrix == null) return false;

        var s_zone = opt.ZoneSize ?? 0;
        var n_barPixels = opt.BarPixels ?? _modulePixels;
        var a = opt.AutoScaleLevel ?? AutoScaleLevelDefault;
        var o = matrix.Rows;
        var h = matrix.Cols;
        if (o <= 0 || h <= 0) return false;

        var rotation = GetItemRotation(opt);
        var rotateMode = GetRotateMode(opt.RotateMode);
        var paddings = GetPaddings(opt);
        var g = paddings[1] + paddings[3];
        var p = paddings[0] + paddings[2];
        var m = o + 2 * s_zone;  // 含静区的总行数
        var f = h + 2 * s_zone;  // 含静区的总列数
        var P = f * _pixPerUnit;
        var R = m * _pixPerUnit;

        var C = GetHorizontalAlignment(opt.HorizontalAlignment, Alignment.Center);
        var A = GetVerticalAlignment(opt.VerticalAlignment, Alignment.Center);
        var I = 0.0;  // 水平模块像素
        var y = 0.0;  // 垂直模块像素

        if (C > Alignment.Stretch || C < Alignment.Start) C = Alignment.Center;
        if (A > Alignment.Stretch || A < Alignment.Start) A = Alignment.Center;

        // JS: 计算模块像素 I
        if ((opt.Width ?? 0) <= 0)
        {
            I = n_barPixels * _pixPerUnit;
            opt.Width = I * f + g;
        }
        else if ((opt.Width ?? 0) - g <= P)
        {
            I = _pixPerUnit;
            opt.Width = P + g;
        }
        else
        {
            I = ((opt.Width ?? 0) - g) / f;
        }

        // JS: 计算模块像素 y
        if ((opt.Height ?? 0) <= 0)
        {
            y = n_barPixels * _pixPerUnit;
            opt.Height = y * m + p;
        }
        else if ((opt.Height ?? 0) - p <= R)
        {
            y = _pixPerUnit;
            opt.Height = R + p;
        }
        else
        {
            y = ((opt.Height ?? 0) - p) / m;
        }

        if (string.IsNullOrEmpty(opt.Color)) opt.Color = _foreground;

        var b = GetAntiColor(opt.AntiColor);
        var v = b != AntiColorMode.None ? (opt.BgColor ?? "") : (opt.Color ?? "");
        var E = b != AntiColorMode.None ? (opt.Color ?? "") : (opt.BgColor ?? "");

        // JS: FillFull → 整块填充背景
        if ((b & AntiColorMode.FillFull) != 0)
        {
            DrawRect(new DrawOptions
            {
                X = opt.X,
                Y = opt.Y,
                Width = opt.Width,
                Height = opt.Height,
                Color = opt.Color ?? _foreground,
                Rotation = rotateMode == RotateMode.RotateContent ? 0 : rotation,
                Fill = true
            });
            if (string.IsNullOrEmpty(v) || IsTransparent(v)) v = ColorBgDefault;
        }

        // JS: 旋转 90°
        if (rotation > 0 && IsPortrait(rotation) && rotateMode == RotateMode.RotateContent
            && (opt.Width ?? 0) > 0 && (opt.Height ?? 0) > 0)
        {
            var rect = new Rect(opt.X ?? 0, opt.Y ?? 0, opt.Width ?? 0, opt.Height ?? 0);
            RectUtils.Rotate90(rect);
            opt.X = rect.X; opt.Y = rect.Y; opt.Width = rect.Width; opt.Height = rect.Height;
        }

        _ctx!.Save();
        SetRotation(rotation, new SKPoint((float)(opt.X ?? 0), (float)(opt.Y ?? 0)),
                    new SKSize((float)(opt.Width ?? 0), (float)(opt.Height ?? 0)));

        if (g > 0) { opt.X += paddings[3]; opt.Width -= g; }
        if (p > 0) { opt.Y += paddings[0]; opt.Height -= p; }

        // JS: 非拉伸对齐时，I/y 取较小值并按 pixPerUnit 向下取整
        if (C < Alignment.Stretch)
        {
            I = Math.Min(I, y);
            if (I < a * _pixPerUnit)
                I = Math.Floor(I / _pixPerUnit) * _pixPerUnit;
        }
        if (A < Alignment.Stretch)
        {
            y = Math.Min(I, y);
            if (y < a * _pixPerUnit)
                y = Math.Floor(y / _pixPerUnit) * _pixPerUnit;
        }

        var _width = f * I;
        var D_height = m * y;
        var w = 0.0;
        var T = 0.0;
        switch (C)
        {
            case Alignment.Start: break;
            case Alignment.End: w = Math.Round((opt.Width ?? 0) - _width); break;
            default: w = Math.Round(0.5 * ((opt.Width ?? 0) - _width)); break;
        }
        switch (A)
        {
            case Alignment.Start: break;
            case Alignment.End: T = Math.Round((opt.Height ?? 0) - D_height); break;
            default: T = Math.Round(0.5 * ((opt.Height ?? 0) - D_height)); break;
        }

        var O = Math.Floor((opt.X ?? 0) + w);
        var L = Math.Floor((opt.Y ?? 0) + T);
        _borderAlign = BorderAlign.None;

        // JS: 绘制静区边框（4 条边）
        if (!string.IsNullOrEmpty(E) && s_zone > 0)
        {
            DrawRectCore(new DrawOptions { X = O, Y = L, Width = Math.Round(_width), Height = Math.Round(y * s_zone), Color = E, Fill = true });
            DrawRectCore(new DrawOptions { X = O, Y = Math.Round(L + (o + s_zone) * y), Width = Math.Round(_width), Height = Math.Round(s_zone * y), Color = E, Fill = true });
            DrawRectCore(new DrawOptions { X = O, Y = L, Width = Math.Round(I * s_zone), Height = Math.Round(D_height), Color = E, Fill = true });
            DrawRectCore(new DrawOptions { X = Math.Round(O + (h + s_zone) * I), Y = L, Width = Math.Round(s_zone * I), Height = Math.Round(D_height), Color = E, Fill = true });
        }

        // JS: 绘制数据模块
        var e_y = L + y * s_zone;
        for (var t = 0; t < o; t++, e_y += y)
        {
            var r_x = O + I * s_zone;
            for (var n_col = 0; n_col < h; n_col++, r_x += I)
            {
                var bit = matrix.Data[t * (matrix.Cols) + n_col];
                if (bit != 0)
                {
                    if (!string.IsNullOrEmpty(v))
                        DrawRectCore(new DrawOptions { X = r_x, Y = e_y, Width = I, Height = y, Color = v, Fill = true });
                }
                else if (!string.IsNullOrEmpty(E))
                {
                    DrawRectCore(new DrawOptions { X = r_x, Y = e_y, Width = I, Height = y, Color = E, Fill = true });
                }
            }
        }

        _ctx.Restore();
        return true;
    }
}

using SkiaSharp;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvas 绘图原语（partial）。对应 JS <c>c</c> 类的：
//    drawLine / _drawLine
//    drawRect / _drawRect / adjustRect
//    drawRoundRect / drawRoundRectPath
//    drawEllipse / drawCircle
// =====================================================================

public sealed partial class PrinterCanvas
{
    // ============ drawLine ============

    /// <summary>
    /// 绘制直线。对应 JS <c>c.drawLine(t)</c>。
    /// 自动处理点退化（两点重合时根据 width/height 扩展为线段或全宽线）。
    /// </summary>
    public bool DrawLine(DrawOptions opt)
    {
        // JS: let e = t.x1||0, i = t.y1||0, s = "number" == typeof t.x2 ? t.x2 : e, n = "number" == typeof t.y2 ? t.y2 : i;
        var e = opt.X1 ?? 0;
        var i = opt.Y1 ?? 0;
        var s = opt.X2 ?? e;
        var n = opt.Y2 ?? i;

        // JS: const r = "number" == typeof t.x ? t.x : e, a = "number" == typeof t.y ? t.y : i;
        var r = opt.X ?? e;
        var a = opt.Y ?? i;

        if (e == s && i == n)
        {
            // JS: 两点重合 → 根据 width/height 扩展
            var o = opt.Width ?? 0;
            var c = opt.Height ?? 0;
            if (o > 0 || c > 0)
            {
                var h = Math.Min(o, c);
                if (h > 0) opt.LineWidth = h;
                if (o > c)
                {
                    // 水平线
                    opt.X1 = e = r;
                    opt.X2 = s = r + o;
                    opt.Y1 = i = a + 0.5 * c;
                    opt.Y2 = n = i;
                }
                else
                {
                    // 垂直线
                    opt.X1 = e = r + 0.5 * o;
                    opt.X2 = s = e;
                    opt.Y1 = i = a;
                    opt.Y2 = n = a + c;
                }
            }
            else
            {
                // 全宽水平线
                opt.X1 = e = r;
                opt.X2 = s = r + _width;
                opt.Y1 = i = a;
                opt.Y2 = n = a;
            }
        }
        else
        {
            // JS: 水平线时若未指定 y1/y2 但指定 y，则用 y
            if (i == n)
            {
                if (!opt.Y1.HasValue && !opt.Y2.HasValue && opt.Y.HasValue)
                {
                    opt.Y1 = i = opt.Y.Value;
                    opt.Y2 = n = opt.Y.Value;
                }
            }
            // JS: 垂直线时若未指定 x1/x2 但指定 x，则用 x
            else if (e == s && !opt.X1.HasValue && !opt.X2.HasValue && opt.X.HasValue)
            {
                opt.X1 = e = opt.X.Value;
                opt.X2 = s = opt.X.Value;
            }
        }

        _ctx!.Save();
        var rotation = GetItemRotation(opt);
        SetRotation(rotation, new SKPoint((float)(0.5 * (e + s)), (float)(0.5 * (i + n))), null);
        var result = DrawLineCore(opt);
        _ctx.Restore();
        return result;
    }

    /// <summary>实际绘制直线。对应 JS <c>c._drawLine(t)</c>。</summary>
    private bool DrawLineCore(DrawOptions opt)
    {
        // JS: 端点坐标 +0.5 偏移（HTML5 Canvas 1px 线锐利技巧）
        var e = Math.Floor(opt.X1 ?? 0) + 0.5;
        var i = Math.Floor(opt.Y1 ?? 0) + 0.5;
        var s = opt.X2.HasValue ? Math.Floor(opt.X2.Value) + 0.5 : e;
        var n = opt.Y2.HasValue ? Math.Floor(opt.Y2.Value) + 0.5 : i;

        _strokePaint.Color = ParseColor(opt.Color ?? _foreground);
        _strokePaint.StrokeWidth = (float)Math.Ceiling(GetLineWidth(opt.LineWidth));

        // 虚线处理
        var r = opt.DashLens;
        if (r == null || r.Length <= 0)
        {
            var dashStr = opt.DashLen ?? "";
            if (!string.IsNullOrEmpty(dashStr))
            {
                r = dashStr.Split(',')
                           .Select(x => double.TryParse(x, out var v) ? v : 0)
                           .ToArray();
            }
        }
        if (SupportLineDash && r != null && r.Length > 0)
        {
            var oldEffect = _strokePaint.PathEffect;
            _strokePaint.PathEffect = SKPathEffect.CreateDash(
                r.Select(x => (float)x).ToArray(), 0);
            oldEffect?.Dispose();
        }
        else
        {
            _strokePaint.PathEffect?.Dispose();
            _strokePaint.PathEffect = null;
        }

        using var path = new SKPath();
        path.MoveTo((float)e, (float)i);
        path.LineTo((float)s, (float)n);
        _ctx!.DrawPath(path, _strokePaint);

        // 重置虚线
        if (SupportLineDash)
        {
            _strokePaint.PathEffect?.Dispose();
            _strokePaint.PathEffect = null;
        }
        return true;
    }

    // ============ drawRect ============

    /// <summary>
    /// 绘制矩形。对应 JS <c>c.drawRect(t)</c>。
    /// 若指定圆角参数则转调 <see cref="DrawRoundRect"/>。
    /// </summary>
    public bool DrawRect(DrawOptions opt)
    {
        var e = opt.X ?? 0;
        var i = opt.Y ?? 0;
        var s = opt.Width ?? 0;
        var n = opt.Height ?? 0;

        // JS: 圆角参数 → 转调 drawRoundRect
        if ((opt.CornerWidth ?? 0) > 0 || (opt.CornerHeight ?? 0) > 0 || (opt.Radius ?? 0) > 0)
            return DrawRoundRect(opt);

        if (s > 0 && n > 0)
        {
            _ctx!.Save();
            var rotation = GetItemRotation(opt);
            SetRotation(rotation, new SKPoint((float)e, (float)i), new SKSize((float)s, (float)n));
            var result = DrawRectCore(opt);
            _ctx.Restore();
            return result;
        }
        return false;
    }

    /// <summary>实际绘制矩形。对应 JS <c>c._drawRect(t)</c>。</summary>
    private bool DrawRectCore(DrawOptions opt)
    {
        var colorStr = ValidateColorStr(opt.Color ?? _foreground);
        var i = opt.X ?? 0;
        var s = opt.Y ?? 0;
        var n = opt.Width ?? opt.Height ?? 0;
        var a = opt.Height ?? opt.Width ?? 0;

        if (n <= 0) return false;
        if (IsTransparent(colorStr)) return true;

        _strokePaint.StrokeJoin = opt.LineJoin switch
        {
            "round" => SKStrokeJoin.Round,
            "bevel" => SKStrokeJoin.Bevel,
            _ => SKStrokeJoin.Miter
        };

        if (opt.Fill)
        {
            var rect = AdjustRect(new Rect(i, s, n, a), opt.BorderAlign ?? _borderAlign);
            _fillPaint.Color = ParseColor(colorStr);
            _ctx!.DrawRect(new SKRect((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height)), _fillPaint);
        }
        else
        {
            var o = GetLineWidth(opt.LineWidth);
            double[]? c = opt.DashLens;
            if (c == null || c.Length <= 0)
            {
                var dashStr = opt.DashLen ?? "";
                if (!string.IsNullOrEmpty(dashStr))
                {
                    c = dashStr.Split(',')
                               .Select(x => double.TryParse(x, out var v) ? v : 0)
                               .ToArray();
                }
            }
            o = Math.Ceiling(o);
            i += 0.5 * o;
            s += 0.5 * o;
            n -= o;
            a -= o;

            var rect = AdjustRect(new Rect(i, s, n, a), opt.BorderAlign ?? _borderAlign);
            _strokePaint.StrokeWidth = (float)(o != 0 ? o : LineWidth);
            _strokePaint.Color = ParseColor(colorStr);

            if (SupportLineDash && c != null && c.Length > 0)
            {
                _strokePaint.PathEffect?.Dispose();
                _strokePaint.PathEffect = SKPathEffect.CreateDash(c.Select(x => (float)x).ToArray(), 0);
            }

            _ctx!.DrawRect(new SKRect((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height)), _strokePaint);

            if (SupportLineDash)
            {
                _strokePaint.PathEffect?.Dispose();
                _strokePaint.PathEffect = null;
            }
        }
        return true;
    }

    /// <summary>
    /// 调整矩形坐标以对齐到像素边界。对应 JS <c>c.adjustRect(e, i)</c>。
    /// 根据 BorderAlign 标志用 floor/ceil/round 调整 x/y 与 right/bottom。
    /// </summary>
    public Rect AdjustRect(Rect rect, BorderAlign align)
    {
        var s = rect.X;
        var n = rect.Y;
        var r = rect.X + rect.Width;
        var a = rect.Y + rect.Height;

        // JS: const o = 240 & i;  取高 4 位（垂直部分）
        var verticalPart = (int)align & 0xF0;

        // JS: switch (15 & i)  取低 4 位（水平部分）
        switch ((int)align & 0x0F)
        {
            case (int)BorderAlign.Left: s = Math.Floor(s); r = Math.Floor(r); break;
            case (int)BorderAlign.HInner: s = Math.Ceiling(s); r = Math.Floor(r); break;
            case (int)BorderAlign.Right: s = Math.Ceiling(s); r = Math.Ceiling(r); break;
            case (int)BorderAlign.HOuter: s = Math.Floor(s); r = Math.Ceiling(r); break;
            default: s = Math.Round(s); r = Math.Round(r); break;
        }

        switch (verticalPart)
        {
            case (int)BorderAlign.Top: n = Math.Floor(n); a = Math.Floor(a); break;
            case (int)BorderAlign.VInner: n = Math.Ceiling(n); a = Math.Floor(a); break;
            case (int)BorderAlign.Bottom: n = Math.Ceiling(n); a = Math.Ceiling(a); break;
            case (int)BorderAlign.VOuter: n = Math.Floor(n); a = Math.Ceiling(a); break;
            default: n = Math.Round(n); a = Math.Round(a); break;
        }

        var c = r - s;
        var h = a - n;
        return new Rect(s, n, c > 0 ? c : 1, h > 0 ? h : 1);
    }

    // ============ drawRoundRect ============

    /// <summary>
    /// 绘制圆角矩形。对应 JS <c>c.drawRoundRect(t)</c>。
    /// </summary>
    public bool DrawRoundRect(DrawOptions opt)
    {
        var e = opt.X ?? 0;
        var i = opt.Y ?? 0;
        var s = opt.Width ?? 0;
        var n = opt.Height ?? 0;
        var r = opt.CornerWidth ?? opt.CornerHeight ?? opt.Radius ?? 0;

        if (s <= 0 && n <= 0) return false;
        if (r <= 0) return DrawRect(opt);

        var a = 0.5 * Math.Min(s, n);
        var colorStr = ValidateColorStr(opt.Color ?? _foreground);
        var h = GetLineWidth(opt.LineWidth);

        // JS: 非填充时线宽不超过半径，且向内偏移
        if (!opt.Fill)
        {
            if (h > a) h = a;
            e += 0.5 * h;
            i += 0.5 * h;
            s -= h;
            n -= h;
        }

        _ctx!.Save();
        var rotation = GetItemRotation(opt);
        SetRotation(rotation, new SKPoint((float)e, (float)i), new SKSize((float)s, (float)n));
        _ctx.Translate((float)e, (float)i);
        var color = ParseColor(colorStr);
        _fillPaint.Color = color;
        _strokePaint.Color = color;

        DrawRoundRectPath(s, n, r);
        if (opt.Fill)
        {
            _ctx.DrawRect(new SKRect(0, 0, (float)s, (float)n), _fillPaint);
        }
        else
        {
            _strokePaint.StrokeWidth = (float)h;
            // 用 SKRoundRect 绘制
            var roundRect = new SKRoundRect(new SKRect(0, 0, (float)s, (float)n), (float)r);
            _ctx.DrawRoundRect(roundRect, _strokePaint);
            roundRect.Dispose();
        }
        _ctx.Restore();
        return true;
    }

    /// <summary>
    /// 绘制圆角矩形路径。对应 JS <c>c.drawRoundRectPath(t, e, i)</c>。
    /// JS 用 4 段 arc + lineTo 绘制；C# 直接用 <see cref="SKRoundRect"/> 等价。
    /// </summary>
    private void DrawRoundRectPath(double width, double height, double radius)
    {
        // JS 实现通过 ctx.arc + lineTo + closePath 构造路径。
        // C# 中由调用方使用 SKRoundRect 直接绘制，本方法保留为占位（实际绘制在 DrawRoundRect 中完成）。
        // 注意：JS 路径在调用本方法后由 ctx.fill()/ctx.stroke() 完成绘制。
    }

    // ============ drawEllipse ============

    /// <summary>
    /// 绘制椭圆。对应 JS <c>c.drawEllipse(t)</c>。
    /// </summary>
    public bool DrawEllipse(DrawOptions opt)
    {
        var e = opt.X ?? 0;
        var i = opt.Y ?? 0;
        var s = opt.Width ?? opt.Height ?? 0;
        var n = opt.Height ?? opt.Width ?? 0;
        if (s <= 0 && n <= 0) return false;
        if (s == n) return DrawCircle(opt);

        var colorStr = ValidateColorStr(opt.Color ?? _foreground);
        var a = GetLineWidth(opt.LineWidth);
        e += 0.5 * a;
        i += 0.5 * a;
        s -= a;
        n -= a;
        var o = 0.5 * s;
        var h = 0.5 * n;
        e += o;
        i += h;

        var rotation = GetItemRotation(opt);
        _ctx!.Save();
        SetRotation(rotation, new SKPoint((float)e, (float)i), null);
        _strokePaint.StrokeWidth = (float)a;
        var color = ParseColor(colorStr);
        _fillPaint.Color = color;
        _strokePaint.Color = color;

        // JS: ctx.ellipse ? beginPath + ellipse : 兼容路径（4 段贝塞尔）
        // C# 用 DrawOval 等价
        var rect = new SKRect((float)(e - o), (float)(i - h), (float)(e + o), (float)(i + h));
        if (opt.Fill)
            _ctx.DrawOval(rect, _fillPaint);
        else
            _ctx.DrawOval(rect, _strokePaint);

        _ctx.Restore();
        return true;
    }

    // ============ drawCircle ============

    /// <summary>
    /// 绘制圆。对应 JS <c>c.drawCircle(t)</c>。
    /// </summary>
    public bool DrawCircle(DrawOptions opt)
    {
        var e = opt.X ?? 0;
        var i = opt.Y ?? 0;
        var s = opt.Radius ?? 0;

        if (s <= 0)
        {
            var n = opt.Width ?? 0;
            var r = opt.Height ?? 0;
            if (n > 0 && r > 0)
            {
                s = 0.5 * Math.Min(n, r);
                e += 0.5 * n;
                i += 0.5 * r;
            }
            else if (n > 0)
            {
                s = 0.5 * n;
                e += s;
            }
            else if (r > 0)
            {
                s = 0.5 * r;
                i += s;
            }
            else
            {
                return false;
            }
        }

        var colorStr = ValidateColorStr(opt.Color ?? _foreground);
        var lw = GetLineWidth(opt.LineWidth);
        s -= 0.5 * lw;

        _strokePaint.StrokeWidth = (float)lw;
        var color = ParseColor(colorStr);
        _fillPaint.Color = color;
        _strokePaint.Color = color;

        var rect = new SKRect((float)(e - s), (float)(i - s), (float)(e + s), (float)(i + s));
        if (opt.Fill)
            _ctx!.DrawOval(rect, _fillPaint);
        else
            _ctx!.DrawOval(rect, _strokePaint);
        return true;
    }
}

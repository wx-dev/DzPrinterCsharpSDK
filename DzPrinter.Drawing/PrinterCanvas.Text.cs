using SkiaSharp;
using FontStyleFlag = DzPrinter.Drawing.FontStyle;

namespace DzPrinter.Drawing;

// =====================================================================
//  PrinterCanvas 文本绘制与度量（partial）。对应 JS <c>c</c> 类的：
//    drawText / _drawText / drawTextList1 / _drawSingleLineText / fillCharSpaceText
//    drawArcText / getTextWidths
//    measureText / measureTextExt / measureFontSize
//    findSplitPosition / findWordSplitPos / getCustomCharWidth / splitText
// =====================================================================

public sealed partial class PrinterCanvas
{
    // ============ 文本度量结果 ============

    /// <summary>
    /// 文本度量结果。对应 JS <c>ctx.measureText()</c> 返回的 TextMetrics 对象。
    /// </summary>
    public sealed class TextMetrics
    {
        /// <summary>文本宽度（像素）。</summary>
        public double Width { get; set; }

        /// <summary>文本高度（像素）。用于 measureTextExt 多行高度。</summary>
        public double Height { get; set; }

        /// <summary>基线上方边界（像素）。对应 JS <c>actualBoundingBoxAscent</c>。</summary>
        public double ActualBoundingBoxAscent { get; set; }

        /// <summary>基线下方边界（像素）。对应 JS <c>actualBoundingBoxDescent</c>。</summary>
        public double ActualBoundingBoxDescent { get; set; }
    }

    // ============ measureText ============

    /// <summary>
    /// 度量文本。对应 JS <c>c.measureText(t, e)</c>。
    /// JS 返回 <c>ctx.measureText(i)</c> 结果；C# 用 <see cref="SKPaint.MeasureText"/> 等价。
    /// </summary>
    /// <param name="opt">包含 text/fontHeight/fontStyle/fontName 的选项。</param>
    /// <param name="applyFont">是否在度量前应用字体（默认 true）。</param>
    public TextMetrics MeasureText(DrawOptions opt, bool applyFont = true)
    {
        // JS: null !== t.text && void 0 !== t.text || (t.text = "")
        if (opt.Text == null) opt.Text = "";
        var i = opt.Text is string s ? s : opt.Text!.ToString() ?? "";

        if (i.Length <= 0) return new TextMetrics();

        // JS: (t.fontHeight || t.fontStyle || t.fontName) && ("boolean" != typeof e || e) && this.setFont(...)
        if ((opt.FontHeight.HasValue || opt.FontStyle.HasValue || !string.IsNullOrEmpty(opt.FontName))
            && applyFont)
        {
            SetFont(opt.FontHeight, opt.FontStyle, opt.FontName);
        }

        // JS: void 0 !== this.ctx.measureText → 返回 ctx.measureText(i)
        // C# 用 SKPaint.MeasureText(string) + FontMetrics（SkiaSharp 2.88 无 string+out SKRect 重载）
        var width = _textPaint.MeasureText(i);
        var fm = _textPaint.FontMetrics;
        return new TextMetrics
        {
            Width = width,
            // SkiaSharp FontMetrics.Ascent 为负（基线上方），Descent 为正（基线下方）
            ActualBoundingBoxAscent = -fm.Ascent,
            ActualBoundingBoxDescent = fm.Descent
        };
    }

    /// <summary>
    /// 度量文本（含多行高度）。对应 JS <c>c.measureTextExt(t)</c>。
    /// </summary>
    public TextMetrics MeasureTextExt(DrawOptions opt)
    {
        var e = opt.FontHeight ?? _fontHeight;
        var i = opt.Width ?? 0;

        var splitResult = SplitText(new DrawOptions
        {
            Text = opt.Text ?? "",
            Width = opt.Width,
            FontHeight = opt.FontHeight,
            FontStyle = opt.FontStyle,
            FontName = opt.FontName,
            CharSpace = opt.CharSpace,
            AutoReturn = opt.AutoReturn,
            LineSpace = opt.LineSpace,
            MeasureOptimizeStep = opt.MeasureOptimizeStep
        });

        var n = GetLineSpace(opt.LineSpace ?? 0, e);
        var r = opt.CharSpace ?? 0;
        // JS: a = s.length > 1 ? (s.length-1)*n : 0
        var a = splitResult.Count > 1 ? (splitResult.Count - 1) * n : 0;

        if (i <= 0)
        {
            // JS: for (let t=0; t<s.length; t++) if (s[t].length > 0) { ... 取最大宽度 }
            for (var t = 0; t < splitResult.Count; t++)
            {
                if (splitResult[t].Length > 0)
                {
                    var w = _textPaint.MeasureText(splitResult[t]) + (splitResult[t].Length - 1) * r;
                    if (w > i) i = w;
                }
            }
        }

        return new TextMetrics
        {
            Width = i,
            Height = GetLineHeight(e) * splitResult.Count + a
        };
    }

    /// <summary>
    /// 测量适合宽度的字体大小。对应 JS <c>c.measureFontSize(t)</c>。
    /// 从给定字号开始，每次 ×0.95 缩小，直到文本宽度 ≤ 目标宽度或达到最小字号。
    /// </summary>
    public double MeasureFontSize(DrawOptions opt)
    {
        var e = opt.Text as string ?? opt.Text?.ToString() ?? "";
        var i = opt.Width ?? 0;
        var s = opt.MinFontHeight ?? 6;

        if (i <= 0 || e.Length <= 0) return opt.FontHeight ?? _fontHeight;

        var r = opt.FontHeight ?? _fontHeight;
        double n;
        do
        {
            n = MeasureText(new DrawOptions
            {
                Text = e,
                FontHeight = r,
                FontStyle = opt.FontStyle,
                FontName = opt.FontName
            }).Width;
            if (n <= i) return r;
            r *= 0.95;
        } while (r > s);
        return s;
    }

    // ============ splitText / findSplitPosition ============

    /// <summary>
    /// 查找文本在指定宽度内的拆分位置。对应 JS <c>c.findSplitPosition(t, e, i, s)</c>。
    /// 二分查找最大字符数 a，使 text.Substring(0, a) 在宽度 e 内。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="maxWidth">最大宽度。</param>
    /// <param name="charSpace">字符间距。</param>
    /// <param name="step">优化步长（&gt;0 时按步长扫描，0 时直接二分）。</param>
    public int FindSplitPosition(string text, double maxWidth, double charSpace, double step = 0)
    {
        if (text.Length <= 1) return text.Length;

        // JS: let a = 1, o = ctx.measureText(t.substring(0, a)).width || 0
        var a = 1;
        double o = _textPaint.MeasureText(text.Substring(0, a));
        if (o <= 0) return text.Length;
        if (o >= maxWidth) return a;

        double cWidth;
        int c;
        if (step > 0)
        {
            // JS: r > 0 时按步长扫描
            var h = 0.0;
            c = 0;
            h = 0;
            while (h < maxWidth && c < text.Length)
            {
                if (c + (int)step > text.Length) c = text.Length;
                else
                {
                    if (h > o) { a = c; o = h; }
                    c += (int)step;
                }
                h = _textPaint.MeasureText(text.Substring(0, c)) + (c - 1) * charSpace;
            }
        }
        else
        {
            c = text.Length;
            cWidth = _textPaint.MeasureText(text) + (text.Length - 1) * charSpace;
            if (cWidth <= maxWidth) return c;
            // 进入二分
            while (a < c && c != a + 1)
            {
                var mid = a + (int)Math.Floor((c - a) / 2.0);
                var w = _textPaint.MeasureText(text.Substring(0, mid)) + (mid - 1) * charSpace;
                if (w > maxWidth) { c = mid; cWidth = w; }
                else if (a == mid) { a = mid; o = w; break; }
                else { a = mid; o = w; if (w >= maxWidth) break; }
            }
            return a;
        }

        // step > 0 路径的最终判定
        cWidth = _textPaint.MeasureText(text) + (text.Length - 1) * charSpace;
        if (cWidth <= maxWidth) return text.Length;

        // 二分
        while (a < c && c != a + 1)
        {
            var mid = a + (int)Math.Floor((c - a) / 2.0);
            var w = _textPaint.MeasureText(text.Substring(0, mid)) + (mid - 1) * charSpace;
            if (w > maxWidth) { c = mid; }
            else { a = mid; o = w; if (w >= maxWidth) break; }
        }
        return a;
    }

    /// <summary>
    /// 查找单词拆分位置。对应 JS <c>c.findWordSplitPos(t, e)</c>。
    /// 若 e 位置非单词边界，向前回溯到最近的非字母数字字符后一位。
    /// </summary>
    public int FindWordSplitPos(string text, int pos)
    {
        // JS: /\W/.exec(t.charAt(e))  —— 非单词字符
        if (pos >= text.Length || IsNonWordChar(text[pos])) return pos;
        var i = pos - 1;
        while (i >= 0 && !IsNonWordChar(text[i])) i--;
        return i + 1;
    }

    private static bool IsNonWordChar(char c) => !char.IsLetterOrDigit(c) && c != '_';

    /// <summary>
    /// 获取字符的自定义宽度（无 measureText 时的回退）。对应 JS <c>c.getCustomCharWidth(t, e)</c>。
    /// 中文 = t；字母 = 0.55t；数字 = 0.5t；空白 = 0.25t；其他 = 0.5t。
    /// </summary>
    public static double GetCustomCharWidth(double fontHeight, char c)
    {
        if (c == '\0' || fontHeight <= 0) return 0;
        // JS: /[\u4e00-\u9fa5]/.test(e) → 中文
        if (c >= 0x4e00 && c <= 0x9fa5) return fontHeight;
        // JS: /[a-zA-Z]/.test(e)
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return 0.55 * fontHeight;
        // JS: /[0-9]/.test(e)
        if (c >= '0' && c <= '9') return 0.5 * fontHeight;
        // JS: /\s/.test(e)
        if (char.IsWhiteSpace(c)) return 0.25 * fontHeight;
        return 0.5 * fontHeight;
    }

    /// <summary>
    /// 拆分文本为多行。对应 JS <c>c.splitText(e)</c>。
    /// 处理换行符、按宽度和换行模式自动换行。
    /// </summary>
    public List<string> SplitText(DrawOptions opt)
    {
        // JS: c.isNull(e.text) && !c.isNull(e.content) && (e.text = e.content)
        if (opt.Text == null && opt.Content != null) opt.Text = opt.Content;
        if (opt.Text == null) return new List<string>();

        // JS: "number" != typeof e.charSpace && (e.charSpace = this.CharSpace)
        if (!opt.CharSpace.HasValue) opt.CharSpace = _charSpace;
        var charSpace = opt.CharSpace > 0 ? opt.CharSpace.Value : 0;

        // JS: s = Array.isArray(e.text) ? e.text : [String(e.text)]
        var srcTexts = opt.Text is string[] arr
            ? arr
            : new[] { opt.Text is string ss ? ss : opt.Text!.ToString() ?? "" };

        // JS: n.push(...t.split("\n"))
        var n = new List<string>();
        foreach (var t in srcTexts)
            n.AddRange(t.Split('\n'));

        var d = opt.FontHeight ?? _fontHeight;
        var u = opt.Width ?? 0;
        SetFont(opt.FontHeight, opt.FontStyle, opt.FontName);
        var l = GetReturnMode(opt.AutoReturn);

        var h = new List<string>();
        var measureAvailable = true;  // SkiaSharp 总是支持 MeasureText

        foreach (var s in n)
        {
            if (s.Length > 0 && l != WrapMode.None && u > 0 && measureAvailable)
            {
                // JS: 有 measureText 且启用换行
                var remaining = s;
                while (remaining.Length > 0)
                {
                    var a = FindSplitPosition(remaining, u, charSpace, opt.MeasureOptimizeStep ?? 0);
                    if (l == WrapMode.Word)
                    {
                        var o = FindWordSplitPos(remaining, a);
                        if (o > 0 && o < a) a = o;
                    }
                    h.Add(remaining.Substring(0, a));
                    remaining = a < remaining.Length ? remaining.Substring(a) : "";
                }
            }
            else if (!measureAvailable && u > 0)
            {
                // JS: 无 measureText 的回退（按字符宽度累加）
                var t = 0.0;
                var e = 0;
                for (var i = 0; i < s.Length; i++)
                {
                    var w = GetCustomCharWidth(d, s[i]);
                    t += w;
                    if (t > u && i > e)
                    {
                        h.Add(s.Substring(e, i - e));
                        e = i;
                        t = w;
                    }
                }
                if (e < s.Length) h.Add(s.Substring(e));
            }
            else
            {
                h.Add(s);
            }
        }
        return h;
    }

    // ============ drawText ============

    /// <summary>
    /// 绘制文本（支持多行、自动换行、自动缩放、反色、旋转）。对应 JS <c>c.drawText(e)</c>。
    /// </summary>
    public bool DrawText(DrawOptions opt)
    {
        // JS: 默认值填充
        opt.X ??= 0;
        opt.Y ??= 0;
        opt.Width ??= 0;
        opt.Height ??= 0;

        var i = GetItemRotation(opt);
        var s = GetRotateMode(opt.RotateMode);
        var n = GetAntiColor(opt.AntiColor, AntiColorMode.AntiColor | AntiColorMode.FillFull);

        if (i > 0 || n != AntiColorMode.None)
        {
            var paddings = GetPaddings(opt);
            var o = paddings[1] + paddings[3];
            var h = paddings[0] + paddings[2];

            // JS: (e.width <= 0 || e.height <= 0) && void 0 !== ctx.measureText → 度量扩展
            if ((opt.Width <= 0 || opt.Height <= 0))
            {
                var t = MeasureTextExt(opt);
                if (opt.Width <= 0) opt.Width = t.Width + o;
                if (opt.Height <= 0) opt.Height = t.Height + h;
            }

            if (opt.Width <= 0 || opt.Height <= 0) return false;

            // JS: (n & FillFull) > 0 → 先画背景矩形
            if ((n & AntiColorMode.FillFull) != 0)
            {
                DrawRect(new DrawOptions
                {
                    X = opt.X,
                    Y = opt.Y,
                    Width = opt.Width,
                    Height = opt.Height,
                    Rotation = s == RotateMode.RotateContent ? 0 : i,
                    Color = opt.Color ?? _foreground,
                    Fill = true
                });
            }

            // JS: isPortrait(i) && s === RotateContent → 旋转 90°
            if (IsPortrait(i) && s == RotateMode.RotateContent)
            {
                // 用 Rect 包装再旋转
                var rect = new Rect(opt.X ?? 0, opt.Y ?? 0, opt.Width ?? 0, opt.Height ?? 0);
                RectUtils.Rotate90(rect);
                opt.X = rect.X;
                opt.Y = rect.Y;
                opt.Width = rect.Width;
                opt.Height = rect.Height;
            }

            var d = (opt.X ?? 0) + 0.5 * (opt.Width ?? 0);
            var u = (opt.Y ?? 0) + 0.5 * (opt.Height ?? 0);
            _ctx!.Save();
            SetRotation(i, new SKPoint((float)d, (float)u), null);
            var l = DrawTextCore(opt);
            _ctx.Restore();
            return l;
        }

        return DrawTextCore(opt);
    }

    /// <summary>实际绘制文本。对应 JS <c>c._drawText(e)</c>。</summary>
    private bool DrawTextCore(DrawOptions opt)
    {
        // JS: 坐标 NaN 处理
        if (!opt.X.HasValue || double.IsNaN(opt.X.Value)) opt.X = 0;
        if (!opt.Y.HasValue || double.IsNaN(opt.Y.Value)) opt.Y = 0;

        // JS: null == e.text && (void 0 === e.content ? return false : e.text = e.content)
        if (opt.Text == null)
        {
            if (opt.Content == null) return false;
            opt.Text = opt.Content;
        }

        var antiColor = GetAntiColor(opt.AntiColor, AntiColorMode.AntiColor | AntiColorMode.FillFull);
        var fgColor = opt.Color ?? _foreground;
        var bgColor = opt.BgColor ?? _background;
        var minFontHeight = opt.MinFontHeight ?? 6;
        var autoReturn = GetReturnMode(opt.AutoReturn);
        var autoShrink = opt.AutoShrink ?? true;
        var paddings = GetPaddings(opt);
        var padH = paddings[1] + paddings[3];
        var padV = paddings[0] + paddings[2];

        // JS: 反色时前景色与背景色互换
        var textColor = fgColor;
        if (antiColor != AntiColorMode.None)
        {
            textColor = IsTransparent(bgColor) ? ColorBgDefault : bgColor;
        }

        var startY = (opt.Y ?? 0) + paddings[0];
        var width = opt.Width ?? 0;
        var height = opt.Height ?? 0;

        // JS: (!e.texts || e.texts.length <= 0) && (e.texts = Array.isArray(e.text) ? e.text : [String(e.text)])
        if (opt.Texts == null || opt.Texts.Length == 0)
        {
            opt.Texts = opt.Text is string[] arr
                ? arr
                : new[] { opt.Text is string s ? s : opt.Text!.ToString() ?? "" };
        }

        var f = opt.Texts;
        var P = opt.FontHeight ?? (height > 0 ? height : _fontHeight);
        var R = GetHorizontalAlignment(opt.HorizontalAlignment);
        var C = GetVerticalAlignment(opt.VerticalAlignment);

        if (P <= 0) return false;

        // JS: 对齐值范围校验
        if (R > Alignment.Stretch || R < Alignment.Start) R = Alignment.Start;
        if (C > Alignment.Stretch || C < Alignment.Start) C = Alignment.Start;

        // JS: "number" != typeof e.charSpace && (e.charSpace = this.CharSpace)
        if (!opt.CharSpace.HasValue) opt.CharSpace = _charSpace;
        var A = opt.CharSpace > 0 ? opt.CharSpace.Value : 0;

        _fillPaint.Color = ParseColor(textColor);
        _textPaint.Color = ParseColor(textColor);
        SetFont(P, opt.FontStyle, opt.FontName);
        SetTextBaseline(TextBaselineDefault);

        // JS: let I = []; for (let t of f) ... 处理 tab 和换行
        var I = new List<string>();
        foreach (var t in f)
        {
            var str = t is string ts ? ts : (t?.ToString() ?? "");
            str = str.Replace("\t", "");
            I.AddRange(str.Split('\n'));
        }

        // JS: a !== None && p > 0 → 按宽度拆分
        if (autoReturn != WrapMode.None && width > 0)
        {
            var splitOpt = new DrawOptions
            {
                Text = I.ToArray(),
                Width = width - padH,
                FontHeight = P,
                AutoReturn = autoReturn,
                FontStyle = opt.FontStyle,
                FontName = opt.FontName,
                CharSpace = opt.CharSpace,
                LineSpace = opt.LineSpace,
                MeasureOptimizeStep = opt.MeasureOptimizeStep
            };
            I = SplitText(splitOpt);
        }

        var y = GetLineSpace(opt.LineSpace ?? 0, P);

        if (I.Count > 0)
        {
            // JS: a = getLineHeight(P) * I.length; l = a + (I.length-1) * y
            var a = GetLineHeight(P) * I.Count;
            var l = a + (I.Count - 1) * y;

            // JS: m <= 0 && (m = l + u)
            if (height <= 0) height = l + padV;

            if (l <= height || !autoShrink || P < minFontHeight)
            {
                // JS: 不需要缩小字号
                if (C == Alignment.Stretch)
                {
                    // JS: y = (m - a - h[0] - h[2]) / (I.length - 1)
                    y = I.Count > 1 ? (height - a - paddings[0] - paddings[2]) / (I.Count - 1) : 0;
                }
                else if (C == Alignment.End)
                {
                    startY += height - l - padV;
                }
                else if (C == Alignment.Center)
                {
                    startY += 0.5 * (height - padV - l);
                }

                DrawTextList1(new DrawOptions
                {
                    Texts = I.ToArray(),
                    X = (opt.X ?? 0) + paddings[3],
                    Y = startY,
                    Width = width - padH,
                    FontHeight = P,
                    LineSpace = y,
                    CharSpace = A,
                    HorizontalAlignment = R,
                    FontStyle = opt.FontStyle ?? (FontStyleFlag)_fontStyle,
                    Color = antiColor != AntiColorMode.None ? bgColor : fgColor
                });
            }
            else
            {
                // JS: 缩小到 0.95*P 重试
                var newOpt = opt.Clone();
                newOpt.Text = f;  // 用原始文本重新拆分
                newOpt.Width = width;
                newOpt.FontHeight = 0.95 * P;
                return DrawTextCore(newOpt);
            }
        }
        return true;
    }

    /// <summary>
    /// 绘制多行文本。对应 JS <c>c.drawTextList1(t)</c>。
    /// </summary>
    private void DrawTextList1(DrawOptions opt)
    {
        var ls = opt.LineSpace as double? ?? 0;
        var lineSpace = ls > 0 ? ls : 0;
        var lineHeight = GetLineHeight(opt.FontHeight ?? _fontHeight);
        var s = 0.0;
        var texts = opt.Texts ?? Array.Empty<string>();
        foreach (var n in texts)
        {
            DrawSingleLineText(
                n,
                opt.X ?? 0,
                (opt.Y ?? 0) + s,
                opt.Width ?? 0,
                opt.FontHeight ?? _fontHeight,
                opt.HorizontalAlignment ?? Alignment.Start,
                opt.CharSpace ?? 0,
                opt.FontStyle ?? FontStyleFlag.Regular,
                opt.Color ?? _foreground);
            s += lineHeight + lineSpace;
        }
    }

    /// <summary>绘制单行文本。对应 JS <c>c._drawSingleLineText(e, i, s, n, r, a, o, h, d)</c>。</summary>
    private void DrawSingleLineText(
        string text, double x, double y, double width, double fontHeight,
        Alignment align, double charSpace, FontStyle style, string color)
    {
        text = text ?? string.Empty;
        // JS: u = .2*r; l = r - u
        var u = 0.2 * fontHeight;
        var l = fontHeight - u;

        SetFont(fontHeight, style, null);
        var g = MeasureText(new DrawOptions { Text = text, FontHeight = fontHeight }, false);
        var p = g.ActualBoundingBoxAscent > 0 ? g.ActualBoundingBoxAscent : l;
        var m = g.ActualBoundingBoxDescent > 0 ? g.ActualBoundingBoxDescent : u;

        var f = text.Length > 1 && charSpace > 0 ? (text.Length - 1) * charSpace : 0;
        var P = align;
        var R = g.Width;
        var C = 0.05 * fontHeight;
        if (C < 1) C = 1;

        var A = y + GetLineHeight(l);
        var I = p + m;

        if (g != null && P == Alignment.Stretch && width > g.Width)
        {
            // JS: 拉伸对齐 → 重新分配字符间距
            charSpace = text.Length > 1 ? (width - g.Width) / (text.Length - 1) : 0;
            if (text.Length == 1)
            {
                _ctx!.DrawText(text, (float)(x + 0.5 * (width - g.Width)), (float)A, _textPaint);
            }
            else
            {
                FillCharSpaceText(text, x, A, charSpace, 0);
            }
            R = width;
        }
        else
        {
            // JS: 普通对齐
            var s = g != null ? g.Width + f : 0;
            if (width > s)
            {
                if (P == Alignment.End) x = x + width - s;
                else if (P == Alignment.Center) x += 0.5 * (width - s);
            }

            if (width > 0 && width < s)
            {
                // JS: 文本超出宽度 → 缩放绘制
                FillCharSpaceText(text, x, A, charSpace, width);
                R = width;
            }
            else
            {
                FillCharSpaceText(text, x, A, charSpace, 0);
                R = s;
            }
        }

        // JS: 删除线
        if ((style & FontStyleFlag.Strikeout) != 0)
        {
            // 复用 DrawRectCore
            DrawRectCore(new DrawOptions
            {
                X = x,
                Y = A - p + 0.5 * (I - C),
                Width = R,
                Height = C,
                Color = color,
                Fill = true
            });
        }
        // JS: 下划线
        if ((style & FontStyleFlag.Underline) != 0)
        {
            DrawRectCore(new DrawOptions
            {
                X = x,
                Y = y + fontHeight - 0.5 * C,
                Width = R,
                Height = C,
                Color = color,
                Fill = true
            });
        }
    }

    /// <summary>
    /// 绘制带字符间距的文本。对应 JS <c>c.fillCharSpaceText(t, e, i, s, n)</c>。
    /// </summary>
    /// <param name="text">文本。</param>
    /// <param name="x">起始 X。</param>
    /// <param name="y">基线 Y。</param>
    /// <param name="charSpace">字符间距。</param>
    /// <param name="maxWidth">最大宽度（&gt;0 时按比例缩放，0 不缩放）。</param>
    private void FillCharSpaceText(string text, double x, double y, double charSpace, double maxWidth)
    {
        if (text.Length <= 0) return;

        var totalWidth = _textPaint.MeasureText(text);
        var charSpaceTotal = text.Length > 1 ? (text.Length - 1) * charSpace : 0;

        if (charSpace > 0)
        {
            var fullWidth = totalWidth + charSpaceTotal;
            var scale = maxWidth > 0 && maxWidth < fullWidth ? maxWidth / fullWidth : 1.0;
            var h = x;
            foreach (var ch in text)
            {
                var w = _textPaint.MeasureText(ch.ToString());
                _ctx!.DrawText(ch.ToString(), (float)h, (float)y, _textPaint);
                h += (w + charSpace) * scale;
            }
        }
        else if (maxWidth > 0)
        {
            // JS: ctx.fillText(t, e, i, n)  —— 第 4 参数 maxWidth
            // SkiaSharp 不直接支持 maxWidth 缩放，用 scale 变换实现
            var scale = totalWidth > 0 && maxWidth < totalWidth ? maxWidth / totalWidth : 1.0;
            if (scale < 1.0)
            {
                _ctx!.Save();
                _ctx.Scale((float)scale, 1);
                _ctx.DrawText(text, (float)(x / scale), (float)y, _textPaint);
                _ctx.Restore();
            }
            else
            {
                _ctx!.DrawText(text, (float)x, (float)y, _textPaint);
            }
        }
        else
        {
            _ctx!.DrawText(text, (float)x, (float)y, _textPaint);
        }
    }

    // ============ drawArcText ============

    /// <summary>
    /// 绘制弧形文本。对应 JS <c>c.drawArcText(e)</c>。
    /// 文本沿圆形路径分布，每个字符独立旋转。
    /// </summary>
    public bool DrawArcText(DrawOptions opt)
    {
        // JS: 文本回退
        if (opt.Text == null && opt.Content != null) opt.Text = opt.Content;
        if (opt.Text == null) return false;

        var i = opt.FontHeight ?? 0;
        if (i <= 0) return false;

        var s = new Rect(opt.X ?? 0, opt.Y ?? 0, opt.Width ?? 0, opt.Height ?? 0);
        var n = new SKPoint((float)s.X, (float)s.Y);
        var lineWidth = opt.LineWidth ?? 0;
        var padding0 = GetPaddings(opt)[0];
        var antiColor = GetAntiColor(opt.AntiColor, AntiColorMode.AntiColor | AntiColorMode.AntiBackground);
        var h_flag = AntiColorMode.AntiBackground | AntiColorMode.FillFull;
        var fgColor = opt.Color ?? _foreground;

        // JS: 反色处理
        var textColor = fgColor;
        if ((antiColor & h_flag) != 0)
        {
            textColor = opt.BgColor ?? _background;
            if (string.IsNullOrEmpty(textColor) || IsTransparent(textColor))
                textColor = ColorBgDefault;
        }

        // JS: 计算半径
        var radius = opt.Radius ?? 0;
        if (radius <= 0)
        {
            if (s.Width <= 0 && s.Height <= 0) return false;
            if (s.Width > 0 && s.Height > 0)
            {
                radius = 0.5 * Math.Min(s.Width, s.Height);
                n.X += (float)(0.5 * s.Width);
                n.Y += (float)(0.5 * s.Height);
            }
            else if (s.Width > 0)
            {
                s.Height = s.Width;
                radius = 0.5 * s.Width;
                n.X += (float)radius;
            }
            else
            {
                s.Width = s.Height;
                radius = 0.5 * s.Height;
                n.Y += (float)radius;
            }
        }
        else
        {
            s.X -= radius;
            s.Y -= radius;
            s.Width = s.Height = 2 * radius;
        }

        // JS: 填充背景
        if ((antiColor & AntiColorMode.FillFull) != 0)
        {
            DrawRect(new DrawOptions
            {
                X = s.X,
                Y = s.Y,
                Width = s.Width,
                Height = s.Height,
                Rotation = opt.Rotation,
                Color = fgColor,
                Fill = true
            });
        }
        else if ((antiColor & AntiColorMode.AntiBackground) != 0)
        {
            DrawCircle(new DrawOptions
            {
                X = n.X,
                Y = n.Y,
                LineWidth = lineWidth,
                Radius = radius,
                Color = fgColor,
                Fill = true
            });
        }

        // JS: 描边圆
        if (lineWidth > 0)
        {
            DrawCircle(new DrawOptions
            {
                X = n.X,
                Y = n.Y,
                LineWidth = lineWidth,
                Radius = radius,
                Color = textColor
            });
            radius -= lineWidth;
        }

        var text = opt.Text as string ?? opt.Text!.ToString() ?? "";
        radius -= i;
        if (padding0 > 0 && padding0 < radius) radius -= padding0;

        // JS: 计算文本总弧度
        var m = 2 * Math.PI * radius;
        var measuredWidth = _textPaint.MeasureText(text);
        var f = measuredWidth / m * Math.PI * 2;

        SetFont(0.95 * i, opt.FontStyle, opt.FontName);
        _fillPaint.Color = ParseColor(textColor);
        _ctx!.Save();

        var rotation = GetItemRotation(opt);
        SetRotation(rotation, n, null);
        _ctx.Translate(n.X, n.Y);
        _ctx.RotateDegrees(-90);  // JS: rotate(.5 * -Math.PI) → -90°

        var oldBaseline = _textBaseline;
        SetTextBaseline("bottom");

        var charArray = text.ToCharArray();
        for (var idx = 0; idx < charArray.Length; idx++)
        {
            var ch = charArray[idx].ToString();
            var charWidth = _textPaint.MeasureText(ch);
            var charArc = charWidth / m * Math.PI * 2;

            if (idx == 0)
            {
                _ctx.RotateRadians((float)(0.5 * -f + 0.5 * charArc));
            }
            else
            {
                _ctx.RotateRadians((float)charArc);
            }

            _ctx.Save();
            _ctx.RotateDegrees(90);  // JS: rotate(PI/2)
            _ctx.Translate((float)(0.5 * -charWidth), (float)(-radius));
            _ctx.DrawText(ch, 0, 0, _textPaint);
            _ctx.Restore();
        }

        SetTextBaseline(oldBaseline);
        _ctx.Restore();
        return true;
    }

    // ============ getTextWidths ============

    /// <summary>
    /// 批量获取文本宽度。对应 JS <c>c.getTextWidths(t)</c>。
    /// </summary>
    public List<double> GetTextWidths(IList<string>? texts)
    {
        var result = new List<double>();
        if (texts == null) return result;
        foreach (var i in texts)
        {
            result.Add(_textPaint.MeasureText(i));
        }
        return result;
    }
}

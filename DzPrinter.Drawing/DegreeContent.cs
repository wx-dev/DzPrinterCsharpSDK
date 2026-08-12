namespace DzPrinter.Drawing;

// =====================================================================
//  DegreeContent（角度内容解析器）。对应 JS SDK 中 <c>Ie</c> 类。
//  JS 中 <c>Ie</c> 用于弧文本（ArcText）渲染：
//    - 从文本中提取数字部分（角度值）
//    - 支持步进递增/递减（step）
//    - 支持前导零填充（ShownDegree）
//    - 解析结果包含：左文本 + 角度数字 + 右文本
//
//  典型用途：在弧形标签上打印递增的序号/角度值。
//  例如 "ABC000123DEF" → contentLeft="ABC", currValue=123, contentRight="DEF"
// =====================================================================

/// <summary>
/// 角度内容解析器。对应 JS SDK 中的 <c>Ie</c>（DegreeContent）类。
/// 从文本中提取数字部分，支持步进递增与前导零填充。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>Ie</c> 在弧文本渲染中用于处理可变数字部分。</para>
/// <para><b>解析规则</b>：从文本末尾向前查找连续数字段，提取为当前值。
/// <c>degreeOffset</c> 为 0 表示无效（无数值部分）。</para>
/// </remarks>
public sealed class DegreeContent
{
    // ============ 静态常量 ============
    // 对应 JS: Ie.MaxDegreeLength=15, Ie.MaxDegreeValue=10^15-1, Ie.MaxDegreeOffset

    /// <summary>最大数字长度。JS: <c>Ie.MaxDegreeLength = 15</c>。</summary>
    public const int MaxDegreeLength = 15;

    /// <summary>最大角度值。JS: <c>Ie.MaxDegreeValue = Math.pow(10, 15) - 1</c>。</summary>
    public static readonly long MaxDegreeValue = (long)Math.Pow(10, MaxDegreeLength) - 1;

    /// <summary>最大角度偏移量。JS: <c>Ie.MaxDegreeOffset = Math.pow(10, MaxDegreeLength)</c>。</summary>
    public static readonly long MaxDegreeOffset = (long)Math.Pow(10, MaxDegreeLength);

    // ============ 实例字段 ============

    private long _currValue;

    /// <summary>数字部分左侧文本。对应 JS <c>this.contentLeft</c>。</summary>
    public string ContentLeft { get; private set; } = string.Empty;

    /// <summary>数字部分右侧文本。对应 JS <c>this.contentRight</c>。</summary>
    public string ContentRight { get; private set; } = string.Empty;

    /// <summary>当前数字值的字符串长度（含前导零）。对应 JS <c>this.currLength</c>。</summary>
    public int CurrLength { get; private set; }

    /// <summary>角度偏移量（步进值）。0 表示无效。对应 JS <c>this.degreeOffset</c>。</summary>
    public int DegreeOffset { get; private set; }

    /// <summary>最大角度值限制。</summary>
    public long MaxDegreeValueLimit { get; private set; }

    /// <summary>
    /// 是否有效（degreeOffset 不为 0）。对应 JS <c>get IsValid()</c>。
    /// </summary>
    public bool IsValid => DegreeOffset != 0;

    /// <summary>
    /// 当前角度值。对应 JS <c>get CurrValue()</c> / <c>set CurrValue(t)</c>。
    /// </summary>
    public long CurrValue
    {
        get => _currValue;
        set
        {
            if (value > MaxDegreeValue) _currValue = MaxDegreeValue;
            else _currValue = value < 0 ? 0 : value;
        }
    }

    /// <summary>
    /// 获取显示的角度字符串（含前导零）。对应 JS <c>get ShownDegree</c>。
    /// </summary>
    public string ShownDegree
    {
        get
        {
            if (!IsValid) return string.Empty;
            var val = CurrValue;
            var sign = val < 0 ? "-" : string.Empty;
            val = Math.Abs(val);
            var str = val.ToString();
            if (str.Length < CurrLength)
                str = new string('0', CurrLength - str.Length) + str;
            return sign + str;
        }
    }

    // ============ 构造函数 ============

    /// <summary>
    /// 构造 DegreeContent。对应 JS <c>Ie.constructor(text, degreeOffset, length)</c>。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <param name="degreeOffset">步进偏移量（默认 1）。</param>
    /// <param name="length">数字部分固定长度（0 表示自动）。</param>
    public DegreeContent(string? text, int degreeOffset = 1, int length = 0)
    {
        MaxDegreeValueLimit = long.MaxValue;
        if (string.IsNullOrEmpty(text)) return;

        degreeOffset = degreeOffset == 0 ? 1 : degreeOffset;
        length = length < 0 ? 0 : length;

        // 从末尾向前查找连续数字段
        var n = text!.Length - 1;
        while (n >= 0 && !IsDigit(text[n])) n--;
        if (n < 0) return;

        // 继续向前查找数字段的起点
        var s = n - 1;
        while (s >= 0 && IsDigit(text[s]) && (n - s) < MaxDegreeLength) s--;
        s++;

        // 提取数字值
        _currValue = long.Parse(text.Substring(s, n - s + 1));
        MaxDegreeValueLimit = MaxDegreeValue;
        ContentLeft = text.Substring(0, s);
        ContentRight = text.Substring(n + 1);
        CurrLength = length > 0 ? length : n - s + 1;
        DegreeOffset = degreeOffset;
    }

    // ============ 方法 ============

    /// <summary>
    /// 步进递增/递减。对应 JS <c>step(t)</c>。
    /// </summary>
    /// <param name="steps">步数（正=递增，负=递减）。</param>
    /// <returns>步进后的完整文本。</returns>
    public string Step(int steps)
    {
        CurrValue += (long)DegreeOffset * steps;
        return ToString();
    }

    /// <summary>
    /// 转为字符串。对应 JS <c>toString()</c>。
    /// </summary>
    /// <returns>左文本 + 角度数字 + 右文本。</returns>
    public override string ToString() =>
        IsValid ? ContentLeft + ShownDegree + ContentRight : string.Empty;

    // ============ 私有方法 ============

    private static bool IsDigit(char c) => c >= '0' && c <= '9';
}

namespace DzPrinter.Barcode;

// =====================================================================
//  DzChar / DzCharCodes（字符工具）。对应 JS SDK 中 <c>R</c> 和 <c>P</c>。
//  JS 中 <c>R</c>（DzChar）提供字符判断与转换的静态方法，
//  <c>P</c>（DzCharCodes）提供 ASCII 字符码常量。
//  主要用于条码编码中的字符处理（CODE128 / CODE39 等）。
// =====================================================================

/// <summary>
/// 字符码常量。对应 JS SDK 中的 <c>P</c>（DzCharCodes）。
/// 提供 ASCII 字符码常量，供条码编码使用。
/// </summary>
public static class DzCharCodes
{
    /// <summary>数字 '0' 的字符码。JS: <c>P.num_0</c>。</summary>
    public const int Num0 = '0'; // 48

    /// <summary>数字 '9' 的字符码。JS: <c>P.num_9</c>。</summary>
    public const int Num9 = '9'; // 57

    /// <summary>大写字母 'A' 的字符码。JS: <c>P.A</c>。</summary>
    public const int UpperA = 'A'; // 65

    /// <summary>大写字母 'Z' 的字符码。JS: <c>P.Z</c>。</summary>
    public const int UpperZ = 'Z'; // 90

    /// <summary>小写字母 'a' 的字符码。JS: <c>P.a</c>。</summary>
    public const int LowerA = 'a'; // 97

    /// <summary>小写字母 'z' 的字符码。JS: <c>P.z</c>。</summary>
    public const int LowerZ = 'z'; // 122

    // 兼容 JS 命名（直接通过属性访问）
    // JS 中使用 P.num_0 / P.A / P.Z / P.a / P.z 等

    /// <summary>JS 兼容：num_0。对应 <see cref="Num0"/>。</summary>
    public static int num_0 => Num0;

    /// <summary>JS 兼容：num_9。对应 <see cref="Num9"/>。</summary>
    public static int num_9 => Num9;

    /// <summary>JS 兼容：A。对应 <see cref="UpperA"/>。</summary>
    public static int A => UpperA;

    /// <summary>JS 兼容：Z。对应 <see cref="UpperZ"/>。</summary>
    public static int Z => UpperZ;

    /// <summary>JS 兼容：a。对应 <see cref="LowerA"/>。</summary>
    public static int a => LowerA;

    /// <summary>JS 兼容：z。对应 <see cref="LowerZ"/>。</summary>
    public static int z => LowerZ;
}

/// <summary>
/// 字符工具类。对应 JS SDK 中的 <c>R</c>（DzChar）。
/// 提供字符判断（数字/大写/小写）与字符到整数的转换。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>R</c> 用于条码编码中的字符分类与转换，
/// 如 CODE128 的字符集切换、CODE39 的字符验证等。</para>
/// </remarks>
public static class DzChar
{
    /// <summary>
    /// 判断是否为数字。对应 JS <c>R.isDigit(t)</c>。
    /// 支持字符和字符码两种输入。
    /// </summary>
    /// <param name="c">字符或字符码。</param>
    /// <returns>是数字返回 true。</returns>
    public static bool IsDigit(int c) =>
        c >= DzCharCodes.Num0 && c <= DzCharCodes.Num9;

    /// <summary>
    /// 判断是否为数字。对应 JS <c>R.isDigit(t)</c>。
    /// </summary>
    public static bool IsDigit(char c) =>
        c >= '0' && c <= '9';

    /// <summary>
    /// 判断子串是否全为数字。对应 JS <c>R.isDigits(text, start, length)</c>。
    /// </summary>
    /// <param name="text">文本。</param>
    /// <param name="start">起始位置（默认 0）。</param>
    /// <param name="length">检查长度（0 表示到末尾）。</param>
    /// <returns>全为数字返回 true。</returns>
    public static bool IsDigits(string text, int start = 0, int length = 0)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var s = Math.Max(0, start);
        var n = length > 0 ? s + length : text.Length;
        if (n > text.Length) return false;
        for (var i = s; i < n; i++)
        {
            if (!IsDigit(text[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// 判断是否为大写字母。对应 JS <c>R.isUpper(t)</c>。
    /// </summary>
    public static bool IsUpper(int c) =>
        c >= DzCharCodes.UpperA && c <= DzCharCodes.UpperZ;

    /// <summary>
    /// 判断是否为大写字母。对应 JS <c>R.isUpper(t)</c>。
    /// </summary>
    public static bool IsUpper(char c) =>
        c >= 'A' && c <= 'Z';

    /// <summary>
    /// 判断是否为小写字母。对应 JS <c>R.isLower(t)</c>。
    /// </summary>
    public static bool IsLower(int c) =>
        c >= DzCharCodes.LowerA && c <= DzCharCodes.LowerZ;

    /// <summary>
    /// 判断是否为小写字母。对应 JS <c>R.isLower(t)</c>。
    /// </summary>
    public static bool IsLower(char c) =>
        c >= 'a' && c <= 'z';

    /// <summary>
    /// 字符转整数（进制转换）。对应 JS <c>R.ctoi(t)</c>。
    /// 数字 → 0-9，大写 → 10-35，小写 → 10-35。
    /// 不在范围内返回 -1。
    /// </summary>
    /// <param name="c">字符或字符码。</param>
    /// <returns>整数值；无效返回 -1。</returns>
    public static int Ctoi(int c)
    {
        if (c >= DzCharCodes.Num0 && c <= DzCharCodes.Num9)
            return c - DzCharCodes.Num0;
        if (c >= DzCharCodes.UpperA && c <= DzCharCodes.UpperZ)
            return c - DzCharCodes.UpperA + 10;
        if (c >= DzCharCodes.LowerA && c <= DzCharCodes.LowerZ)
            return c - DzCharCodes.LowerA + 10;
        return -1;
    }

    /// <summary>
    /// 字符转整数。对应 JS <c>R.ctoi(t)</c>。
    /// </summary>
    public static int Ctoi(char c) => Ctoi((int)c);

    /// <summary>
    /// 重复字符。对应 JS <c>R.repeatChar(char, count)</c>。
    /// </summary>
    /// <param name="c">要重复的字符。</param>
    /// <param name="count">重复次数。</param>
    /// <returns>重复后的字符串。</returns>
    public static string RepeatChar(char c, int count) =>
        count > 0 ? new string(c, count) : string.Empty;

    /// <summary>
    /// 重复字符。对应 JS <c>R.repeatChar(str, count)</c>。
    /// </summary>
    public static string RepeatChar(string str, int count) =>
        count > 0 ? string.Concat(Enumerable.Repeat(str, count)) : string.Empty;
}

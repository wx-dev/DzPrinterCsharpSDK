using System.Text;

namespace DzPrinter.Barcode;

/// <summary>
/// ITF/Code25 类条码的位展开工具。对应 JS SDK 中 <c>V</c> 类。
/// 将数字字符串按 1/0 交替Latch 的方式展开为模块序列。
/// 该类被 at/ot/ct/ht/dt/yt/bt/vt/It/Pt/Ct 等多个 1D 编码器共用。
/// </summary>
internal sealed class ItfExpander
{
    private static readonly ItfExpander Instance = new();

    /// <summary>当前 latch 状态（true=1，false=0）。对应 JS <c>V.latch</c>。</summary>
    private bool _latch = true;

    /// <summary>累计展开后的全部模块序列。对应 JS <c>V.allEncodes</c>。</summary>
    private string _allEncodes = string.Empty;

    /// <summary>
    /// 内部构造。对应 JS 中 <c>new V()</c> 创建新实例（latch=true）。
    /// bt/It 等编码器需要在新实例上多次调用 <see cref="Expand(string)"/> 以保持 latch 跨调用延续。
    /// </summary>
    internal ItfExpander() { }

    /// <summary>
    /// 提取字符串中的数字部分。对应 JS <c>V.getDigitText(t)</c>。
    /// </summary>
    public static string GetDigitText(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : new string(text.Where(c => c >= '0' && c <= '9').ToArray());

    /// <summary>
    /// 获取单例。对应 JS <c>V.getInstance()</c>。
    /// </summary>
    public static ItfExpander GetInstance() => Instance;

    /// <summary>
    /// 静态编码入口。对应 JS <c>V.encode(t)</c>。
    /// 内部使用单例，调用前会重置 latch。
    /// </summary>
    public static string Encode(string text) => GetInstance().Init().Expand(text);

    /// <summary>
    /// 计算 GS1/ITF 校验位。对应 JS <c>V.checkDigit(t, e)</c>。
    /// 算法：从左到右交替权重 3/1（起始权重取决于长度奇偶），加和取模 10 后用 10 减。
    /// </summary>
    /// <param name="text">输入数字串。</param>
    /// <param name="length">参与计算的长度（默认为整串长度）。</param>
    /// <returns>校验位字符。</returns>
    public static string CheckDigit(string text, int? length = null)
    {
        var len = length ?? text.Length;
        var sum = 0;
        var weight = (len & 1) == 1 ? 3 : 1;
        for (var i = 0; i < len; i++)
        {
            sum += weight * (text[i] - '0');
            weight ^= 2;  // 1 ↔ 3
        }
        return ((10 - sum % 10) % 10).ToString();
    }

    /// <summary>
    /// 重置 latch 为初始状态。对应 JS <c>V.init()</c>。
    /// </summary>
    public ItfExpander Init()
    {
        _latch = true;
        _allEncodes = string.Empty;
        return this;
    }

    /// <summary>
    /// 将数字字符串展开为模块序列。对应 JS <c>V.expand(t)</c>。
    /// 每个数字按其数值生成对应个数的 1 或 0，每位后 latch 翻转。
    /// </summary>
    public string Expand(string text)
    {
        var sb = new StringBuilder(text.Length * 5);
        for (var i = 0; i < text.Length; i++)
        {
            var count = text[i] - '0';
            for (var j = 0; j < count; j++) sb.Append(_latch ? '1' : '0');
            _latch = !_latch;
        }
        var result = sb.ToString();
        _allEncodes += result;
        return result;
    }
}

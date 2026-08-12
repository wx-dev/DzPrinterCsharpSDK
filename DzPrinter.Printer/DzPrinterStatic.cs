using DzPrinter.Core;
using System.Text.RegularExpressions;

namespace DzPrinter.Printer;

// =====================================================================
//  DzPrinter（静态工具类）。对应 JS SDK 中 <c>mi</c> 类。
//  JS 中 <c>mi</c> 是打印机名称解析、型号/渠道验证、设备管理器创建的核心工具类：
//    - getPrinterNameInfo(name)：解析打印机名，提取型号/序列号/渠道/校验和
//    - isSupportedDevice(name, ...)：判断设备名是否为支持的德佟打印机
//    - setSupportModels(models)：设置支持的机型列表
//    - setBleFilters(filters)：设置 BLE 过滤名称
//    - filterSupperTrades / filterNormalTrades：过滤特殊/普通渠道
//    - createWebHIDManager / createWebBLEManager：创建设备管理器（平台相关）
//
//  C# 实现策略：
//   - 静态类，保持与 JS 一致的 API 表面
//   - 设备管理器创建委托给外部注入的工厂（对应 JS 的 BleAdapter/HidAdapter）
//   - 打印机名称解析与校验和算法逐字节翻译
// =====================================================================

/// <summary>
/// 打印机静态工具类。对应 JS SDK 中的 <c>mi</c>（DzPrinter）类。
/// 提供打印机名称解析、型号验证、设备管理器创建等功能。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>mi</c> 是全局静态工具类，
/// 维护支持的机型列表、渠道列表、BLE 过滤器等配置。</para>
/// <para><b>名称解析</b>：德佟打印机名称格式为 <c>品牌-型号-序列号</c>，
/// 序列号包含渠道标识与校验和。<see cref="GetPrinterNameInfo"/> 解析此格式。</para>
/// </remarks>
public static class DzPrinter
{
    private static ILogger Log => DzLogger.Current;

    // ============ 静态字段 ============

    /// <summary>支持的机型列表（分号分隔）。对应 JS <c>mi.models</c>。</summary>
    public static string Models { get; private set; } = string.Empty;

    /// <summary>支持的渠道列表（分号分隔）。对应 JS <c>mi.trades</c>。</summary>
    public static string Trades { get; private set; } = string.Empty;

    /// <summary>BLE 过滤器名称列表。对应 JS <c>mi.bleFilters</c>。</summary>
    public static string[]? BleFilters { get; private set; }

    /// <summary>
    /// 机型匹配器。对应 JS <c>mi.matcher</c>。
    /// C# 中 <see cref="SupportPrinterMatcher"/> 为静态类，无需实例化，
    /// 匹配方法直接通过 <see cref="SupportPrinterMatcher.IsSupported"/> 等静态方法调用。
    /// </summary>
    public static bool HasMatcher => !string.IsNullOrEmpty(Models);

    /// <summary>自动关闭定时器（毫秒）。对应 JS <c>mi.closeTimer</c>。</summary>
    public static int CloseTimer { get; set; }

    /// <summary>关闭回调。对应 JS <c>mi.closeAction</c>。</summary>
    public static Action<LpaResult>? CloseAction { get; set; }

    /// <summary>德佟校验和字符表。对应 JS <c>mi.sDeTongCheckSum</c>。</summary>
    public const string SDeTongCheckSum = "0123456789";

    /// <summary>打印机名称通用匹配正则。对应 JS <c>mi.patternGeneral</c>。</summary>
    /// <remarks>
    /// 匹配格式：品牌(可选)-型号-序列号。如 "DZ-D110-DO12345678"。
    /// 捕获组：[0]=完整匹配, [1]=品牌前缀, [2]=序列号部分。
    /// </remarks>
    public static readonly Regex PatternGeneral = new(
        @"^(?:([A-Za-z]{1,2})-)?([A-Za-z0-9]+)-([A-Za-z0-9]+)$",
        RegexOptions.Compiled);

    // ============ 打印机名称解析 ============

    /// <summary>
    /// 解析打印机名称信息。对应 JS <c>mi.getPrinterNameInfo(name)</c>。
    /// </summary>
    /// <param name="name">打印机名称（如 "DZ-D110-DO12345678"）。</param>
    /// <returns>解析结果；解析失败返回 null。</returns>
    public static PrinterNameInfo? GetPrinterNameInfo(string? name)
    {
        name ??= string.Empty;
        var match = PatternGeneral.Match(name);
        if (!match.Success) return null;

        // 提取序列号部分
        var serials = match.Groups[3].Value;
        if (serials.Length < 8) return null;

        var isAllDigits = Regex.IsMatch(serials, @"^\d+$");
        var trade = string.Empty;
        var checkSum = 0;

        // 提取渠道前缀（非数字开头部分）
        if (serials.Length > 1 && !char.IsDigit(serials[1]))
        {
            trade = serials.Substring(0, 2);
            serials = serials.Substring(2);
            checkSum += 11 * trade[0];
            checkSum += 13 * trade[1];
        }
        else if (serials.Length > 0 && !char.IsDigit(serials[0]))
        {
            trade = serials.Substring(0, 1);
            serials = serials.Substring(1);
            checkSum += 17 * trade[0];
        }

        if (serials.Length < 8) return null;

        // 校验和验证（仅当第4位不是'0'或非纯数字时）
        if (!isAllDigits || serials.Length >= 9 || serials[3] != '0')
        {
            if (isAllDigits)
            {
                checkSum += 2 * (serials[0] - '0');
                checkSum += 3 * (serials[1] - '0');
                checkSum += 5 * (serials[2] - '0');
                for (var a = 4; a < serials.Length; ++a)
                    checkSum += (serials[a] - '0') * ((a & 1) == 1 ? 9 : 7);
            }
            else
            {
                checkSum += 2 * (serials[0] - '0');
                checkSum += 3 * (serials[1] - '0');
                checkSum += 5 * (serials[2] - '0');
                for (var a = 4; a < serials.Length; ++a)
                    checkSum += serials[a] * ((a & 1) == 1 ? 9 : 7);
            }

            var checkDigit = checkSum % 10;
            if (checkDigit >= SDeTongCheckSum.Length ||
                SDeTongCheckSum[checkDigit] != serials[3])
                return null;
        }

        return new PrinterNameInfo
        {
            Model = match.Value,
            Serials = serials,
            Trade = trade,
            CheckSum = checkSum,
        };
    }

    // ============ 渠道验证 ============

    /// <summary>
    /// 判断是否为超级渠道。对应 JS <c>mi.isSupperTrade(trade, extraTrades)</c>。
    /// 超级渠道包括 "D"、"O" 以及额外指定的渠道。
    /// </summary>
    public static bool IsSupperTrade(string? trade, string[]? extraTrades = null)
    {
        if (string.IsNullOrEmpty(trade)) return false;
        if (trade == "D" || trade == "O") return true;
        if (extraTrades != null && extraTrades.Length > 0)
            return Array.IndexOf(extraTrades, trade) >= 0;
        return false;
    }

    /// <summary>
    /// 判断渠道是否受支持。对应 JS <c>mi.isTradeSupported(trade, supportedTrades)</c>。
    /// </summary>
    public static bool IsTradeSupported(string? trade, string[]? supportedTrades)
    {
        if (supportedTrades == null || supportedTrades.Length <= 0) return true;
        if (string.IsNullOrEmpty(trade)) return false;
        return IsSupperTrade(trade) || Array.IndexOf(supportedTrades, trade) >= 0;
    }

    /// <summary>
    /// 从渠道列表中筛选超级渠道（以 "#" 开头）。对应 JS <c>mi.filterSupperTrades(trades)</c>。
    /// </summary>
    public static string[] FilterSupperTrades(string[] trades) =>
        trades.Where(t => !string.IsNullOrEmpty(t) && t[0] == '#')
              .Select(t => t.Substring(1))
              .ToArray();

    /// <summary>
    /// 从渠道列表中筛选普通渠道（不以 "#" 开头）。对应 JS <c>mi.filterNormalTrades(trades)</c>。
    /// </summary>
    public static string[] FilterNormalTrades(string[] trades) =>
        trades.Where(t => !string.IsNullOrEmpty(t) && t[0] != '#').ToArray();

    // ============ 设备验证 ============

    /// <summary>
    /// 判断设备名是否为支持的德佟打印机。对应 JS <c>mi.isSupportedDevice(name, normalTrades, supperTrades)</c>。
    /// </summary>
    /// <param name="name">设备名称。</param>
    /// <param name="normalTrades">普通渠道列表（null 表示使用配置）。</param>
    /// <param name="supperTrades">超级渠道列表（null 表示使用配置）。</param>
    /// <returns>是否支持。</returns>
    public static bool IsSupportedDevice(string? name,
        string[]? normalTrades = null, string[]? supperTrades = null)
    {
        var info = GetPrinterNameInfo(name);
        if (info == null) return false;

        // 从 Trades 配置中提取渠道列表
        if (supperTrades == null || normalTrades == null)
        {
            var allTrades = !string.IsNullOrEmpty(Trades)
                ? Trades.Split(';')
                : Array.Empty<string>();
            supperTrades ??= FilterSupperTrades(allTrades);
            normalTrades ??= FilterNormalTrades(allTrades);
        }

        if (IsSupperTrade(info.Trade, supperTrades)) return true;
        if (normalTrades.Length > 0 && !IsTradeSupported(info.Trade, normalTrades))
        {
            Log.Info($"---- ----: 不支持的渠道: {info.Trade}");
            return false;
        }
        return true;
    }

    // ============ 配置方法 ============

    /// <summary>
    /// 设置支持的机型列表。对应 JS <c>mi.setSupportModels(models)</c>。
    /// </summary>
    /// <param name="models">机型列表（数组或分号分隔字符串）。</param>
    public static void SetSupportModels(string? models)
    {
        var modelStr = models ?? string.Empty;
        if (Models != modelStr)
        {
            Models = modelStr;
            Log.Info($"【DzPrinter】SetSupportModels() —— models={modelStr}");
        }
    }

    /// <summary>
    /// 设置 BLE 过滤器。对应 JS <c>mi.setBleFilters(filters)</c>。
    /// </summary>
    public static void SetBleFilters(string[]? filters)
    {
        BleFilters = filters;
        Log.Info($"【DzPrinter】SetBleFilters() —— count={filters?.Length ?? 0}");
    }

    /// <summary>
    /// 设置渠道列表。对应 JS <c>mi.trades</c> 赋值。
    /// </summary>
    public static void SetTrades(string? trades)
    {
        Trades = trades ?? string.Empty;
        Log.Info($"【DzPrinter】SetTrades() —— trades={Trades}");
    }

    // ============ 定时器 ============

    /// <summary>
    /// 取消自动关闭定时器。对应 JS <c>mi.cancelCloseTimer()</c>。
    /// </summary>
    public static void CancelCloseTimer()
    {
        if (CloseTimer > 0)
        {
            CloseTimer = 0;
            CloseAction?.Invoke(LpaResult.ErrorCancel);
        }
    }
}

/// <summary>
/// 打印机名称解析结果。对应 JS <c>mi.getPrinterNameInfo()</c> 的返回值。
/// </summary>
public sealed class PrinterNameInfo
{
    /// <summary>完整型号名称。</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>序列号。</summary>
    public string Serials { get; set; } = string.Empty;

    /// <summary>渠道标识。</summary>
    public string Trade { get; set; } = string.Empty;

    /// <summary>校验和。</summary>
    public int CheckSum { get; set; }

    /// <inheritdoc />
    public override string ToString() =>
        $"PrinterNameInfo(model={Model}, serials={Serials}, trade={Trade}, checkSum={CheckSum})";
}

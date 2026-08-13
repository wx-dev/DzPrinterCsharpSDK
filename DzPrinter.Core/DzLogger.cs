namespace DzPrinter.Core;

/// <summary>
/// 统一日志抽象。对应 JS SDK 中被各模块引用的 <c>a</c> 日志对象。
/// 各模块（Printer/Imaging/Transport/Jobs）通过本类输出日志，
/// 上层应用可注入 <see cref="ILogger"/> 实现来接管日志输出。
/// </summary>
/// <remarks>
/// 设计要点：
/// <list type="bullet">
///   <item>默认实现写入 <see cref="Debug"/> 监听器，便于开发期调试</item>
///   <item>通过 <see cref="SetLogger"/> 注入自定义实现（如 ILogger → Serilog/NLog/Unity 等）</item>
///   <item>线程安全：<see cref="SetLogger"/> 与各写入方法均使用锁保护</item>
/// </list>
/// </remarks>
public static class DzLogger
{
    private static readonly object _lock = new();
    private static ILogger? _logger;

    /// <summary>
    /// 注入自定义日志实现。设为 null 则恢复默认 <see cref="Debug"/> 输出。
    /// </summary>
    public static void SetLogger(ILogger? logger)
    {
        lock (_lock) { _logger = logger; }
    }

    /// <summary>当前日志实现（用于单元测试等场景）。</summary>
    public static ILogger Current
    {
        get { lock (_lock) { return _logger ?? DefaultLogger.Instance; } }
    }

    public static void Info(string message) => Current.Info(message);
    public static void Warn(string message) => Current.Warn(message);
    public static void Error(string message) => Current.Error(message);
    public static void Debug(string message) => Current.Debug(message);
}

/// <summary>
/// 日志接口。上层应用实现此接口即可接管 DzPrinter 所有模块的日志输出。
/// </summary>
public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Debug(string message);
}

/// <summary>
/// 默认日志实现：写入 <see cref="System.Diagnostics.Debug"/> 监听器。
/// 对应 JS SDK 中 <c>a.log</c>/<c>a.warn</c> 等直接打印到控制台。
/// </summary>
internal sealed class DefaultLogger : ILogger
{
    public static readonly DefaultLogger Instance = new();

    public void Info(string message) => System.Diagnostics.Trace.WriteLine($"[DzPrinter/INFO] {message}");
    public void Warn(string message) => System.Diagnostics.Trace.WriteLine($"[DzPrinter/WARN] {message}");
    public void Error(string message) => System.Diagnostics.Trace.WriteLine($"[DzPrinter/ERROR] {message}");
    public void Debug(string message) => System.Diagnostics.Trace.WriteLine($"[DzPrinter/DEBUG] {message}");
}

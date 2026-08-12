using DzPrinter.Core;

namespace DzPrinter.Protocol;

/// <summary>
/// 协议层日志桥接：将 <see cref="DzLogger"/> 的日志包装为带模块前缀的输出。
/// 所有日志最终走 <see cref="DzLogger"/>，上层可统一接管。
/// </summary>
internal static class DzProtocolLog
{
    private const string Prefix = "DzPrinter.Protocol";

    public static void Info(string message) => DzLogger.Info($"[{Prefix}] {message}");
    public static void Warn(string message) => DzLogger.Warn($"[{Prefix}] {message}");
    public static void Error(string message) => DzLogger.Error($"[{Prefix}] {message}");
}

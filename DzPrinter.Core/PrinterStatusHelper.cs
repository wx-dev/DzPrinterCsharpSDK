namespace DzPrinter.Core;

/// <summary>
/// 打印机状态码与消息。对应 JS SDK 中 <c>xe</c> 类的
/// <c>DZIP_*</c> 常量与 <c>getPrintableMessage()</c> 方法。
/// </summary>
public static class PrinterStatusHelper
{
    /// <summary>
    /// 将状态码转为可读消息。对应 JS <c>xe.getPrintableMessage(t)</c>。
    /// </summary>
    public static string GetMessage(byte statusCode) => statusCode switch
    {
        0 => "OK",
        1 => "正在打印",
        2 => "正在转动马达",
        10 => "没有打印任务",
        11 => "页面数据还没有接收完全",
        12 => "当前打印任务被取消",
        20 => "环境未就绪",
        30 => "打印电压太低了",
        31 => "打印电压太高了",
        32 => "没有检测到打印头",
        33 => "打印头温度太高了",
        34 => "打印机盖子打开了",
        35 => "未检测到纸张",
        36 => "碳带盒未锁紧",
        37 => "未检测到碳带",
        38 => "不匹配的碳带",
        39 => "环境温度过低",
        40 => "用完的碳带",
        41 => "用完的色带",
        42 => "未检测到标签",
        43 => "不匹配的标签",
        44 => "用完的标签",
        45 => "未检测到碳带",
        46 => "不匹配的碳带",
        50 => "标签盒未锁紧",
        _ => $"未知异常: {statusCode}",
    };

    /// <summary>状态码是否表示"可打印"。对应 JS <c>DZIP_PRINTABLE</c>。</summary>
    public static bool IsPrintable(byte statusCode) => statusCode == 0;

    /// <summary>状态码是否为"可继续打印"的非致命状态（可打印 / 正在打印 / 页面未就绪）。</summary>
    public static bool IsContinuePrintable(byte statusCode) =>
        statusCode == 0 || statusCode == 1 || statusCode == 11;
}

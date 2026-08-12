using DzPrinter.Core;
using DzPrinter.Transport;

namespace DzPrinter.Printer;

// =====================================================================
//  LPAPIFactory（LPAPI 工厂）。对应 JS SDK 中 <c>LPAPIFactory</c> 类。
//  JS 中 <c>LPAPIFactory</c> 维护全局单例 <c>api</c>，提供：
//    - getInstance(options)：获取单例（首次创建，后续更新配置）
//    - initApi(options)：创建并打开适配器
//    - resetApi(options)：重置适配器后重新获取实例
//    - createInstance(options)：实际创建 LPAPI 实例
//
//  C# 实现策略：
//   - 保持单例语义（线程安全）
//   - 异步初始化：InitApiAsync 返回 Task<LPAPI>
//   - 传输层工厂由外部注入（对应 JS 中 BleAdapter 的创建）
// =====================================================================

/// <summary>
/// LPAPI 工厂。对应 JS SDK 中的 <c>LPAPIFactory</c> 类。
/// 维护 <see cref="LPAPI"/> 单例并提供创建/重置接口。
/// </summary>
/// <remarks>
/// <para><b>JS 对照</b>：JS <c>LPAPIFactory</c> 通过 <c>getInstance()</c> 返回单例，
/// 首次调用时通过 <c>createInstance(options)</c> 创建，后续调用更新配置。</para>
/// <para><b>线程安全</b>：C# 使用锁保护单例创建。</para>
/// <para><b>传输层注入</b>：JS 中 <c>BleAdapter</c> 是硬编码的蓝牙适配器；
/// C# 中通过 <see cref="TransportFactory"/> 属性注入传输层工厂，支持 BLE/HID/模拟等。</para>
/// </remarks>
public static class LPAPIFactory
{
    private static readonly object _syncRoot = new();
    private static LPAPI? _api;
    private static Func<LpaDeviceType, IDeviceTransport>? _transportFactory;

    /// <summary>
    /// 传输层工厂。必须在 <see cref="GetInstance"/> 前设置。
    /// 对应 JS 中 <c>new BleAdapter(options)</c> 的适配器创建。
    /// </summary>
    /// <remarks>
    /// 工厂委托接收 <see cref="LpaDeviceType"/> 参数，返回对应的 <see cref="IDeviceTransport"/> 实例。
    /// 宿主应用应根据目标平台提供具体实现：
    /// <list type="bullet">
    ///   <item>Windows：WinRT BLE（BluetoothLEDevice）/ HidSharp</item>
    ///   <item>macOS：CoreBluetooth / IOKit HID</item>
    ///   <item>Linux：BlueZ / hidraw</item>
    ///   <item>测试：MockTransport</item>
    /// </list>
    /// </remarks>
    public static Func<LpaDeviceType, IDeviceTransport>? TransportFactory
    {
        get => _transportFactory;
        set
        {
            lock (_syncRoot)
            {
                _transportFactory = value;
                // 切换工厂时清除已有实例
                _api?.Dispose();
                _api = null;
            }
        }
    }

    /// <summary>
    /// 当前单例。对应 JS <c>LPAPIFactory.api</c>。
    /// </summary>
    public static LPAPI? Api
    {
        get { lock (_syncRoot) { return _api; } }
    }

    /// <summary>
    /// 获取 LPAPI 单例。对应 JS <c>LPAPIFactory.getInstance(options)</c>。
    /// 首次调用创建实例；后续调用返回已有实例（忽略 options）。
    /// </summary>
    /// <param name="printerInfo">打印参数（仅首次创建时生效）。</param>
    /// <returns>LPAPI 单例。</returns>
    /// <exception cref="InvalidOperationException">未设置 <see cref="TransportFactory"/>。</exception>
    public static LPAPI GetInstance(PrinterInfo? printerInfo = null)
    {
        lock (_syncRoot)
        {
            if (_api != null) return _api;
            if (_transportFactory == null)
                throw new InvalidOperationException(
                    "必须先设置 LPAPIFactory.TransportFactory，再调用 GetInstance。");

            DzLogger.Current.Info("【LPAPIFactory】GetInstance() —— 创建新实例");
            _api = new LPAPI(_transportFactory, printerInfo);
            return _api;
        }
    }

    /// <summary>
    /// 初始化 LPAPI 并打开适配器。对应 JS <c>LPAPIFactory.initApi(options)</c>。
    /// </summary>
    /// <param name="printerInfo">打印参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已初始化的 LPAPI 实例。</returns>
    public static async Task<LPAPI> InitApiAsync(PrinterInfo? printerInfo = null,
        CancellationToken cancellationToken = default)
    {
        var api = GetInstance(printerInfo);
        DzLogger.Current.Info("【LPAPIFactory】InitApiAsync() —— 适配器已就绪");
        // JS 中此处调用 api.openAdapter({force:true})；
        // C# 中传输层由具体实现管理适配器生命周期，无需显式打开。
        return await Task.FromResult(api).ConfigureAwait(false);
    }

    /// <summary>
    /// 重置适配器并重新获取实例。对应 JS <c>LPAPIFactory.resetApi(options)</c>。
    /// 断开所有连接、释放旧实例，然后创建新实例。
    /// </summary>
    /// <param name="printerInfo">打印参数。</param>
    /// <returns>新的 LPAPI 实例。</returns>
    public static LPAPI ResetApi(PrinterInfo? printerInfo = null)
    {
        lock (_syncRoot)
        {
            _api?.Dispose();
            _api = null;
            return GetInstance(printerInfo);
        }
    }

    /// <summary>
    /// 销毁单例。对应 JS <c>LPAPIFactory.quitApi()</c>（注释中的方法）。
    /// </summary>
    public static void QuitApi()
    {
        lock (_syncRoot)
        {
            _api?.Dispose();
            _api = null;
        }
    }
}

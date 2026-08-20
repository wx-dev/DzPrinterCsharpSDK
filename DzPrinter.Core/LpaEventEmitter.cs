namespace DzPrinter.Core;

/// <summary>
/// 简单的事件发射器。对应 JS SDK 中的 <c>LPAEmitter</c>（minified: <c>Ne</c>）类。
/// 提供按事件名称注册/移除/触发处理器的功能。
/// </summary>
public sealed class LpaEventEmitter
{
    /// <summary>事件名称到处理器列表的映射。</summary>
    private readonly Dictionary<string, List<Action<object?>>> _listeners = new();
    private readonly object _lock = new();

    /// <summary>
    /// 注册事件处理器。对应 JS <c>on(t, e)</c>。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="handler">事件处理器。</param>
    public void On(string eventName, Action<object?> handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (!_listeners.TryGetValue(eventName, out var list))
            {
                list = new List<Action<object?>>();
                _listeners[eventName] = list;
            }
            list.Add(handler);
        }
    }

    /// <summary>
    /// 移除事件处理器。对应 JS <c>off(t, e)</c>。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="handler">要移除的事件处理器。</param>
    public void Off(string eventName, Action<object?> handler)
    {
        if (handler == null) return;
        lock (_lock)
        {
            if (_listeners.TryGetValue(eventName, out var list))
            {
                list.Remove(handler);
            }
        }
    }

    /// <summary>
    /// 触发事件，通知所有已注册的处理器。对应 JS <c>emit</c>。
    /// 触发前会复制一份处理器列表，避免回调中增删导致迭代异常。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="args">传递给处理器的参数。</param>
    public void Emit(string eventName, object? args)
    {
        Action<object?>[] snapshot;
        lock (_lock)
        {
            if (!_listeners.TryGetValue(eventName, out var list) || list.Count == 0) return;
            snapshot = list.ToArray();
        }
        foreach (var handler in snapshot)
        {
            handler(args);
        }
    }
}

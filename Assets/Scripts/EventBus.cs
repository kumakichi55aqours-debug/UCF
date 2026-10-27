using System;
using System.Collections.Generic;
using System.Linq;

public static class EventBus
{
    // イベント型ごとに購読リストを保持
    private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();

    // 購読登録
    public static void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_subscribers.ContainsKey(type))
            _subscribers[type] = new List<Delegate>();
        _subscribers[type].Add(handler);
    }

    // 購読解除（必要に応じて）
    public static void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (_subscribers.TryGetValue(type, out var handlers))
        {
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _subscribers.Remove(type);
        }
    }

    // イベント発行
    public static void Publish<T>(T eventData)
    {
        var type = typeof(T);
        if (!_subscribers.TryGetValue(type, out var handlers)) return;

        // リスト内のすべてを呼び出し
        foreach (var handler in handlers.OfType<Action<T>>())
        {
            handler?.Invoke(eventData);
        }
    }
}

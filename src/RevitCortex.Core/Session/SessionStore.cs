using System.Collections.Concurrent;

namespace RevitCortex.Core.Session;

public class SessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, object> _store = new();

    public T? Get<T>(string key) where T : class
    {
        return _store.TryGetValue(key, out var value) ? value as T : null;
    }

    public void Set<T>(string key, T value) where T : class
    {
        _store[key] = value;
    }

    public void Remove(string key)
    {
        _store.TryRemove(key, out _);
    }

    public void Clear()
    {
        _store.Clear();
    }

    public bool ContainsKey(string key)
    {
        return _store.ContainsKey(key);
    }
}

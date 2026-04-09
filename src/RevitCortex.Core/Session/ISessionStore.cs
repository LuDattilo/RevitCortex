namespace RevitCortex.Core.Session;

public interface ISessionStore
{
    T? Get<T>(string key) where T : class;
    void Set<T>(string key, T value) where T : class;
    void Remove(string key);
    void Clear();
    bool ContainsKey(string key);
}

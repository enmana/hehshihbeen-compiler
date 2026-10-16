using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace hehshihbeen_compiler.Engine3D.Resources
{
    public class ResourceManager
    {
        private readonly ConcurrentDictionary<string, object> _cache = new();

        public Task<T?> LoadAsync<T>(string id, System.Func<Task<T>> loader) where T : class
        {
            if (_cache.TryGetValue(id, out var existing)) return Task.FromResult(existing as T);
            return InternalLoad(id, loader);
        }

        private async Task<T?> InternalLoad<T>(string id, System.Func<Task<T>> loader) where T : class
        {
            var res = await loader();
            _cache[id] = res!;
            return res;
        }
    }
}

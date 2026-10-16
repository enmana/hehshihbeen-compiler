using Engine.Scene;
using System.Collections.Concurrent;

namespace Engine.Content
{
    public class ResourceHandle<T> where T : class
    {
        public T Resource { get; }
        public ResourceHandle(T resource) { Resource = resource; }
    }

    public class ResourceManager
    {
        private readonly ConcurrentDictionary<string, object> _cache = new();

        public Task<ResourceHandle<Mesh>> LoadMeshAsync(string path)
        {
            if (_cache.TryGetValue(path, out var obj) && obj is ResourceHandle<Mesh> h)
                return Task.FromResult(h);
            // synchronous load via ObjLoader
            var mesh = ObjLoader.Load(path);
            var handle = new ResourceHandle<Mesh>(mesh);
            _cache[path] = handle;
            return Task.FromResult(handle);
        }

        public ResourceHandle<T>? Get<T>(string id) where T : class
        {
            if (_cache.TryGetValue(id, out var obj) && obj is ResourceHandle<T> h) return h;
            return null;
        }

        // placeholder: upload mesh to GPU (no-op)
        public Engine.Renderer.GpuMesh UploadToGpu(Mesh mesh) => new Engine.Renderer.GpuMesh();
    }
}

using System.Threading.Tasks;

namespace Engine.Core.Renderer
{
    public interface IRenderBackend : System.IDisposable
    {
        Task InitializeAsync();
        // Avoid referencing Engine.Scene from Engine.Core; accept opaque scene object
        void Render(object? scene, Engine.Core.Camera.Camera? camera);
    }
}

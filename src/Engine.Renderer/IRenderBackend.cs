using Engine.Scene;

namespace Engine.Renderer
{
    public interface IRenderBackend
    {
        void Initialize();
        void Shutdown();
        void Render(SceneGraph scene, Camera camera);
    }

    public class Camera
    {
        public System.Numerics.Matrix4x4 View;
        public System.Numerics.Matrix4x4 Projection;
    }

    public class SceneGraph
    {
        public Node Root { get; } = new Node("Root");
    }
}

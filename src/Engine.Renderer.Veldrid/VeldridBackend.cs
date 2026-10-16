using Engine.Renderer;

namespace Engine.Renderer.Veldrid
{
    public class VeldridBackend : IRenderBackend
    {
        public void Initialize()
        {
            // placeholder: real Veldrid init goes here
        }

        public void Shutdown()
        {
        }

        public void Render(SceneGraph scene, Camera camera)
        {
            // no-op placeholder
        }
    }
}

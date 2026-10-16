using Engine.Scene;
using Engine.Core;

namespace Engine.Scene.Components
{
    public class MeshRenderer
    {
        public Engine.Scene.Mesh? Mesh { get; set; }
        public Engine.Core.Material? Material { get; set; }
        public bool CastShadows { get; set; } = true;

        public MeshRenderer(Engine.Scene.Mesh? mesh = null, Engine.Core.Material? material = null)
        {
            Mesh = mesh; Material = material;
        }
    }
}

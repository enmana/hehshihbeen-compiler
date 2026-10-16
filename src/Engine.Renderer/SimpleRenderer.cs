using System.Numerics;

namespace Engine.Renderer
{
    // Simple forward renderer that builds draw list; backend is responsible for actual GPU submission
    public class SimpleRenderer
    {
        public DrawList BuildDrawList(SceneGraph scene)
        {
            var list = new DrawList();
            void walk(Engine.Scene.Node n, Matrix4x4 parent)
            {
                var world = n.LocalTransform.ToMatrix();
                if (parent != Matrix4x4.Identity) world = world * parent;
                foreach (var child in n.Children) walk(child, world);
                // if node has MeshRenderer component (very small simulation): check via dynamic field (placeholder)
                // Real ECS/component lookup omitted; we assume Node has a MeshRenderer attached via a convention (not implemented)
            }
            walk(scene.Root, Matrix4x4.Identity);
            return list;
        }
    }
}

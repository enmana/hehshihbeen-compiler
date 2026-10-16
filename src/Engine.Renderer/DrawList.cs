using Engine.Core;
using System.Numerics;

namespace Engine.Renderer
{
    public record struct DrawCommand(Engine.Scene.Mesh Mesh, Matrix4x4 World, Material? Material);

    public class DrawList
    {
        private readonly List<DrawCommand> _cmds = new();
        public IReadOnlyList<DrawCommand> Commands => _cmds;
        public void Add(DrawCommand cmd) => _cmds.Add(cmd);
        public int Count => _cmds.Count;
    }
}

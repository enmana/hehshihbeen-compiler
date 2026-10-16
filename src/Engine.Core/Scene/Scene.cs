using System.Collections.Generic;
using Engine.Core.Scene.Components;
using Engine.Scene;

namespace Engine.Scene
{
    public class Scene
    {
        public Node Root { get; } = new Node();
        public List<MeshRenderer> Renderers { get; } = new List<MeshRenderer>();

        public Node CreateNode(string name = "") { var n = new Node(); Root.AddChild(n); n.Name = name; return n; }
    }
}

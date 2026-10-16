using System.Collections.Generic;
using Engine.Core.Maths;
using System.Numerics;

namespace Engine.Scene
{
    public class Node
    {
        public string Name { get; set; }
        public Transform LocalTransform { get; set; } = Transform.Identity;
        public Node? Parent { get; private set; }
        private readonly List<Node> _children = new List<Node>();
        public IReadOnlyList<Node> Children => _children;

        public Node(string name = "") { Name = name; }

        public void AddChild(Node child)
        {
            if (child.Parent != null) child.Parent.RemoveChild(child);
            child.Parent = this;
            _children.Add(child);
        }

        public void RemoveChild(Node child)
        {
            if (_children.Remove(child)) child.Parent = null;
        }

        public Matrix4x4 GetWorldMatrix()
        {
            var local = LocalTransform.ToMatrix();
            if (Parent == null) return local;
            return local * Parent.GetWorldMatrix();
        }
    }
}

using System.Collections.Generic;
using System.Numerics;

namespace hehshihbeen_compiler.Engine3D.Scene
{
    public class Node
    {
        public string Name { get; set; } = string.Empty;
        public Transform Transform { get; } = new Transform();
        public Node? Parent { get; private set; }
        public List<Node> Children { get; } = new List<Node>();


        public void AddChild(Node child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (Parent == null) return Transform.LocalMatrix;
                return Transform.LocalMatrix * Parent.WorldMatrix;
            }
        }
    }
}

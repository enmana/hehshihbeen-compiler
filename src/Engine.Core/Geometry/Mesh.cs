using System.Numerics;

namespace hehshihbeen_compiler.Engine3D.Geometry
{
    public class Mesh : System.IDisposable
    {
        public Vector3[] Positions { get; set; } = System.Array.Empty<Vector3>();
        public Vector3[] Normals { get; set; } = System.Array.Empty<Vector3>();
        public Vector2[] UVs { get; set; } = System.Array.Empty<Vector2>();
        public int[] Indices { get; set; } = System.Array.Empty<int>();

        public void Dispose() { /* placeholder for GPU resource cleanup */ }
    }
}
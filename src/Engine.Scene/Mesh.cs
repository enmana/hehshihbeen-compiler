using System.Numerics;

namespace Engine.Scene
{
    public class Mesh
    {
        public Vector3[] Positions { get; set; } = System.Array.Empty<Vector3>();
        public Vector3[] Normals { get; set; } = System.Array.Empty<Vector3>();
        public Vector2[] UVs { get; set; } = System.Array.Empty<Vector2>();
        public int[] Indices { get; set; } = System.Array.Empty<int>();

        public BoundingBox GetBounds()
        {
            if (Positions == null || Positions.Length == 0) return new BoundingBox();
            var min = Positions[0];
            var max = Positions[0];
            foreach (var p in Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return new BoundingBox(min, max);
        }
    }

    public struct BoundingBox
    {
        public Vector3 Min;
        public Vector3 Max;
        public BoundingBox(Vector3 min, Vector3 max) { Min = min; Max = max; }
    }
}

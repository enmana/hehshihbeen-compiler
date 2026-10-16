using System.Numerics;

namespace Engine.Core
{
    public class Material
    {
        public Vector3 Albedo { get; set; } = new Vector3(1f, 1f, 1f);
        public float Metallic { get; set; } = 0f;
        public float Roughness { get; set; } = 1f;
        public string? AlbedoTexturePath { get; set; }
    }
}

using System.Numerics;

namespace Engine.Core.Camera
{
    public class Camera
    {
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Target { get; set; } = Vector3.UnitZ;
        public float Near { get; set; } = 0.1f;
        public float Far { get; set; } = 1000f;
        public float FovDegrees { get; set; } = 60f;

        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
        public Matrix4x4 ProjectionMatrix(float aspect)
        {
            float fov = MathF.PI * FovDegrees / 180f;
            return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, Near, Far);
        }
    }
}

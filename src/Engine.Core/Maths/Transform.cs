using System.Numerics;

namespace Engine.Core.Maths
{
    public struct Transform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static Transform Identity => new Transform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = new Vector3(1,1,1) };

        public Matrix4x4 ToMatrix()
        {
            var scale = Matrix4x4.CreateScale(Scale);
            var rot = Matrix4x4.CreateFromQuaternion(Rotation);
            var trans = Matrix4x4.CreateTranslation(Position);
            return scale * rot * trans;
        }

        public static Transform FromMatrix(Matrix4x4 m)
        {
            // Simplified extraction: assumes no shear
            Vector3 scale = new Vector3(
                new Vector3(m.M11, m.M12, m.M13).Length(),
                new Vector3(m.M21, m.M22, m.M23).Length(),
                new Vector3(m.M31, m.M32, m.M33).Length());
            var pos = new Vector3(m.M41, m.M42, m.M43);
            // rotation extraction omitted for brevity
            return new Transform { Position = pos, Rotation = Quaternion.Identity, Scale = scale };
        }
    }
}

using System.Numerics;

namespace hehshihbeen_compiler.Engine3D.Runtime.Math
{
    public static class VectorExtensions
    {
        public static Vector3 Transform(this Vector3 v, Matrix4x4 m)
        {
            var x = v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41;
            var y = v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42;
            var z = v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43;
            return new Vector3(x, y, z);
        }
    }
}
using Engine.Scene;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Engine.Content
{
    public static class ObjLoader
    {
        // OBJ loader: supports v, vn, vt, negative indices and generates normals if missing
        public static Mesh LoadFromText(string text)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3?>();
            var uvs = new List<Vector2?>();
            var indices = new List<int>();

            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var raw in lines)
            {
                var s = raw.Trim();
                if (s.Length == 0 || s.StartsWith("#")) continue;
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                if (parts[0] == "v" && parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    {
                        positions.Add(new Vector3(x, y, z));
                        normals.Add(null);
                        uvs.Add(null);
                    }
                }
                else if (parts[0] == "vn" && parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    {
                        normals.Add(Vector3.Normalize(new Vector3(x, y, z)));
                    }
                }
                else if (parts[0] == "vt" && parts.Length >= 3)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var u)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        uvs.Add(new Vector2(u, v));
                    }
                }
                else if (parts[0] == "f" && parts.Length >= 4)
                {
                    var faceVerts = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var tok = parts[i];
                        var sub = tok.Split('/');
                        if (sub.Length >= 1
                            && int.TryParse(sub[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvi))
                        {
                            var vi = ResolveIndex(tvi, positions.Count);
                            if (vi >= 0) faceVerts.Add(vi);   // -1 == dangling reference, drop the corner
                        }
                        // ignore vt/vn indices for this simple packer; we only produce a single index stream
                    }
                    // fan triangulate
                    for (int i = 1; i + 1 < faceVerts.Count; i++)
                    {
                        indices.Add(faceVerts[0]); indices.Add(faceVerts[i]); indices.Add(faceVerts[i+1]);
                    }
                }
            }

            // ensure normals computed
            var posArray = positions.ToArray();
            var normArray = new Vector3[posArray.Length];
            for (int i = 0; i < posArray.Length; i++) normArray[i] = Vector3.Zero;
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = posArray[indices[i]];
                var b = posArray[indices[i+1]];
                var c = posArray[indices[i+2]];
                var n = Vector3.Cross(b - a, c - a);
                normArray[indices[i]] += n;
                normArray[indices[i+1]] += n;
                normArray[indices[i+2]] += n;
            }
            for (int i = 0; i < normArray.Length; i++) normArray[i] = Vector3.Normalize(normArray[i]);

            var mesh = new Mesh();
            mesh.Positions = posArray;
            mesh.Normals = normArray;
            mesh.UVs = System.Array.Empty<Vector2>();
            mesh.Indices = indices.ToArray();
            return mesh;
        }

        // OBJ vertex indices are 1-based; negative values count back from the end of the list
        // (-1 == last vertex). Returns a 0-based index, or -1 for 0 / out-of-range references.
        private static int ResolveIndex(int objIndex, int vertexCount)
        {
            if (objIndex > 0) return objIndex <= vertexCount ? objIndex - 1 : -1;
            if (objIndex < 0)
            {
                var i = vertexCount + objIndex;
                return i >= 0 ? i : -1;
            }
            return -1;   // OBJ has no vertex 0
        }

        public static Mesh Load(string path)
        {
            var text = File.ReadAllText(path);
            return LoadFromText(text);
        }
    }
}

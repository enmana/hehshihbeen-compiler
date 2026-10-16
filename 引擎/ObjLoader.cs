using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace hehshihbeen_compiler
{
    // Lightweight OBJ loader used by 引擎宿主能力C to parse OBJ text.
    // Kept simple and internal to 引擎 for compilation inclusion.
    public class Mesh
    {
        public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
        public Vector3[] Normals { get; set; } = Array.Empty<Vector3>();
        public Vector2[] UVs { get; set; } = Array.Empty<Vector2>();
        public int[] Indices { get; set; } = Array.Empty<int>();
    }

    public static class ObjLoader
    {
        public static Mesh Load(string path)
        {
            var lines = File.ReadAllLines(path);
            return LoadFromLines(lines, normalize: true);
        }

        public static Mesh LoadFromText(string text, bool normalize = true)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            return LoadFromLines(lines, normalize);
        }

        private static Mesh LoadFromLines(string[] lines, bool normalize)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var vns = new List<Vector3>();

            var indexMap = new Dictionary<string, int>(); // "v/vt/vn" -> index
            var positionsOut = new List<Vector3>();
            var normalsOut = new List<Vector3>();
            var uvsOut = new List<Vector2>();
            var indices = new List<int>();

            foreach (var raw in lines)
            {
                if (raw == null) continue;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        verts.Add(new Vector3(x, y, z));
                    }
                    continue;
                }
                if (line.StartsWith("vt ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float u)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    {
                        uvs.Add(new Vector2(u, v));
                    }
                    continue;
                }
                if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float nx)
                        && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ny)
                        && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float nz))
                    {
                        vns.Add(Vector3.Normalize(new Vector3(nx, ny, nz)));
                    }
                    continue;
                }
                if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue; // need at least 3 verts
                    var faceTokens = parts.Skip(1).ToArray();

                    var faceIndices = new List<int>();
                    foreach (var tok in faceTokens)
                    {
                        var comps = tok.Split('/');
                        int vi = ParseIndex(comps, 0, verts.Count);
                        int vti = comps.Length > 1 && comps[1] != "" ? ParseIndex(comps, 1, uvs.Count) : -1;
                        int vni = comps.Length > 2 && comps[2] != "" ? ParseIndex(comps, 2, vns.Count) : -1;

                        if (vi < 0 || vi >= verts.Count) continue; // skip malformed
                        string key = $"{vi}/{vti}/{vni}";
                        if (!indexMap.TryGetValue(key, out int outIndex))
                        {
                            outIndex = positionsOut.Count;
                            indexMap[key] = outIndex;
                            positionsOut.Add(verts[vi]);
                            uvsOut.Add(vti >= 0 ? uvs[vti] : new Vector2());
                            normalsOut.Add(vni >= 0 ? vns[vni] : Vector3.Zero);
                        }
                        faceIndices.Add(outIndex);
                    }

                    for (int i = 1; i < faceIndices.Count - 1; i++)
                    {
                        indices.Add(faceIndices[0]);
                        indices.Add(faceIndices[i]);
                        indices.Add(faceIndices[i + 1]);
                    }
                }
            }

            bool needGenNormals = normalsOut.All(n => n == Vector3.Zero);
            if (needGenNormals)
            {
                for (int i = 0; i < normalsOut.Count; i++) normalsOut[i] = Vector3.Zero;
                for (int i = 0; i < indices.Count; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    var p0 = positionsOut[a];
                    var p1 = positionsOut[b];
                    var p2 = positionsOut[c];
                    var edge1 = p1 - p0;
                    var edge2 = p2 - p0;
                    var n = Vector3.Cross(edge1, edge2);
                    if (n.LengthSquared() > 1e-9f)
                    {
                        var nn = Vector3.Normalize(n);
                        normalsOut[a] += nn;
                        normalsOut[b] += nn;
                        normalsOut[c] += nn;
                    }
                }
                for (int i = 0; i < normalsOut.Count; i++)
                {
                    var nn = normalsOut[i];
                    if (nn.LengthSquared() > 1e-9f) normalsOut[i] = Vector3.Normalize(nn);
                    else normalsOut[i] = new Vector3(0, 1, 0);
                }
            }

            if (normalize && positionsOut.Count > 0)
            {
                float minx = positionsOut[0].X, maxx = positionsOut[0].X;
                float miny = positionsOut[0].Y, maxy = positionsOut[0].Y;
                float minz = positionsOut[0].Z, maxz = positionsOut[0].Z;
                foreach (var p in positionsOut)
                {
                    if (p.X < minx) minx = p.X; else if (p.X > maxx) maxx = p.X;
                    if (p.Y < miny) miny = p.Y; else if (p.Y > maxy) maxy = p.Y;
                    if (p.Z < minz) minz = p.Z; else if (p.Z > maxz) maxz = p.Z;
                }
                float wx = maxx - minx, wy = maxy - miny, wz = maxz - minz;
                float longest = MathF.Max(wx, MathF.Max(wy, wz));
                float scale = longest > 1e-9f ? 2.0f / longest : 1.0f;
                float cx = (minx + maxx) * 0.5f, cy = (miny + maxy) * 0.5f, cz = (minz + maxz) * 0.5f;
                for (int i = 0; i < positionsOut.Count; i++)
                {
                    var p = positionsOut[i];
                    positionsOut[i] = new Vector3((p.X - cx) * scale, (p.Y - cy) * scale, (p.Z - cz) * scale);
                }
            }

            return new Mesh
            {
                Positions = positionsOut.ToArray(),
                Normals = normalsOut.ToArray(),
                UVs = uvsOut.ToArray(),
                Indices = indices.ToArray()
            };
        }

        static int ParseIndex(string[] comps, int pos, int count)
        {
            if (pos >= comps.Length) return -1;
            var s = comps[pos];
            if (string.IsNullOrEmpty(s)) return -1;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                return -1;
            if (idx > 0) return idx - 1;
            return count + idx; // negative index
        }
    }
}

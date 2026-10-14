using System.Collections.Generic;
using UnityEngine;

namespace VMS.Rendering
{
    /// <summary>Accumulates vertices for procedural meshes (track chunks, props, vessels, FX batches).</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(1024);
        public readonly List<Color32> Colors = new List<Color32>(1024);
        public readonly List<Vector2> Uv = new List<Vector2>(1024);
        public readonly List<Vector2> Uv2 = new List<Vector2>(1024);
        readonly List<int>[] _triangles;

        public MeshBuilder(int submeshes = 1)
        {
            _triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>(1536);
        }

        public int VertexCount => Vertices.Count;

        public void Clear()
        {
            Vertices.Clear();
            Colors.Clear();
            Uv.Clear();
            Uv2.Clear();
            foreach (var t in _triangles) t.Clear();
        }

        public int AddVertex(Vector3 position, Color32 color, Vector2 uv, Vector2 uv2)
        {
            Vertices.Add(position);
            Colors.Add(color);
            Uv.Add(uv);
            Uv2.Add(uv2);
            return Vertices.Count - 1;
        }

        public int AddVertex(Vector3 position, Color32 color, Vector2 uv) => AddVertex(position, color, uv, Vector2.zero);

        public void AddTriangle(int a, int b, int c, int submesh = 0)
        {
            var t = _triangles[submesh];
            t.Add(a);
            t.Add(b);
            t.Add(c);
        }

        /// <summary>Quad a-b-c-d in clockwise order as seen from the visible side.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color, int submesh = 0)
        {
            AddQuad(a, b, c, d, color, color, submesh);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 bottom, Color32 top, int submesh = 0)
        {
            int i = AddVertex(a, bottom, new Vector2(0, 0));
            AddVertex(b, top, new Vector2(0, 1));
            AddVertex(c, top, new Vector2(1, 1));
            AddVertex(d, bottom, new Vector2(1, 0));
            AddTriangle(i, i + 1, i + 2, submesh);
            AddTriangle(i, i + 2, i + 3, submesh);
        }

        /// <summary>Axis-aligned box in the space of <paramref name="m"/>, all six faces.</summary>
        public void AddBox(Matrix4x4 m, Vector3 size, Color32 color, int submesh = 0)
        {
            Vector3 h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            Color32 dark = Shade(color, 0.72f), mid = Shade(color, 0.86f);
            AddQuad(p[0], p[2], p[3], p[1], mid, color, submesh); // back (-z)
            AddQuad(p[5], p[7], p[6], p[4], mid, color, submesh); // front (+z)
            AddQuad(p[4], p[6], p[2], p[0], dark, mid, submesh); // left
            AddQuad(p[1], p[3], p[7], p[5], dark, mid, submesh); // right
            AddQuad(p[2], p[6], p[7], p[3], color, color, submesh); // top
            AddQuad(p[4], p[0], p[1], p[5], dark, dark, submesh); // bottom
        }

        /// <summary>Loft between two rectangles (size xy) along local z, for tapered hulls and roofs.</summary>
        public void AddTaper(Matrix4x4 m, Vector2 sizeBack, Vector2 sizeFront, float length, Color32 color, int submesh = 0)
        {
            float z0 = -length * 0.5f, z1 = length * 0.5f;
            var b = Rect(m, sizeBack, z0);
            var f = Rect(m, sizeFront, z1);
            Color32 dark = Shade(color, 0.72f);
            AddQuad(b[0], b[3], b[2], b[1], dark, color, submesh);
            AddQuad(f[1], f[2], f[3], f[0], dark, color, submesh);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Color32 c = i == 2 ? color : i == 0 ? Shade(color, 0.6f) : Shade(color, 0.82f);
                AddQuad(b[i], f[i], f[j], b[j], c, c, submesh);
            }
        }

        static Vector3[] Rect(Matrix4x4 m, Vector2 size, float z)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f;
            return new[]
            {
                m.MultiplyPoint3x4(new Vector3(-x, -y, z)),
                m.MultiplyPoint3x4(new Vector3(x, -y, z)),
                m.MultiplyPoint3x4(new Vector3(x, y, z)),
                m.MultiplyPoint3x4(new Vector3(-x, y, z)),
            };
        }

        /// <summary>Upper hemisphere-ish dome (turtle shells, lantern tops).</summary>
        public void AddDome(Matrix4x4 m, float radius, float height, int segments, Color32 color, int submesh = 0)
        {
            int rings = 4;
            int start = Vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float a = r / (float)rings * Mathf.PI * 0.5f;
                float rr = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * height;
                Color32 c = Shade(color, 0.7f + 0.3f * r / rings);
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments * Mathf.PI * 2f;
                    AddVertex(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(t) * rr, y, Mathf.Sin(t) * rr)), c, new Vector2(s / (float)segments, r / (float)rings));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i0 = start + r * row + s, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                    AddTriangle(i0, i2, i1, submesh);
                    AddTriangle(i1, i2, i3, submesh);
                }
        }

        /// <summary>Open cylinder (no caps) along local y.</summary>
        public void AddCylinder(Matrix4x4 m, float radiusBottom, float radiusTop, float height, int segments, Color32 bottom, Color32 top, int submesh = 0)
        {
            int start = Vertices.Count;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(t), cz = Mathf.Sin(t);
                float shade = 0.75f + 0.25f * Mathf.Max(0f, cx);
                AddVertex(m.MultiplyPoint3x4(new Vector3(cx * radiusBottom, 0f, cz * radiusBottom)), Shade(bottom, shade), new Vector2(s / (float)segments, 0f));
                AddVertex(m.MultiplyPoint3x4(new Vector3(cx * radiusTop, height, cz * radiusTop)), Shade(top, shade), new Vector2(s / (float)segments, 1f));
            }
            for (int s = 0; s < segments; s++)
            {
                int i = start + s * 2;
                AddTriangle(i, i + 1, i + 3, submesh);
                AddTriangle(i, i + 3, i + 2, submesh);
            }
        }

        /// <summary>Camera-facing quad with an atlas cell and rotation (radians) around the view axis.</summary>
        public void AddBillboard(Vector3 center, Vector3 camRight, Vector3 camUp, float size, Color32 color, Rect uvRect, float rotation = 0f, int submesh = 0)
        {
            float half = size * 0.5f;
            float c = Mathf.Cos(rotation) * half, s = Mathf.Sin(rotation) * half;
            Vector3 r = camRight * c + camUp * s;
            Vector3 u = -camRight * s + camUp * c;
            int i = AddVertex(center - r - u, color, new Vector2(uvRect.xMin, uvRect.yMin));
            AddVertex(center - r + u, color, new Vector2(uvRect.xMin, uvRect.yMax));
            AddVertex(center + r + u, color, new Vector2(uvRect.xMax, uvRect.yMax));
            AddVertex(center + r - u, color, new Vector2(uvRect.xMax, uvRect.yMin));
            AddTriangle(i, i + 1, i + 2, submesh);
            AddTriangle(i, i + 2, i + 3, submesh);
        }

        /// <summary>Appends another builder's geometry transformed by <paramref name="m"/>.</summary>
        public void Append(MeshBuilder other, Matrix4x4 m, int fromSubmesh = 0, int toSubmesh = 0)
        {
            int offset = Vertices.Count;
            for (int i = 0; i < other.Vertices.Count; i++)
                AddVertex(m.MultiplyPoint3x4(other.Vertices[i]), other.Colors[i], other.Uv[i], other.Uv2[i]);
            foreach (int idx in other._triangles[fromSubmesh]) _triangles[toSubmesh].Add(idx + offset);
        }

        /// <summary>
        /// Uploads the geometry. Pass <paramref name="fixedBounds"/> for meshes rebuilt every frame to skip the
        /// bounds pass (and keep them from being culled while their contents move).
        /// </summary>
        public void ApplyTo(Mesh mesh, Bounds? fixedBounds = null, bool includeUv2 = true)
        {
            mesh.Clear();
            mesh.indexFormat = Vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Vertices);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uv);
            if (includeUv2) mesh.SetUVs(1, Uv2);
            mesh.subMeshCount = _triangles.Length;
            for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i, false);
            if (fixedBounds.HasValue) mesh.bounds = fixedBounds.Value;
            else mesh.RecalculateBounds();
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            ApplyTo(mesh);
            return mesh;
        }

        public static Color32 Shade(Color32 c, float k) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);
    }
}

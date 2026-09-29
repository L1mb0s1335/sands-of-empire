using System.Collections.Generic;
using UnityEngine;

namespace Runeterra.Map
{
    /// <summary>
    /// Сборщик low-poly меша с цветом в вершинах и плоским затенением
    /// (у каждого треугольника свои вершины и нормаль).
    /// </summary>
    public class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        public int VertexCount => _vertices.Count;

        /// <summary>Преобразование, применяемое ко всем следующим фигурам (для наклонённых деталей).</summary>
        public Matrix4x4 Matrix { get; set; } = Matrix4x4.identity;

        /// <summary>Треугольник, развёрнутый лицевой стороной к facing.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector3 facing)
        {
            if (!Matrix.isIdentity)
            {
                a = Matrix.MultiplyPoint3x4(a);
                b = Matrix.MultiplyPoint3x4(b);
                c = Matrix.MultiplyPoint3x4(c);
                facing = Matrix.MultiplyVector(facing);
            }
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, facing) < 0f)
            {
                (b, c) = (c, b);
                (cb, cc) = (cc, cb);
                n = -n;
            }
            n.Normalize();
            int i = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            _colors.Add(ca); _colors.Add(cb); _colors.Add(cc);
            _normals.Add(n); _normals.Add(n); _normals.Add(n);
            _triangles.Add(i); _triangles.Add(i + 1); _triangles.Add(i + 2);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 facing) =>
            Triangle(a, b, c, color, color, color, facing);

        /// <summary>Четырёхугольник a-b-c-d (по контуру).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd, Vector3 facing)
        {
            Triangle(a, b, c, ca, cb, cc, facing);
            Triangle(a, c, d, ca, cc, cd, facing);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 facing) =>
            Quad(a, b, c, d, color, color, color, color, facing);

        /// <summary>Конус/пирамида. Если topRadius > 0 — усечённый (без крышки).</summary>
        public void Cone(Vector3 baseCenter, float radius, float height, int sides, Color baseColor, Color topColor,
            float topRadius = 0f, float rotation = 0f, Vector3 apexOffset = default)
        {
            var top = baseCenter + Vector3.up * height + apexOffset;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rotation + i * Mathf.PI * 2f / sides;
                float a1 = rotation + (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var b0 = baseCenter + d0 * radius;
                var b1 = baseCenter + d1 * radius;
                var outward = (d0 + d1).normalized + Vector3.up * 0.2f;
                if (topRadius <= 0f)
                {
                    Triangle(b0, b1, top, baseColor, baseColor, topColor, outward);
                }
                else
                {
                    var t0 = top + d0 * topRadius;
                    var t1 = top + d1 * topRadius;
                    Quad(b0, b1, t1, t0, baseColor, baseColor, topColor, topColor, outward);
                }
            }
        }

        /// <summary>Призма (цилиндр с плоским верхом).</summary>
        public void Prism(Vector3 baseCenter, float radius, float height, int sides, Color side, Color top, float rotation = 0f)
        {
            var up = Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rotation + i * Mathf.PI * 2f / sides;
                float a1 = rotation + (i + 1) * Mathf.PI * 2f / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var b0 = baseCenter + d0 * radius;
                var b1 = baseCenter + d1 * radius;
                Quad(b0, b1, b1 + up, b0 + up, side, (d0 + d1).normalized);
                Triangle(baseCenter + up, b0 + up, b1 + up, top, Vector3.up);
            }
        }

        /// <summary>Коробка, повёрнутая вокруг Y.</summary>
        public void Box(Vector3 baseCenter, Vector3 size, float yaw, Color side, Color top)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var hx = rot * new Vector3(size.x * 0.5f, 0f, 0f);
            var hz = rot * new Vector3(0f, 0f, size.z * 0.5f);
            var up = Vector3.up * size.y;
            var c = new[]
            {
                baseCenter - hx - hz, baseCenter + hx - hz, baseCenter + hx + hz, baseCenter - hx + hz,
            };
            for (int i = 0; i < 4; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % 4];
                var mid = (a + b) * 0.5f - baseCenter;
                Quad(a, b, b + up, a + up, side, mid);
            }
            Quad(c[0] + up, c[1] + up, c[2] + up, c[3] + up, top, Vector3.up);
        }

        /// <summary>Полусфера-купол из колец.</summary>
        public void Dome(Vector3 baseCenter, float radius, int sides, int rings, Color bottom, Color top)
        {
            for (int r = 0; r < rings; r++)
            {
                float p0 = r * Mathf.PI * 0.5f / rings, p1 = (r + 1) * Mathf.PI * 0.5f / rings;
                float y0 = Mathf.Sin(p0) * radius, y1 = Mathf.Sin(p1) * radius;
                float r0 = Mathf.Cos(p0) * radius, r1 = Mathf.Cos(p1) * radius;
                var c0 = Color.Lerp(bottom, top, (float)r / rings);
                var c1 = Color.Lerp(bottom, top, (float)(r + 1) / rings);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var v00 = baseCenter + d0 * r0 + Vector3.up * y0;
                    var v10 = baseCenter + d1 * r0 + Vector3.up * y0;
                    var v01 = baseCenter + d0 * r1 + Vector3.up * y1;
                    var v11 = baseCenter + d1 * r1 + Vector3.up * y1;
                    var outward = ((v00 + v11) * 0.5f - baseCenter).normalized;
                    if (r1 < 0.0001f) Triangle(v00, v10, v01, c0, c0, c1, outward);
                    else Quad(v00, v10, v11, v01, c0, c0, c1, c1, outward);
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

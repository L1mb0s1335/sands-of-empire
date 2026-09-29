using System.Collections;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Units
{
    /// <summary>Стрела, летящая по дуге (выстрелы лучников и городов).</summary>
    public static class Projectile
    {
        public static IEnumerator Fly(Vector3 from, Vector3 to, Material material, float hexSize)
        {
            var arrow = new GameObject("Arrow").transform;
            var b = new MeshBuilder();
            var shaft = new Color(0.4f, 0.28f, 0.17f);
            var tip = new Color(0.7f, 0.72f, 0.75f);
            b.Box(Vector3.zero, new Vector3(0.012f, 0.012f, 0.28f) * hexSize, 0f, shaft, shaft);
            b.Cone(new Vector3(0f, 0f, 0.14f) * hexSize, 0.02f * hexSize, 0.001f, 4, tip, tip);
            var mesh = b.ToMesh("Arrow");
            arrow.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            arrow.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;

            float dist = Vector3.Distance(from, to);
            var prev = from;
            for (float t = 0f; t < 1f; t += Time.deltaTime / (0.12f + dist * 0.08f))
            {
                var p = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * dist * 0.25f;
                if ((p - prev).sqrMagnitude > 0.000001f) arrow.rotation = Quaternion.LookRotation(p - prev);
                arrow.position = p;
                prev = p;
                yield return null;
            }
            Object.Destroy(arrow.gameObject);
            Object.Destroy(mesh);
        }
    }
}

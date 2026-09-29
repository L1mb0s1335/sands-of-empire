using UnityEngine;

namespace Runeterra.Cities
{
    /// <summary>Полоска прочности стен над городом (синяя; красная, когда стены пали).</summary>
    public class CityWallsBar : MonoBehaviour
    {
        private City _city;
        private Camera _camera;
        private Transform _fill;
        private Material _fillMaterial;

        public static CityWallsBar Create(City city, Vector3 position, float hexSize, Camera camera, Material baseMaterial)
        {
            var go = new GameObject($"Walls_{city.Data.id}");
            go.transform.position = position;
            var bar = go.AddComponent<CityWallsBar>();
            bar._city = city;
            bar._camera = camera;

            var bg = Quad(go.transform, baseMaterial, new Color(0.08f, 0.08f, 0.1f), 0f);
            bg.localScale = new Vector3(0.8f, 0.09f, 1f) * hexSize;
            bar._fill = new GameObject("Fill").transform;
            bar._fill.SetParent(go.transform, false);
            bar._fill.localPosition = new Vector3(-0.39f * hexSize, 0f, -0.001f);
            var fill = Quad(bar._fill, baseMaterial, Color.white, 0.5f);
            fill.localScale = new Vector3(0.78f, 0.065f, 1f) * hexSize;
            fill.localPosition = new Vector3(0.39f * hexSize, 0f, 0f);
            bar._fillMaterial = fill.GetComponent<Renderer>().sharedMaterial;
            return bar;
        }

        private static Transform Quad(Transform parent, Material baseMaterial, Color color, float emission)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            var mat = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Runeterra/HexTerrain"));
            mat.color = color;
            mat.SetColor("_EmissionColor", color * emission);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return quad.transform;
        }

        /// <summary>Туман войны: показывать ли полоску (город разведан).</summary>
        public static System.Func<City, bool> IsShown;
        private bool _shown = true;

        private void LateUpdate()
        {
            bool shown = IsShown == null || IsShown(_city);
            if (shown != _shown)
            {
                _shown = shown;
                foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
            }
            if (_camera != null) transform.rotation = _camera.transform.rotation;
            float t = (float)_city.Walls / City.MaxWalls;
            _fill.localScale = new Vector3(Mathf.Max(0.001f, t), 1f, 1f);
            var c = _city.Walls > 0 ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.9f, 0.2f, 0.2f);
            _fillMaterial.color = c;
            _fillMaterial.SetColor("_EmissionColor", c * 0.5f);
        }
    }
}

using System.Collections;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Economy
{
    /// <summary>Тележка (по суше) или парусник (по морю) каравана. Плавно едет по пройденным клеткам.</summary>
    public class CaravanView : MonoBehaviour
    {
        private Caravan _caravan;
        private HexMapView _map;
        private int _shownPosition = -1;
        private Coroutine _move;
        private bool _shown = true;

        /// <summary>Туман войны: виден ли караван зрителю (null — видно всё).</summary>
        public static System.Func<Caravan, bool> IsShown;

        private void Update()
        {
            bool shown = IsShown == null || IsShown(_caravan);
            if (shown == _shown) return;
            _shown = shown;
            GetComponent<Renderer>().enabled = shown;
        }

        public static CaravanView Create(Caravan caravan, HexMapView map, Color ownerColor, Material baseMaterial)
        {
            var go = new GameObject($"Caravan_{caravan.Id}");
            go.transform.SetParent(map.transform, true);
            var view = go.AddComponent<CaravanView>();
            view._caravan = caravan;
            view._map = map;
            go.AddComponent<MeshFilter>().sharedMesh = caravan.BySea ? Ship(map.hexSize * 1.8f, ownerColor) : Cart(map.hexSize * 1.8f, ownerColor);
            go.AddComponent<MeshRenderer>().sharedMaterial = baseMaterial != null ? baseMaterial : new Material(Shader.Find("Runeterra/HexTerrain"));
            go.transform.position = Place(map, caravan.From.Coord, caravan.BySea);
            return view;
        }

        private static Vector3 Place(HexMapView map, HexCoord c, bool sea) =>
            map.SurfacePosition(c) + new Vector3(0.25f, sea ? 0f : 0.01f, -0.25f) * map.hexSize;

        /// <summary>Сразу поставить на текущую клетку пути (караван из сохранения).</summary>
        public void Snap()
        {
            transform.position = Place(_map, _caravan.Coord, _caravan.BySea);
            if (_caravan.Position >= 0)
            {
                var ahead = _caravan.Path[System.Math.Min(_caravan.Path.Count - 1, _caravan.Position + 1)];
                var flat = Place(_map, ahead, _caravan.BySea) - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flat);
            }
            _shownPosition = _caravan.Position;
        }

        public void Refresh()
        {
            if (_move != null) StopCoroutine(_move);
            if (!Application.isPlaying) { transform.position = Place(_map, _caravan.Coord, _caravan.BySea); _shownPosition = _caravan.Position; return; }
            _move = StartCoroutine(MoveAlong(_shownPosition, _caravan.Position));
            _shownPosition = _caravan.Position;
        }

        private IEnumerator MoveAlong(int from, int to)
        {
            for (int i = from + 1; i <= to; i++)
            {
                var a = transform.position;
                var b = Place(_map, _caravan.Path[i], _caravan.BySea);
                var flat = b - a;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flat);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f)
                {
                    transform.position = Vector3.Lerp(a, b, t);
                    yield return null;
                }
                transform.position = b;
            }
        }

        public void Finish()
        {
            if (!Application.isPlaying) { DestroyImmediate(gameObject); return; }
            StartCoroutine(Fade());
        }

        private IEnumerator Fade()
        {
            if (_move != null) yield return _move;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                transform.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            Destroy(gameObject);
        }

        private static Mesh Cart(float s, Color owner)
        {
            var b = new MeshBuilder { Matrix = Matrix4x4.Scale(Vector3.one * s) };
            var wood = new Color(0.45f, 0.32f, 0.2f);
            var cloth = new Color(0.9f, 0.86f, 0.75f);
            b.Box(new Vector3(0f, 0.05f, 0f), new Vector3(0.14f, 0.05f, 0.22f), 0f, wood, wood);
            b.Box(new Vector3(0f, 0.1f, -0.01f), new Vector3(0.13f, 0.07f, 0.18f), 0f, cloth, owner);
            foreach (float x in new[] { -0.08f, 0.08f })
            {
                b.Matrix = Matrix4x4.Scale(Vector3.one * s) * Matrix4x4.TRS(new Vector3(x, 0.035f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
                b.Prism(Vector3.zero, 0.035f, 0.015f, 8, wood, wood);
            }
            b.Matrix = Matrix4x4.Scale(Vector3.one * s);
            // Ослик впереди.
            var donkey = new Color(0.5f, 0.45f, 0.4f);
            b.Box(new Vector3(0f, 0.06f, 0.19f), new Vector3(0.05f, 0.05f, 0.1f), 0f, donkey, donkey);
            b.Box(new Vector3(0f, 0.1f, 0.25f), new Vector3(0.035f, 0.035f, 0.05f), 0f, donkey, donkey);
            foreach (float x in new[] { -0.018f, 0.018f })
            foreach (float z in new[] { 0.16f, 0.22f })
                b.Box(new Vector3(x, 0f, z), new Vector3(0.012f, 0.06f, 0.012f), 0f, donkey, donkey);
            return b.ToMesh("Cart");
        }

        private static Mesh Ship(float s, Color owner)
        {
            var b = new MeshBuilder { Matrix = Matrix4x4.Scale(Vector3.one * s) };
            var wood = new Color(0.45f, 0.32f, 0.2f);
            b.Box(new Vector3(0f, -0.02f, 0f), new Vector3(0.14f, 0.07f, 0.34f), 0f, wood, new Color(0.55f, 0.4f, 0.25f));
            b.Cone(new Vector3(0f, -0.02f, 0.17f), 0.07f, 0.001f, 4, wood, wood);
            b.Prism(new Vector3(0f, 0.05f, 0f), 0.01f, 0.3f, 4, wood, wood);
            b.Quad(new Vector3(0f, 0.32f, 0f), new Vector3(0f, 0.32f, 0.001f), new Vector3(0f, 0.1f, 0.14f), new Vector3(0f, 0.1f, -0.1f),
                owner, Vector3.right);
            b.Quad(new Vector3(0f, 0.32f, 0f), new Vector3(0f, 0.1f, -0.1f), new Vector3(0f, 0.1f, 0.14f), new Vector3(0f, 0.32f, 0.001f),
                owner, Vector3.left);
            return b.ToMesh("Ship");
        }
    }
}

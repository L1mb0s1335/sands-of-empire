using System.Linq;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Мини-карта: клетки цветом местности (затемнены вне обзора, чёрные — не разведаны),
    /// территории цветом владельца, города — квадраты, юниты — точки, рамка — куда смотрит камера.
    /// Клик по мини-карте переносит камеру. Карта круглая: всё вне вписанного круга прозрачно.
    /// </summary>
    public class Minimap
    {
        public const int Size = 220;

        private readonly GameState _state;
        private readonly HexMapView _map;
        private readonly int _viewer;
        private Texture2D _texture;
        private float _nextRefresh;
        private float _minX, _maxX, _minZ, _maxZ;

        public Minimap(GameState state, HexMapView map, int viewer)
        {
            _state = state;
            _map = map;
            _viewer = viewer;
            foreach (var t in map.Grid.Tiles)
            {
                var p = HexMetrics.ToWorld(t.Coord, map.hexSize);
                _minX = Mathf.Min(_minX, p.x); _maxX = Mathf.Max(_maxX, p.x);
                _minZ = Mathf.Min(_minZ, p.z); _maxZ = Mathf.Max(_maxZ, p.z);
            }
            // Круглая мини-карта: одинаковый масштаб по осям, вся карта внутри вписанного круга.
            float cx = (_minX + _maxX) / 2f, cz = (_minZ + _maxZ) / 2f, half = 0f;
            foreach (var t in map.Grid.Tiles)
                half = Mathf.Max(half, (new Vector2(HexMetrics.ToWorld(t.Coord, map.hexSize).x - cx, HexMetrics.ToWorld(t.Coord, map.hexSize).z - cz)).magnitude);
            half += map.hexSize * 1.2f;
            _minX = cx - half; _maxX = cx + half; _minZ = cz - half; _maxZ = cz + half;
        }

        private Vector2 ToPixel(Vector3 local) => new Vector2(
            (local.x - _minX) / (_maxX - _minX) * (Size - 1),
            (local.z - _minZ) / (_maxZ - _minZ) * (Size - 1));

        /// <summary>Мировая точка по координатам внутри мини-карты (0..1, y вниз).</summary>
        public Vector3 ToWorld(Vector2 uv) => _map.transform.TransformPoint(new Vector3(
            Mathf.Lerp(_minX, _maxX, uv.x), 0f, Mathf.Lerp(_maxZ, _minZ, uv.y)));

        public Texture2D Texture
        {
            get
            {
                if (_texture == null || Time.unscaledTime >= _nextRefresh) Redraw();
                return _texture;
            }
        }

        private void Redraw()
        {
            _nextRefresh = Time.unscaledTime + 0.5f;
            if (_texture == null) _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            var bg = new Color32(14, 32, 34, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            int r = Mathf.Max(2, Mathf.RoundToInt(Size / (_maxX - _minX) * _map.hexSize * 0.95f));

            foreach (var t in _map.Grid.Tiles)
            {
                bool explored = _state.Vision.IsExplored(_viewer, t.Coord);
                if (!explored) continue;
                bool visible = _state.Vision.IsVisible(_viewer, t.Coord);
                var col = _map.TileColor(t.Coord);
                col.a = 1f;
                var owner = _state.OwnerOfTile(t.Coord);
                if (owner != null) col = Color.Lerp(col, _state.Players[owner.Value].Region.primaryColor, 0.35f);
                if (!visible) col = Color.Lerp(col, Color.black, 0.45f);
                Disk(px, ToPixel(HexMetrics.ToWorld(t.Coord, _map.hexSize)), r, col);
            }
            foreach (var c in _state.Cities.Where(c => _state.Vision.IsExplored(_viewer, c.Coord)))
            {
                var region = _state.Players[c.OwnerIndex].Region;
                Square(px, ToPixel(HexMetrics.ToWorld(c.Coord, _map.hexSize)), r + 1, Color.white);
                Square(px, ToPixel(HexMetrics.ToWorld(c.Coord, _map.hexSize)), r, region.secondaryColor);
            }
            foreach (var p in _state.Players)
            foreach (var u in p.Units.Where(u => u.IsAlive && (p.Index == _viewer || _state.Vision.IsVisible(_viewer, u.Coord))))
                Disk(px, ToPixel(HexMetrics.ToWorld(u.Coord, _map.hexSize)), Mathf.Max(1, r / 2),
                    p.Index == _viewer ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.25f, 0.2f));

            // Рамка обзора камеры: точка, куда смотрит центр экрана.
            var cam = _map.mapCamera;
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                var plane = new Plane(Vector3.up, _map.transform.position);
                if (plane.Raycast(ray, out float d))
                {
                    var c = ToPixel(_map.transform.InverseTransformPoint(ray.GetPoint(d)));
                    Frame(px, c, r * 5, r * 3, Color.white);
                }
            }
            // Вне вписанного круга — прозрачно (мягкий край).
            float rad = Size / 2f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = new Vector2(x + 0.5f - rad, y + 0.5f - rad).magnitude;
                px[y * Size + x].a = (byte)(px[y * Size + x].a * Mathf.Clamp01(rad - d));
            }
            _texture.SetPixels32(px);
            _texture.Apply();
        }

        private static void Put(Color32[] px, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            px[y * Size + x] = c;
        }

        private static void Disk(Color32[] px, Vector2 c, int r, Color col)
        {
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (dx * dx + dy * dy <= r * r) Put(px, (int)c.x + dx, (int)c.y + dy, col);
        }

        private static void Square(Color32[] px, Vector2 c, int r, Color col)
        {
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                Put(px, (int)c.x + dx, (int)c.y + dy, col);
        }

        private static void Frame(Color32[] px, Vector2 c, int w, int h, Color col)
        {
            for (int dx = -w; dx <= w; dx++) { Put(px, (int)c.x + dx, (int)c.y - h, col); Put(px, (int)c.x + dx, (int)c.y + h, col); }
            for (int dy = -h; dy <= h; dy++) { Put(px, (int)c.x - w, (int)c.y + dy, col); Put(px, (int)c.x + w, (int)c.y + dy, col); }
        }
    }
}

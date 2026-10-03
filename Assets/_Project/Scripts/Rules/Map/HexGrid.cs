using System;
using System.Collections.Generic;
using System.Linq;

namespace Runeterra.Map
{
    /// <summary>
    /// Прямоугольная гекс-карта Леванта, Южной Анатолии и Верхней Месопотамии (30.2–46.2° в. д., 30.3–39.6° с. ш.).
    /// Чистый C#, без UnityEngine: география задана берегами, хребтами и реками в градусах,
    /// мелкие детали (извилины берега, леса, оазисы, месторождения) детерминированы по seed.
    /// Radius — полуширина карты в колонках, высота — 0.72 от неё.
    /// </summary>
    public class HexGrid
    {
        public const float LonMin = 30.2f, LonMax = 46.2f, LatMin = 30.3f, LatMax = 39.6f;

        private readonly Dictionary<HexCoord, HexTile> _tiles = new Dictionary<HexCoord, HexTile>();

        /// <summary>Полуширина карты (в колонках гексов).</summary>
        public int Radius { get; }
        /// <summary>Полувысота карты (в рядах гексов).</summary>
        public int HalfHeight { get; }
        public int Seed { get; }
        public IReadOnlyCollection<HexTile> Tiles => _tiles.Values;

        public HexGrid(int radius, int seed)
        {
            Radius = radius;
            HalfHeight = (int)Math.Round(radius * 0.72);
            Seed = seed;
            _noise = new ValueNoise(seed);
            Generate();
        }

        /// <summary>Изменяемые поля клеток (местность, объекты, дороги, ресурсы, города) — снимок для отката карты.</summary>
        public sealed class MutableSnapshot
        {
            internal readonly List<(HexTile tile, TerrainType terrain, TileFeature feature, bool road, string resource, string cityId)> Tiles =
                new List<(HexTile, TerrainType, TileFeature, bool, string, string)>();
        }

        public MutableSnapshot CaptureMutable()
        {
            var snap = new MutableSnapshot();
            foreach (var t in _tiles.Values) snap.Tiles.Add((t, t.Terrain, t.Feature, t.HasRoad, t.Resource, t.CityId));
            return snap;
        }

        /// <summary>Вернуть клетки к снимку (для повторных прогонов партии на той же карте).</summary>
        public void RestoreMutable(MutableSnapshot snap)
        {
            foreach (var (t, terrain, feature, road, resource, cityId) in snap.Tiles)
            {
                t.Terrain = terrain;
                t.Feature = feature;
                t.HasRoad = road;
                t.Resource = resource;
                t.CityId = cityId;
            }
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile) => _tiles.TryGetValue(coord, out tile);

        public HexTile GetTile(HexCoord coord) => _tiles.TryGetValue(coord, out var t) ? t : null;

        public IEnumerable<HexTile> Neighbors(HexCoord coord)
        {
            for (int d = 0; d < 6; d++)
                if (_tiles.TryGetValue(coord.Neighbor(d), out var t))
                    yield return t;
        }

        /// <summary>Клетка у края карты (не все соседи на карте).</summary>
        public bool IsEdge(HexCoord c)
        {
            for (int d = 0; d < 6; d++)
                if (!_tiles.ContainsKey(c.Neighbor(d))) return true;
            return false;
        }

        public IEnumerable<HexTile> InRange(HexCoord center, int range)
        {
            for (int dq = -range; dq <= range; dq++)
            for (int dr = Math.Max(-range, -dq - range); dr <= Math.Min(range, -dq + range); dr++)
                if (_tiles.TryGetValue(center + new HexCoord(dq, dr), out var t))
                    yield return t;
        }

        // ---------- География ----------

        private static int FloorHalf(int r) => r >= 0 ? r / 2 : -((1 - r) / 2);

        /// <summary>Долгота и широта центра клетки.</summary>
        public (float lon, float lat) LonLat(HexCoord c)
        {
            float x = c.q + c.r * 0.5f;
            float lon = LonMin + (x + Radius) / (2f * Radius) * (LonMax - LonMin);
            float lat = LatMax - (c.r + HalfHeight) / (2f * HalfHeight) * (LatMax - LatMin);
            return (lon, lat);
        }

        /// <summary>Ближайшая клетка карты к точке (долгота, широта).</summary>
        public HexCoord FromLonLat(float lon, float lat)
        {
            int r = (int)Math.Round((LatMax - lat) / (LatMax - LatMin) * 2f * HalfHeight - HalfHeight);
            r = Math.Max(-HalfHeight, Math.Min(HalfHeight, r));
            float x = (lon - LonMin) / (LonMax - LonMin) * 2f * Radius - Radius;
            var c = new HexCoord((int)Math.Round(x - r * 0.5f), r);
            if (_tiles.ContainsKey(c)) return c;
            return _tiles.Keys.OrderBy(k => k.DistanceTo(c)).First();
        }

        /// <summary>Биом клетки: из местности, объекта и соседства с морем.</summary>
        public Biome BiomeOf(HexTile t)
        {
            if (t.Terrain.IsWater()) return Biome.Water;
            if (t.Feature == TileFeature.Oasis) return Biome.Oasis;
            if (t.Feature == TileFeature.Forest) return Biome.Forest;
            switch (t.Terrain)
            {
                case TerrainType.Mountains:
                case TerrainType.Hills: return Biome.Mountains;
                case TerrainType.River: return Biome.RiverValley;
                case TerrainType.Marsh: return Biome.Marsh;
            }
            if (Neighbors(t.Coord).Any(n => n.Terrain.IsWater())) return Biome.Coast;
            return t.Terrain == TerrainType.Desert ? Biome.Desert : Biome.Steppe;
        }

        private readonly ValueNoise _noise;

        // Берег Леванта: (широта, долгота береговой линии).
        private static readonly (float lat, float lon)[] LevantCoast =
        {
            (31.15f, 34.20f), (31.5f, 34.45f), (32.0f, 34.75f), (32.5f, 34.90f), (32.9f, 35.07f), (33.3f, 35.20f),
            (33.9f, 35.48f), (34.45f, 35.82f), (35.0f, 35.88f), (35.5f, 35.78f), (36.0f, 35.93f), (36.25f, 35.85f),
            (36.6f, 36.15f), (36.95f, 36.0f), (37.1f, 35.6f),
        };

        // Южный берег Анатолии: (долгота, широта береговой линии).
        private static readonly (float lon, float lat)[] AnatolianCoast =
        {
            (30.2f, 36.40f), (30.7f, 36.85f), (31.3f, 36.80f), (32.0f, 36.50f), (32.8f, 36.10f), (33.6f, 36.15f),
            (34.1f, 36.35f), (34.6f, 36.75f), (35.3f, 36.70f), (35.9f, 36.70f), (36.2f, 36.80f), (36.6f, 37.20f),
        };

        private struct Range
        {
            public (float lon, float lat)[] Line;
            public float HalfWidth;
        }

        private static readonly Range[] Ranges =
        {
            // Тавр.
            new Range { HalfWidth = 0.30f, Line = new[] { (30.2f, 37.2f), (31.0f, 37.35f), (31.8f, 37.2f), (32.6f, 37.0f), (33.5f, 36.9f), (34.3f, 37.1f), (34.9f, 37.35f), (35.6f, 37.55f), (36.3f, 37.75f) } },
            // Антитавр и Юго-Восточный Тавр.
            new Range { HalfWidth = 0.30f, Line = new[] { (36.3f, 37.75f), (37.2f, 37.85f), (38.1f, 37.95f), (39.2f, 38.15f), (40.4f, 38.45f), (41.6f, 38.25f), (42.6f, 37.85f), (43.4f, 37.55f), (44.1f, 37.25f) } },
            // Загрос.
            new Range { HalfWidth = 0.36f, Line = new[] { (44.1f, 37.25f), (44.8f, 36.5f), (45.4f, 35.6f), (45.8f, 34.7f), (46.3f, 33.8f) } },
            // Аманос.
            new Range { HalfWidth = 0.14f, Line = new[] { (36.3f, 36.55f), (36.45f, 37.0f), (36.55f, 37.4f) } },
            // Ливан и Антиливан.
            new Range { HalfWidth = 0.14f, Line = new[] { (35.55f, 33.25f), (35.8f, 33.7f), (36.05f, 34.2f), (36.25f, 34.6f) } },
            new Range { HalfWidth = 0.12f, Line = new[] { (35.85f, 33.3f), (36.15f, 33.7f), (36.45f, 34.1f) } },
            // Ансарийские горы.
            new Range { HalfWidth = 0.12f, Line = new[] { (35.95f, 34.8f), (36.1f, 35.3f), (36.05f, 35.8f) } },
        };

        /// <summary>Перевалы: Киликийские ворота, путь к Атталии, Белен.</summary>
        private static readonly (float lon, float lat)[] Passes = { (34.75f, 37.3f), (30.9f, 37.3f), (36.35f, 36.5f), (38.9f, 38.1f) };

        private static readonly (float lon, float lat)[] Euphrates =
        {
            (39.6f, 39.1f), (39.0f, 38.7f), (38.6f, 38.3f), (38.4f, 37.9f), (38.0f, 37.3f), (38.05f, 36.8f), (38.3f, 36.2f),
            (39.0f, 35.95f), (40.1f, 35.35f), (40.9f, 34.45f), (41.9f, 34.2f), (42.8f, 33.8f), (43.6f, 33.4f), (44.3f, 32.6f),
            (45.0f, 31.8f), (45.8f, 31.2f), (46.3f, 31.0f),
        };

        private static readonly (float lon, float lat)[] Tigris =
        {
            (39.8f, 38.6f), (40.2f, 37.95f), (41.0f, 37.8f), (41.8f, 37.5f), (42.2f, 37.3f), (42.8f, 36.8f), (43.13f, 36.34f),
            (43.4f, 35.6f), (43.7f, 34.6f), (44.1f, 33.9f), (44.4f, 33.33f), (44.9f, 32.9f), (45.8f, 32.5f), (46.3f, 31.9f),
        };

        private static readonly (float lon, float lat)[][] MinorRivers =
        {
            // Иордан.
            new[] { (35.62f, 33.2f), (35.58f, 32.7f), (35.55f, 32.2f), (35.52f, 31.75f) },
            // Оронт.
            new[] { (36.35f, 33.9f), (36.6f, 34.5f), (36.75f, 35.1f), (36.55f, 35.6f), (36.35f, 36.0f), (36.2f, 36.2f), (35.95f, 36.05f) },
            // Хабур.
            new[] { (40.0f, 36.9f), (40.7f, 36.5f), (40.6f, 35.8f), (40.4f, 35.2f) },
        };

        /// <summary>Постоянные оазисы: Пальмира, Иерихон, Азрак, Гута Дамаска, Рутба, Эль-Ула.</summary>
        private static readonly (float lon, float lat)[] FixedOases =
        {
            (38.27f, 34.55f), (35.44f, 31.87f), (36.81f, 31.83f), (36.65f, 33.3f), (40.3f, 33.03f), (38.1f, 30.8f), (34.6f, 30.7f),
        };

        private static readonly (float lon, float lat) DeadSea = (35.5f, 31.5f);
        private static readonly (float lon, float lat) SaltLake = (33.4f, 38.7f);

        private static float Interp((float key, float value)[] table, float key)
        {
            if (key <= table[0].key) return table[0].value;
            for (int i = 1; i < table.Length; i++)
                if (key <= table[i].key)
                {
                    var (k0, v0) = table[i - 1];
                    var (k1, v1) = table[i];
                    return v0 + (v1 - v0) * (key - k0) / (k1 - k0);
                }
            return table[table.Length - 1].value;
        }

        private static readonly (float, float)[] LevantTable = LevantCoast.Select(p => (p.lat, p.lon)).ToArray();
        private static readonly (float, float)[] AnatolianTable = AnatolianCoast.Select(p => (p.lon, p.lat)).ToArray();
        private static readonly (float, float)[] TaurusTable = Ranges[0].Line.Select(p => (p.lon, p.lat)).ToArray();
        private static readonly (float, float)[] AntiTaurusTable = Ranges[1].Line.Select(p => (p.lon, p.lat)).ToArray();

        private static float LevantCoastLon(float lat) => lat < 31.15f || lat > 37.1f ? -999f : Interp(LevantTable, lat);

        private static float AnatolianCoastLat(float lon) => Interp(AnatolianTable, lon);

        /// <summary>Расстояние в «градусах» (долгота сжата на широте ~35°): 1 гекс ≈ 0.24.</summary>
        private static float Dist(float lon1, float lat1, float lon2, float lat2)
        {
            float dx = (lon1 - lon2) * 0.82f, dy = lat1 - lat2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DistToLine((float lon, float lat)[] line, float lon, float lat)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Length; i++)
            {
                float ax = line[i - 1].lon * 0.82f, ay = line[i - 1].lat, bx = line[i].lon * 0.82f, by = line[i].lat;
                float px = lon * 0.82f, py = lat;
                float dx = bx - ax, dy = by - ay;
                float t = Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
                float ex = ax + dx * t - px, ey = ay + dy * t - py;
                best = Math.Min(best, (float)Math.Sqrt(ex * ex + ey * ey));
            }
            return best;
        }

        private bool IsSea(float lon, float lat)
        {
            // Кипр — остров.
            float cy = Sq((lon - 33.3f) / 1.05f) + Sq((lat - 35.05f) / 0.32f);
            float karpas = Sq((lon - 34.2f) / 0.45f) + Sq((lat - 35.45f) / 0.12f);
            if (cy < 1f || karpas < 1f) return false;
            return lat >= 31.15f && lon < LevantCoastLon(lat) && lat < AnatolianCoastLat(lon);
        }

        private static float Sq(float x) => x * x;

        /// <summary>Насколько клетка «внутри» хребта: 1 — ось, 0 — край, меньше 0 — вне.</summary>
        private static float MountainFactor(float lon, float lat)
        {
            float m = -9f;
            foreach (var range in Ranges) m = Math.Max(m, 1f - DistToLine(range.Line, lon, lat) / range.HalfWidth);
            foreach (var pass in Passes)
                if (Dist(lon, lat, pass.lon, pass.lat) < 0.28f) m = Math.Min(m, 0.2f);
            return m;
        }

        private static float TaurusLat(float lon) =>
            lon <= 36.3f ? Interp(TaurusTable, lon) : Interp(AntiTaurusTable, lon);

        private void Generate()
        {
            for (int r = -HalfHeight; r <= HalfHeight; r++)
            {
                int shift = FloorHalf(r);
                for (int q = -Radius - shift; q <= Radius - shift; q++)
                {
                    var coord = new HexCoord(q, r);
                    var (lon, lat) = LonLat(coord);
                    float x = q + r * 0.5f, y = r * 0.866f;
                    float wiggle = (_noise.Fractal(x * 0.35f + 40f, y * 0.35f) - 0.5f) * 0.32f;
                    float moisture = _noise.Fractal(x * 0.2f + 100f, y * 0.2f + 100f);
                    float h = _noise.Fractal(x * 0.3f, y * 0.3f);
                    bool sea = IsSea(lon + wiggle, lat + wiggle * 0.6f);
                    var terrain = sea ? TerrainType.Ocean : Land(lon, lat, h, moisture);
                    _tiles[coord] = new HexTile(coord, terrain, h, moisture);
                }
            }

            DrawRiver(Euphrates, widen: true);
            DrawRiver(Tigris, widen: true);
            foreach (var r in MinorRivers) DrawRiver(r, widen: false);
            PlaceMarshes();
            PlaceFeatures();

            // Мелководье у берега.
            foreach (var tile in _tiles.Values)
            {
                if (tile.Terrain != TerrainType.Ocean) continue;
                if (Neighbors(tile.Coord).Any(n => !n.Terrain.IsWater())) tile.Terrain = TerrainType.Coast;
            }
            PlaceResources();
        }

        /// <summary>Суша по климатическим поясам (без рек и болот).</summary>
        private TerrainType Land(float lon, float lat, float h, float moisture)
        {
            float m = MountainFactor(lon, lat);
            if (m > 0.45f && h > 0.3f) return TerrainType.Mountains;
            if (m > 0.05f || (m > -0.7f && h > 0.62f)) return TerrainType.Hills;

            float coast = LevantCoastLon(lat);
            float taurus = TaurusLat(lon);

            // Синай и Негев.
            if (lat < 31.25f) return lon > 34.3f && lat > 31.1f ? TerrainType.Plains : TerrainType.Desert;
            // Анатолийское плато и Армянское нагорье — степь.
            if (lat > taurus + 0.2f)
            {
                if (lon > 38.8f) return h > 0.55f ? TerrainType.Hills : TerrainType.Plains;
                if (lat > 38.9f && moisture > 0.5f) return TerrainType.Grassland;
                if (Dist(lon, lat, SaltLake.lon, SaltLake.lat) < 0.35f) return TerrainType.Desert;
                return h > 0.7f ? TerrainType.Hills : TerrainType.Plains;
            }
            // Киликия и Памфилия — между Тавром и морем.
            if (lon < 36.3f && lat > 36.3f) return moisture > 0.4f ? TerrainType.Grassland : TerrainType.Plains;
            // Левант: прибрежная равнина, холмы Иудеи, Иорданская впадина.
            if (coast > 0f && lon < coast + 0.4f) return TerrainType.Grassland;
            if (coast > 0f && lon < coast + 1.5f)
            {
                if (lat < 32.0f && lon > 35.42f && lon < 35.8f) return TerrainType.Desert;
                if (lat < 32.7f && lon < 35.45f) return h > 0.38f ? TerrainType.Hills : TerrainType.Plains;
                return moisture > 0.55f ? TerrainType.Grassland : TerrainType.Plains;
            }
            // Заиорданье.
            if (lat < 33.2f && coast > 0f && lon < coast + 2.1f) return h < 0.6f ? TerrainType.Plains : TerrainType.Desert;
            // Северная Сирия.
            if (lat >= 35.2f && lon < 38.6f) return moisture > 0.62f ? TerrainType.Grassland : TerrainType.Plains;
            // Джазира.
            if (lat >= 35.9f && lon < 43.5f) return moisture > 0.7f ? TerrainType.Grassland : TerrainType.Plains;
            // Земли за Тигром.
            if (lon >= 43.5f && lat >= 34.3f) return h > 0.58f ? TerrainType.Hills : TerrainType.Plains;
            // Месопотамия: пашня у рек, дальше пустыня.
            if (lon >= 43.2f)
            {
                float river = Math.Min(DistToLine(Euphrates, lon, lat), DistToLine(Tigris, lon, lat));
                if (river < 0.55f) return moisture > 0.5f ? TerrainType.Grassland : TerrainType.Plains;
            }
            // Сирийская пустыня.
            return TerrainType.Desert;
        }

        /// <summary>Река — непрерывная цепочка клеток речной долины (перерезает и горы: там речной проход).</summary>
        private void DrawRiver((float lon, float lat)[] line, bool widen)
        {
            var cells = new List<HexCoord>();
            for (int i = 1; i < line.Length; i++)
            {
                var a = FromLonLat(line[i - 1].lon, line[i - 1].lat);
                var b = FromLonLat(line[i].lon, line[i].lat);
                int n = Math.Max(1, a.DistanceTo(b));
                for (int s = 0; s <= n; s++) cells.Add(Lerp(a, b, s / (float)n));
            }
            foreach (var c in cells)
            {
                if (!_tiles.TryGetValue(c, out var t) || t.Terrain.IsWater()) continue;
                t.Terrain = TerrainType.River;
                t.Feature = TileFeature.None;
            }
            if (!widen) return;
            // В Месопотамии пойма шире.
            foreach (var c in cells)
            {
                var (lon, lat) = LonLat(c);
                if (lat > 34.5f) continue;
                foreach (var n in Neighbors(c).ToList())
                    if (n.Terrain != TerrainType.River && !n.Terrain.IsWater() && _noise.Hash01(n.Coord.q, n.Coord.r, 11) < 0.35f)
                        n.Terrain = TerrainType.River;
            }
        }

        private static HexCoord Lerp(HexCoord a, HexCoord b, float t)
        {
            float q = a.q + (b.q - a.q) * t + 1e-4f, r = a.r + (b.r - a.r) * t + 1e-4f, s = -q - r;
            int rq = (int)Math.Round(q), rr = (int)Math.Round(r), rs = (int)Math.Round(s);
            float dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        /// <summary>Болота Нижней Месопотамии (Ахвар).</summary>
        private void PlaceMarshes()
        {
            foreach (var t in _tiles.Values)
            {
                if (t.Terrain.IsWater() || t.Terrain == TerrainType.River || t.Terrain == TerrainType.Mountains) continue;
                var (lon, lat) = LonLat(t.Coord);
                if (lon < 44.9f || lat > 32.4f) continue;
                bool nearRiver = InRange(t.Coord, 2).Any(n => n.Terrain == TerrainType.River);
                if (nearRiver && _noise.Hash01(t.Coord.q, t.Coord.r, 13) < 0.75f) t.Terrain = TerrainType.Marsh;
            }
        }

        private void PlaceFeatures()
        {
            foreach (var tile in _tiles.Values)
            {
                if (tile.Terrain.IsWater() || tile.Terrain == TerrainType.River || tile.Terrain == TerrainType.Marsh ||
                    tile.Terrain == TerrainType.Mountains) continue;
                var (lon, lat) = LonLat(tile.Coord);
                float roll = _noise.Hash01(tile.Coord.q, tile.Coord.r, 7);
                if (tile.Terrain == TerrainType.Desert)
                {
                    if (roll < 0.035f) tile.Feature = TileFeature.Oasis;
                    continue;
                }
                float chance;
                float taurus = TaurusLat(lon);
                if (lon < 36.6f && lat < taurus + 0.1f && lat > taurus - 0.8f) chance = 0.5f;       // южные склоны Тавра
                else if (lat > 38.9f) chance = 0.3f;                                                // север Анатолии
                else if (Ranges.Skip(3).Any(rg => DistToLine(rg.Line, lon, lat) < rg.HalfWidth * 2.2f)) chance = 0.45f; // кедры Ливана, Аманос
                else if (tile.Terrain == TerrainType.Hills) chance = 0.12f;
                else if (tile.Terrain == TerrainType.Grassland) chance = 0.1f;
                else chance = 0.03f;
                if (roll < chance) tile.Feature = TileFeature.Forest;
            }
            foreach (var (lon, lat) in FixedOases)
            {
                var t = GetTile(FromLonLat(lon, lat));
                if (t != null && t.Terrain.IsPassable() && t.Terrain != TerrainType.River) t.Feature = TileFeature.Oasis;
            }
        }

        // ---------- Месторождения ----------

        /// <summary>Годится ли клетка под месторождение ресурса (id товара).</summary>
        public bool SuitsResource(HexTile t, string resource)
        {
            if (!t.Terrain.IsPassable() || t.CityId != null) return false;
            var biome = BiomeOf(t);
            return resource switch
            {
                "horses" => t.Terrain == TerrainType.Plains || t.Terrain == TerrainType.Grassland,
                "silk" => biome == Biome.Coast || biome == Biome.Forest || t.Terrain == TerrainType.Grassland,
                "dates" => biome == Biome.Oasis || biome == Biome.RiverValley || biome == Biome.Desert,
                "cotton" => biome == Biome.RiverValley || t.Terrain == TerrainType.Grassland || t.Terrain == TerrainType.Plains,
                "salt" => biome == Biome.Marsh || biome == Biome.Desert || biome == Biome.Coast,
                "glass" => biome == Biome.Coast || biome == Biome.Desert,
                _ => true,
            };
        }

        /// <summary>Шанс месторождения на клетке по биому и месту: так у каждой страны своя специализация.</summary>
        private string NaturalResource(HexTile t)
        {
            var (lon, lat) = LonLat(t.Coord);
            var biome = BiomeOf(t);
            float roll = _noise.Hash01(t.Coord.q, t.Coord.r, 21);
            bool levant = lon < 36.8f && lat < 36.4f;
            switch (biome)
            {
                case Biome.Steppe when lat > 35.8f && t.Terrain == TerrainType.Plains:
                    return roll < 0.08f ? "horses" : null;
                case Biome.Coast when lat > 33.4f && lon < 37f:
                    return roll < 0.3f ? "silk" : null;
                case Biome.Coast when levant:
                    return roll < 0.3f ? "glass" : null;
                case Biome.Oasis:
                    return roll < 0.6f ? "dates" : null;
                case Biome.RiverValley when lat < 34f:
                    return roll < 0.1f ? "dates" : roll < 0.16f ? "cotton" : null;
                case Biome.RiverValley:
                    return roll < 0.12f ? "cotton" : null;
                case Biome.Marsh:
                    return roll < 0.15f ? "salt" : null;
                case Biome.Desert when Dist(lon, lat, DeadSea.lon, DeadSea.lat) < 0.55f:
                    return roll < 0.35f ? "salt" : null;
                case Biome.Desert when Dist(lon, lat, SaltLake.lon, SaltLake.lat) < 0.6f:
                    return roll < 0.4f ? "salt" : null;
                case Biome.Desert:
                    return roll < 0.012f ? "salt" : null;
            }
            if (t.Terrain == TerrainType.Grassland && lon < 36.3f && lat > 36.3f && roll < 0.1f) return "silk";
            return null;
        }

        private void PlaceResources()
        {
            foreach (var t in _tiles.Values)
                if (t.Terrain.IsPassable()) t.Resource = NaturalResource(t);
        }

        /// <summary>
        /// Гарантировать месторождение ресурса рядом с точкой (стартовые специализации стран).
        /// Ищется подходящая клетка в радиусе 2, иначе любая проходимая.
        /// </summary>
        public void EnsureResource(HexCoord near, string resource, int count)
        {
            int have = InRange(near, 2).Count(t => t.Resource == resource);
            var spots = InRange(near, 2).Where(t => t.Coord != near && t.Resource == null && t.Terrain.IsPassable() && t.CityId == null)
                .OrderBy(t => SuitsResource(t, resource) ? 0 : 1).ThenBy(t => _noise.Hash01(t.Coord.q, t.Coord.r, 31)).ToList();
            foreach (var t in spots)
            {
                if (have >= count) break;
                t.Resource = resource;
                have++;
            }
        }

        /// <summary>
        /// Все точки должны быть связаны сушей: если нет — прорубаем перевал (горы на пути становятся холмами).
        /// </summary>
        public void EnsureLandConnected(IList<HexCoord> points)
        {
            if (points.Count < 2) return;
            for (int i = 1; i < points.Count; i++)
            {
                if (HexPathfinder.FindPath(this, points[0], points[i]) != null) continue;
                var path = HexPathfinder.FindPath(this, points[0], points[i], null,
                    t => t.Terrain.IsWater() ? TerrainRules.Impassable : t.Terrain == TerrainType.Mountains ? 6 : 1);
                if (path == null) continue;
                foreach (var c in path)
                    if (_tiles[c].Terrain == TerrainType.Mountains) _tiles[c].Terrain = TerrainType.Hills;
            }
        }

        /// <summary>Простой детерминированный value noise (0..1).</summary>
        private sealed class ValueNoise
        {
            private readonly int _seed;
            public ValueNoise(int seed) => _seed = seed;

            public float Fractal(float x, float y)
            {
                float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
                for (int i = 0; i < 4; i++)
                {
                    sum += Sample(x * freq, y * freq) * amp;
                    norm += amp;
                    amp *= 0.5f;
                    freq *= 2f;
                }
                return sum / norm;
            }

            private float Sample(float x, float y)
            {
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                float tx = Smooth(x - x0), ty = Smooth(y - y0);
                float a = Hash(x0, y0), b = Hash(x0 + 1, y0);
                float c = Hash(x0, y0 + 1), d = Hash(x0 + 1, y0 + 1);
                return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
            }

            public float Hash01(int x, int y, int salt) => Hash(x * 31 + salt, y * 17 - salt);

            private float Hash(int x, int y)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + _seed * 144269504);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / (float)0xFFFFFF;
                }
            }

            private static float Smooth(float t) => t * t * (3f - 2f * t);
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        }
    }
}

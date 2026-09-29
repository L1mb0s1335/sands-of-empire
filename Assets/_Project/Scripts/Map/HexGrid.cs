using System;
using System.Collections.Generic;

namespace Runeterra.Map
{
    /// <summary>
    /// Гекс-карта в форме шестиугольника заданного радиуса. Чистый C#, без UnityEngine —
    /// генерация детерминирована по seed, поэтому её можно гонять в тестах и на стороне ИИ.
    /// </summary>
    public class HexGrid
    {
        private readonly Dictionary<HexCoord, HexTile> _tiles = new Dictionary<HexCoord, HexTile>();

        public int Radius { get; }
        public int Seed { get; }
        public IReadOnlyCollection<HexTile> Tiles => _tiles.Values;

        public HexGrid(int radius, int seed)
        {
            Radius = radius;
            Seed = seed;
            Generate();
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile) => _tiles.TryGetValue(coord, out tile);

        public HexTile GetTile(HexCoord coord) => _tiles.TryGetValue(coord, out var t) ? t : null;

        public IEnumerable<HexTile> Neighbors(HexCoord coord)
        {
            for (int d = 0; d < 6; d++)
                if (_tiles.TryGetValue(coord.Neighbor(d), out var t))
                    yield return t;
        }

        private void Generate()
        {
            var noise = new ValueNoise(Seed);
            for (int q = -Radius; q <= Radius; q++)
            {
                int rMin = Math.Max(-Radius, -q - Radius);
                int rMax = Math.Min(Radius, -q + Radius);
                for (int r = rMin; r <= rMax; r++)
                {
                    var coord = new HexCoord(q, r);
                    // Материк в центре: высота падает к краю карты.
                    float edge = (float)coord.DistanceTo(default) / Radius;
                    float x = q + r * 0.5f, y = r * 0.866f;
                    float h = noise.Fractal(x * 0.18f, y * 0.18f) - edge * edge * 0.75f + 0.1f;
                    float moisture = noise.Fractal(x * 0.12f + 100f, y * 0.12f + 100f);
                    _tiles[coord] = new HexTile(coord, Classify(h, moisture), h, moisture);
                }
            }

            // Леса во влажных низинах, редкие оазисы в пустыне.
            foreach (var tile in _tiles.Values)
            {
                float roll = noise.Hash01(tile.Coord.q, tile.Coord.r, 7);
                if ((tile.Terrain == TerrainType.Grassland && tile.Moisture > 0.6f && roll < 0.75f) ||
                    (tile.Terrain == TerrainType.Plains && tile.Moisture > 0.5f && roll < 0.35f) ||
                    (tile.Terrain == TerrainType.Hills && tile.Moisture > 0.55f && roll < 0.4f))
                    tile.Feature = TileFeature.Forest;
                else if (tile.Terrain == TerrainType.Desert && roll < 0.08f)
                    tile.Feature = TileFeature.Oasis;
            }

            // Мелководье у берега.
            foreach (var tile in _tiles.Values)
            {
                if (tile.Terrain != TerrainType.Ocean) continue;
                foreach (var n in Neighbors(tile.Coord))
                {
                    if (!n.Terrain.IsWater())
                    {
                        tile.Terrain = TerrainType.Coast;
                        break;
                    }
                }
            }
        }

        private static TerrainType Classify(float h, float moisture)
        {
            if (h < 0.30f) return TerrainType.Ocean;
            if (h > 0.84f) return TerrainType.Mountains;
            if (h > 0.70f) return TerrainType.Hills;
            if (moisture < 0.38f) return TerrainType.Desert;
            if (moisture < 0.55f) return TerrainType.Plains;
            return TerrainType.Grassland;
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

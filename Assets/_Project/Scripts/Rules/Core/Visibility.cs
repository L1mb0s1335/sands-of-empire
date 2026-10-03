using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Map;

namespace Runeterra.Core
{
    /// <summary>
    /// Туман войны. Для каждого игрока: разведанные клетки (видел хоть раз) и видимые сейчас.
    /// Видят юниты (радиус обзора, +1 с холмов) и города (радиус 2 и вся их территория).
    /// ИИ пользуется тем же — он не знает того, чего не видел.
    /// </summary>
    public class Visibility
    {
        private readonly GameState _game;
        private readonly Dictionary<int, HashSet<HexCoord>> _explored = new Dictionary<int, HashSet<HexCoord>>();
        private readonly Dictionary<int, HashSet<HexCoord>> _visible = new Dictionary<int, HashSet<HexCoord>>();

        /// <summary>Видимость игрока изменилась (для перерисовки тумана).</summary>
        public event Action<int> Changed;

        public Visibility(GameState game) => _game = game;

        public HashSet<HexCoord> Explored(int player) => Get(_explored, player);
        public HashSet<HexCoord> Visible(int player) => Get(_visible, player);

        public bool IsVisible(int player, HexCoord c) => Visible(player).Contains(c);
        public bool IsExplored(int player, HexCoord c) => Explored(player).Contains(c);

        private static HashSet<HexCoord> Get(Dictionary<int, HashSet<HexCoord>> d, int p)
        {
            if (!d.TryGetValue(p, out var set)) d[p] = set = new HashSet<HexCoord>();
            return set;
        }

        public void RefreshAll()
        {
            foreach (var p in _game.Players) Refresh(p);
        }

        public void Refresh(PlayerState p)
        {
            var visible = new HashSet<HexCoord>();
            foreach (var u in p.Units.Where(u => u.IsAlive))
            {
                int r = u.Data.sightRange;
                if (_game.Grid.GetTile(u.Coord)?.Terrain == TerrainType.Hills) r++;
                AddRange(visible, u.Coord, r);
            }
            foreach (var c in _game.Cities.Where(c => c.OwnerIndex == p.Index))
            {
                AddRange(visible, c.Coord, 2);
                visible.UnionWith(c.Territory);
            }

            var old = Visible(p.Index);
            var explored = Explored(p.Index);
            int exploredBefore = explored.Count;
            explored.UnionWith(visible);
            bool changed = !old.SetEquals(visible) || explored.Count != exploredBefore;
            _visible[p.Index] = visible;
            if (changed) Changed?.Invoke(p.Index);
        }

        private void AddRange(HashSet<HexCoord> set, HexCoord center, int r)
        {
            for (int dq = -r; dq <= r; dq++)
            for (int dr = Math.Max(-r, -dq - r); dr <= Math.Min(r, -dq + r); dr++)
            {
                var c = center + new HexCoord(dq, dr);
                if (_game.Grid.TryGetTile(c, out _)) set.Add(c);
            }
        }

        /// <summary>Ближайшая к from неразведанная суша на границе разведанного (для разведки ИИ).</summary>
        public HexCoord? Frontier(int player, HexCoord from)
        {
            var explored = Explored(player);
            HexTile best = null;
            int bestDist = int.MaxValue;
            foreach (var t in _game.Grid.Tiles)
            {
                if (explored.Contains(t.Coord) || !t.Terrain.IsPassable()) continue;
                if (!_game.Grid.Neighbors(t.Coord).Any(n => explored.Contains(n.Coord))) continue;
                int d = t.Coord.DistanceTo(from);
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best?.Coord;
        }
    }
}

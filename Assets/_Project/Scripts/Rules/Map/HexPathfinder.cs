using System;
using System.Collections.Generic;

namespace Runeterra.Map
{
    /// <summary>
    /// A* и поиск досягаемых клеток по гекс-сетке. Чистый C#.
    /// Правило движения как в Civ: на клетку можно войти, если осталось хоть одно очко движения,
    /// после входа очки уменьшаются на стоимость (но не ниже нуля).
    /// </summary>
    public static class HexPathfinder
    {
        /// <param name="isBlocked">Клетка занята (например, другим юнитом). Может быть null.</param>
        /// <returns>Путь без стартовой клетки или null, если пути нет.</returns>
        /// <param name="stepCost">Стоимость входа на клетку (по умолчанию — очки движения юнита).</param>
        public static List<HexCoord> FindPath(HexGrid grid, HexCoord start, HexCoord goal, Func<HexCoord, bool> isBlocked = null,
            Func<HexTile, int> stepCost = null)
        {
            stepCost ??= t => t.MoveCost();
            if (start == goal) return new List<HexCoord>();
            if (!grid.TryGetTile(goal, out var goalTile) || stepCost(goalTile) == TerrainRules.Impassable) return null;
            if (isBlocked != null && isBlocked(goal)) return null;

            var open = new PriorityQueue();
            var cameFrom = new Dictionary<HexCoord, HexCoord>();
            var cost = new Dictionary<HexCoord, int> { [start] = 0 };
            open.Push(start, 0);

            while (open.Count > 0)
            {
                var current = open.Pop();
                if (current == goal) return Reconstruct(cameFrom, start, goal);

                foreach (var next in grid.Neighbors(current))
                {
                    int step = stepCost(next);
                    if (step == TerrainRules.Impassable) continue;
                    if (isBlocked != null && next.Coord != goal && isBlocked(next.Coord)) continue;

                    int newCost = cost[current] + step;
                    if (cost.TryGetValue(next.Coord, out int old) && newCost >= old) continue;
                    cost[next.Coord] = newCost;
                    cameFrom[next.Coord] = current;
                    open.Push(next.Coord, newCost + next.Coord.DistanceTo(goal));
                }
            }
            return null;
        }

        /// <summary>Клетки, куда можно дойти за текущий ход, с оставшимися очками движения.</summary>
        public static Dictionary<HexCoord, int> Reachable(HexGrid grid, HexCoord start, int movePoints, Func<HexCoord, bool> isBlocked = null)
        {
            var best = new Dictionary<HexCoord, int> { [start] = movePoints };
            var frontier = new Queue<HexCoord>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                int left = best[current];
                if (left <= 0) continue;
                foreach (var next in grid.Neighbors(current))
                {
                    int step = next.MoveCost();
                    if (step == TerrainRules.Impassable) continue;
                    if (isBlocked != null && isBlocked(next.Coord)) continue;
                    int remaining = Math.Max(0, left - step);
                    if (best.TryGetValue(next.Coord, out int prev) && prev >= remaining) continue;
                    best[next.Coord] = remaining;
                    frontier.Enqueue(next.Coord);
                }
            }
            best.Remove(start);
            return best;
        }

        private static List<HexCoord> Reconstruct(Dictionary<HexCoord, HexCoord> cameFrom, HexCoord start, HexCoord goal)
        {
            var path = new List<HexCoord>();
            for (var c = goal; c != start; c = cameFrom[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>Минимальная бинарная куча (в .NET Standard 2.1 нет PriorityQueue).</summary>
        private sealed class PriorityQueue
        {
            private readonly List<(HexCoord item, int priority)> _heap = new List<(HexCoord, int)>();
            public int Count => _heap.Count;

            public void Push(HexCoord item, int priority)
            {
                _heap.Add((item, priority));
                int i = _heap.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_heap[parent].priority <= _heap[i].priority) break;
                    (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                    i = parent;
                }
            }

            public HexCoord Pop()
            {
                var top = _heap[0].item;
                int last = _heap.Count - 1;
                _heap[0] = _heap[last];
                _heap.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < _heap.Count && _heap[l].priority < _heap[smallest].priority) smallest = l;
                    if (r < _heap.Count && _heap[r].priority < _heap[smallest].priority) smallest = r;
                    if (smallest == i) break;
                    (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
                    i = smallest;
                }
                return top;
            }
        }
    }
}

using System;
using UnityEngine;

namespace Runeterra.Map
{
    /// <summary>
    /// Кубические координаты гекса (q + r + s = 0). Чистый C#, без зависимостей от сцены.
    /// В инспекторе хранятся только q и r, s вычисляется.
    /// </summary>
    [Serializable]
    public struct HexCoord : IEquatable<HexCoord>
    {
        public int q;
        public int r;
        public int S => -q - r;

        public HexCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
        }

        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1),
        };

        public HexCoord Neighbor(int direction) => this + Directions[direction % 6];

        public int DistanceTo(HexCoord other) =>
            (Math.Abs(q - other.q) + Math.Abs(r - other.r) + Math.Abs(S - other.S)) / 2;

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.q + b.q, a.r + b.r);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.q - b.q, a.r - b.r);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);

        public bool Equals(HexCoord other) => q == other.q && r == other.r;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(q, r);
        public override string ToString() => $"({q}, {r}, {S})";
    }
}

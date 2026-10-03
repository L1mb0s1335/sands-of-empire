namespace Runeterra.Core
{
    /// <summary>
    /// Случайность без скрытого состояния: каждый бросок — хеш ключей (сид партии, ход, сторона,
    /// город или юнит, вид броска, номер броска). Один и тот же ключ даёт один и тот же результат
    /// у всех участников сетевой партии и после загрузки сохранения. Основа — SplitMix64.
    /// </summary>
    public static class DetRandom
    {
        /// <summary>Виды бросков: разные события с одинаковыми остальными ключами не совпадают.</summary>
        public enum Kind
        {
            Fire = 1,
            FireBurnsWorkshop,
            FireWorkshopPick,
            Epidemic,
            PeasantsFlee,
            Blight,
            MerchantStays,
            CaravanLost,
            AiClaim,
            AiTradeAgreement,
            AiTreaty,
        }

        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong Combine(ulong h, long v) => Mix(h ^ (ulong)v);

        /// <summary>Стабильный хеш строки (FNV-1a), не зависит от платформы и запуска.</summary>
        public static long StringKey(string s)
        {
            if (s == null) return 0;
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return (long)h;
        }

        public static ulong Hash(int seed, int turn, Kind kind, long a = 0, long b = 0, long c = 0)
        {
            ulong h = Mix((ulong)(uint)seed);
            h = Combine(h, turn);
            h = Combine(h, (long)kind);
            h = Combine(h, a);
            h = Combine(h, b);
            return Combine(h, c);
        }

        /// <summary>Число в [0, 1) из 53 старших бит хеша.</summary>
        public static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));

        /// <summary>Целое в [0, n).</summary>
        public static int Range(ulong h, int n) => n <= 1 ? 0 : (int)((h >> 1) % (ulong)n);
    }
}

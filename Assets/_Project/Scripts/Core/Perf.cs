using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Runeterra.Core
{
    /// <summary>Временный замер (автопроверка): суммарное время секций за ход.</summary>
    public static class Perf
    {
        private static readonly Dictionary<string, long> _ms = new Dictionary<string, long>();
        public static T Measure<T>(string key, System.Func<T> f)
        {
            var sw = Stopwatch.StartNew();
            try { return f(); }
            finally { _ms[key] = (_ms.TryGetValue(key, out var v) ? v : 0) + sw.ElapsedTicks; }
        }
        public static void Measure(string key, System.Action f) => Measure<int>(key, () => { f(); return 0; });
        public static string Dump()
        {
            var s = string.Join(" ", _ms.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value * 1000 / Stopwatch.Frequency}"));
            _ms.Clear();
            return s;
        }
    }
}

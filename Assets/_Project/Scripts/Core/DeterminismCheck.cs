using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Проверка детерминированности (autoplay с -detcheck): партии без сцены, за всех ходит ИИ,
    /// в начале каждого хода игрока печатается хеш состояния по разделам. Для каждого сида и сценария —
    /// два прогона: без перезагрузки и с сохранением/загрузкой на заданных ходах; цепочки хешей должны совпасть.
    /// Аргументы: -seeds 1-20, -scenarios hist,peace, -turns 200, -reloads 10,50,150, -side palestine;
    /// -legacy &lt;файл&gt; — дополнительно старое сохранение (v3): дважды загрузить и сыграть -legacyturns ходов, цепочки должны совпасть.
    /// </summary>
    public static class DeterminismCheck
    {
        public sealed class Options
        {
            public List<int> Seeds = Enumerable.Range(1, 20).ToList();
            public List<Scenario> Scenarios = new List<Scenario> { Scenario.Historical1187, Scenario.PeacefulDevelopment };
            public int Turns = 200;
            public List<int> Reloads = new List<int> { 10, 50, 150 };
            public string Side = "palestine";
            public string Legacy;
            public int LegacyTurns = 30;

            public static Options Parse(string[] args)
            {
                string Arg(string name) { int k = System.Array.IndexOf(args, name); return k >= 0 && k + 1 < args.Length ? args[k + 1] : null; }
                var o = new Options();
                if (Arg("-seeds") is string seeds)
                {
                    var parts = seeds.Split('-');
                    o.Seeds = parts.Length == 2
                        ? Enumerable.Range(int.Parse(parts[0]), int.Parse(parts[1]) - int.Parse(parts[0]) + 1).ToList()
                        : seeds.Split(',').Select(int.Parse).ToList();
                }
                if (Arg("-scenarios") is string sc)
                    o.Scenarios = sc.Split(',').Select(x => x == "peace" ? Scenario.PeacefulDevelopment : Scenario.Historical1187).ToList();
                if (int.TryParse(Arg("-turns"), out var t)) o.Turns = t;
                if (Arg("-reloads") is string r) o.Reloads = r.Split(',').Select(int.Parse).ToList();
                if (Arg("-side") is string side) o.Side = side;
                o.Legacy = Arg("-legacy");
                if (int.TryParse(Arg("-legacyturns"), out var lt)) o.LegacyTurns = lt;
                return o;
            }
        }

        private static string Short(Scenario s) => s == Scenario.PeacefulDevelopment ? "peace" : "hist";

        /// <summary>Ход игрока сыгран логикой ИИ (как в автопроверке сцены), затем ходят остальные.</summary>
        internal static void PlayRound(GameState s, AiPlayer ai)
        {
            var human = s.Turns.Current;
            foreach (var p in s.Diplomacy.Proposals.ToList())
                s.Diplomacy.Answer(p, human.Index, ai.Accepts(human.Index, p.From, p.Kind));
            ai.PlayTurn(human);
            s.Turns.EndTurn();
        }

        /// <summary>Партия до конца срока; на ходах из reloads — сохранение в JSON и загрузка в новое состояние.</summary>
        private static List<(int turn, StateHash hash)> Play(GameController game, GameSetup setup, int turns, ICollection<int> reloads,
            bool print, List<string> problems)
        {
            var (s, ai) = game.CreateHeadless(setup);
            var chain = new List<(int, StateHash)>();
            while (true)
            {
                int turn = s.Turns.Turn;
                if (reloads.Contains(turn) && s.Winner == null)
                {
                    var before = StateHash.Of(s, ai);
                    var json = SaveSystem.ToJson(SaveSystem.Capture(s, ai));
                    (s, ai) = game.CreateHeadless(setup, JsonUtility.FromJson<SaveSystem.SaveData>(json));
                    var after = StateHash.Of(s, ai);
                    if (!after.Equals(before))
                        problems.Add($"{Short(setup.Scenario)} сид {setup.Seed}: состояние после загрузки на ходу {turn} отличается ({before.DiffSections(after)})");
                }
                var h = StateHash.Of(s, ai);
                chain.Add((turn, h));
                if (print) Debug.Log($"[HASH] {Short(setup.Scenario)} seed={setup.Seed} turn={turn} {h}");
                if (s.Winner != null || turn > turns) break;
                PlayRound(s, ai);
            }
            return chain;
        }

        /// <summary>Партия из сохранения: ещё turns ходов от хода загрузки.</summary>
        private static List<(int turn, StateHash hash)> PlayFromSave(GameController game, string json, int turns)
        {
            var save = JsonUtility.FromJson<SaveSystem.SaveData>(json);
            var (s, ai) = game.CreateHeadless(new GameSetup(), save);
            var chain = new List<(int, StateHash)>();
            int end = s.Turns.Turn + turns;
            while (true)
            {
                var h = StateHash.Of(s, ai);
                chain.Add((s.Turns.Turn, h));
                if (s.Winner != null || s.Turns.Turn >= end) break;
                PlayRound(s, ai);
            }
            return chain;
        }

        /// <summary>Первое расхождение двух цепочек (null — совпадают).</summary>
        internal static string Compare(List<(int turn, StateHash hash)> a, List<(int turn, StateHash hash)> b)
        {
            for (int i = 0; i < System.Math.Min(a.Count, b.Count); i++)
            {
                if (a[i].turn != b[i].turn) return $"ход {a[i].turn} против {b[i].turn}";
                if (!a[i].hash.Equals(b[i].hash)) return $"ход {a[i].turn}: {a[i].hash.DiffSections(b[i].hash)}";
            }
            return a.Count == b.Count ? null : $"длина {a.Count} против {b.Count}";
        }

        public static IEnumerator Run(GameController game, Options o)
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            var problems = new List<string>();
            int runs = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[DETCHECK] start: seeds {string.Join(",", o.Seeds)}, scenarios {string.Join(",", o.Scenarios.Select(Short))}, " +
                      $"turns {o.Turns}, reloads {string.Join(",", o.Reloads)}, side {o.Side}");
            foreach (var scenario in o.Scenarios)
            foreach (int seed in o.Seeds)
            {
                var setup = new GameSetup { Scenario = scenario, HumanRegion = o.Side, TurnLimit = o.Turns, Seed = seed };
                int before = problems.Count;
                var plain = Play(game, setup, o.Turns, new int[0], true, problems);
                yield return null;
                var reloaded = Play(game, setup, o.Turns, o.Reloads, false, problems);
                runs += 2;
                var diff = Compare(plain, reloaded);
                if (diff != null) problems.Add($"{Short(scenario)} сид {seed}: цепочка с перезагрузками расходится — {diff}");
                var last = plain[plain.Count - 1];
                Debug.Log($"[DETCHECK] {Short(scenario)} seed={seed}: ходов {last.turn}, перезагрузки {string.Join("/", o.Reloads)} — " +
                          (problems.Count == before ? "цепочки совпали" : "РАСХОЖДЕНИЕ") + $", итог {last.hash.Total:x16}, {sw.Elapsed.TotalSeconds:0} с");
                yield return null;
            }
            if (!string.IsNullOrEmpty(o.Legacy))
            {
                string json = System.IO.File.ReadAllText(o.Legacy);
                var save = JsonUtility.FromJson<SaveSystem.SaveData>(json);
                List<(int turn, StateHash hash)> a = null, b = null;
                try
                {
                    a = PlayFromSave(game, json, o.LegacyTurns);
                    b = PlayFromSave(game, json, o.LegacyTurns);
                }
                catch (System.Exception e) { problems.Add($"старое сохранение v{save.version}: {e.Message}"); }
                if (a != null)
                {
                    var diff = Compare(a, b);
                    if (diff != null) problems.Add($"старое сохранение v{save.version}: две загрузки расходятся — {diff}");
                    Debug.Log($"[DETCHECK] legacy v{save.version} ход {save.turn}: сыграно до хода {a[a.Count - 1].turn}, " +
                              (diff == null ? "две загрузки совпали" : "РАСХОЖДЕНИЕ") + $", итог {a[a.Count - 1].hash.Total:x16}");
                }
                runs += 2;
            }
            foreach (var p in problems) Debug.Log($"[DETCHECK] ошибка: {p}");
            Debug.Log($"[DETCHECK] done: прогонов {runs}, ошибок {problems.Count}, {sw.Elapsed.TotalSeconds:0} с — {(problems.Count == 0 ? "OK" : "FAIL")}");
        }
    }
}

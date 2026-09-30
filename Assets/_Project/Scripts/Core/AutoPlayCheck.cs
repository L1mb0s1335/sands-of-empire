using System.Collections;
using System.IO;
using Runeterra.Units;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Автопроверка сборки: запуск с аргументом -autoplay &lt;папка&gt; проходит
    /// меню → партию (за игрока ходит логика ИИ) → экран итога → «Заново» и сохраняет скриншоты.
    /// Без аргумента ничего не делает.
    /// </summary>
    public class AutoPlayCheck : MonoBehaviour
    {
        private string _outDir;
        private GameSetup _setup;

        /// <summary>Идёт автопроверка (журнал дипломатии пишется в лог).</summary>
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-autoplay");
            if (i < 0) return;
            Active = true;
            var go = new GameObject("AutoPlayCheck");
            DontDestroyOnLoad(go);
            var check = go.AddComponent<AutoPlayCheck>();
            check._outDir = i + 1 < args.Length ? args[i + 1] : Application.persistentDataPath;
            string Arg(string name) { int k = System.Array.IndexOf(args, name); return k >= 0 && k + 1 < args.Length ? args[k + 1] : null; }
            check._setup = new GameSetup
            {
                Scenario = Arg("-scenario") == "peace" ? Scenario.PeacefulDevelopment : Scenario.Historical1187,
                HumanRegion = Arg("-side") ?? "palestine",
                TurnLimit = int.TryParse(Arg("-turns"), out var t) ? t : 200,
            };
            Directory.CreateDirectory(check._outDir);
        }

        private void Start() => StartCoroutine(Run());

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, name));
            yield return null;
            Debug.Log($"[AUTOPLAY] screenshot {name}");
        }

        /// <summary>Ход последней проверки сохранения (20 — начало, 70 — войны, претензии, соглашения).</summary>
        private int _savedAt;

        /// <summary>
        /// Сохранить партию, выйти в главное меню, нажать «Загрузить» и сравнить состояние
        /// до и после (снимки JSON должны совпасть).
        /// </summary>
        private IEnumerator SaveLoadCheck(GameController game, System.Action<GameController> done)
        {
            Time.timeScale = 1f;
            string before = game.Snapshot();
            bool saved = game.SaveGame();
            Debug.Log($"[AUTOPLAY] save at turn {game.Turns.Turn}: ok={saved}, file={SaveSystem.SavePath}, size={new FileInfo(SaveSystem.SavePath).Length}");
            yield return Shot("03e_saved.png");

            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            MainMenu menu = null;
            while (menu == null) { yield return null; menu = FindFirstObjectByType<MainMenu>(); }
            yield return new WaitForSeconds(0.5f);
            yield return Shot("03f_menu_with_load.png");
            Debug.Log($"[AUTOPLAY] menu load: {menu.LoadGame()}");

            GameController fresh = null;
            while (fresh == null || fresh.Turns == null || fresh == game) { yield return null; fresh = FindFirstObjectByType<GameController>(); }
            yield return new WaitForSeconds(1f);
            string after = fresh.Snapshot();
            int diff = 0;
            while (diff < before.Length && diff < after.Length && before[diff] == after[diff]) diff++;
            bool same = before == after;
            Debug.Log($"[AUTOPLAY] loaded: turn={fresh.Turns.Turn}, current={fresh.Turns.Current.Region.displayName}, cities={fresh.Cities.Count}, " +
                      $"units={string.Join("/", System.Linq.Enumerable.Select(fresh.Turns.Players, p => p.Units.Count))}, caravans={fresh.State.Trade.Caravans.Count}, " +
                      $"gold={string.Join("/", System.Linq.Enumerable.Select(fresh.Turns.Players, p => p.Gold))}, state identical={same}" +
                      (same ? "" : $", first diff at {diff}: before «{Excerpt(before, diff)}» after «{Excerpt(after, diff)}»"));
            yield return Shot("03g_loaded.png");
            Time.timeScale = 6f;
            done(fresh);
        }

        private static string Excerpt(string s, int at) => s.Substring(System.Math.Max(0, at - 80), System.Math.Min(160, s.Length - System.Math.Max(0, at - 80)));

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(1.5f);
            var menu = FindFirstObjectByType<MainMenu>();
            Debug.Log($"[AUTOPLAY] menu found: {menu != null}");
            yield return Shot("01_menu.png");
            menu.ShowSetup(true);
            yield return new WaitForSeconds(0.3f);
            yield return Shot("01b_setup.png");
            Debug.Log($"[AUTOPLAY] setup: {GameSetup.ScenarioName(_setup.Scenario)}, side={_setup.HumanRegion}, turns={_setup.TurnLimit}");
            menu.StartGame(_setup);

            GameController game = null;
            while (game == null || game.Turns == null)
            {
                yield return null;
                game = FindFirstObjectByType<GameController>();
            }
            yield return new WaitForSeconds(1f);
            Debug.Log($"[AUTOPLAY] game started, players: {game.Turns.Players.Count}, cities: {game.Cities.Count}");
            yield return Shot("02_turn1.png");

            // Панель города и кнопка строителя.
            var human = System.Linq.Enumerable.First(game.Turns.Players, p => p.IsHuman);
            game.SelectCity(game.State.CapitalOf(human));
            yield return new WaitForSeconds(0.3f);
            yield return Shot("02b_city_panel.png");
            game.SetCityTab(1);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("02d_city_build.png");
            game.SetCityTab(2);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("02e_city_store.png");
            game.SetCityTab(0);
            var builder = human.Units.Find(u => u.Data.buildCharges > 0);
            if (builder != null)
            {
                game.SelectUnit(builder);
                yield return new WaitForSeconds(0.3f);
                yield return Shot("02c_builder.png");
            }
            game.SelectCity(null);

            Time.timeScale = 6f;
            while (game.Winner == null && game.Turns.Turn <= game.State.TurnLimit + 2)
            {
                while (game.Busy) yield return null;
                if ((game.Turns.Turn == 20 || game.Turns.Turn == 70) && _savedAt != game.Turns.Turn)
                {
                    _savedAt = game.Turns.Turn;
                    GameController loaded = null;
                    yield return SaveLoadCheck(game, g => loaded = g);
                    game = loaded;
                }
                game.AutoPlayHumanTurn();
                while (game.Busy) yield return null;
                if (game.Turns.Turn == 8) yield return Shot("03_turn8.png");
                if (game.Turns.Turn == 30)
                {
                    game.ShowTreasury(true);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03a_turn30_treasury.png");
                    game.ShowTreasury(false);
                    game.ShowTech(true);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03c_turn30_tech.png");
                    game.ShowTech(false);
                    game.ShowUnitList(true);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03d_turn30_units.png");
                    game.ShowUnitList(false);
                    game.ShowDiplomacy(true);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03i_turn30_diplomacy.png");
                    game.SetDiplomacyTab(1);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03j_turn30_trade.png");
                    game.SetDiplomacyTab(0);
                    game.ShowDiplomacy(false);
                    game.ShowMenu(true);
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03h_turn30_menu.png");
                    game.ShowMenu(false);
                }
                if (game.Turns.Turn == 40)
                {
                    game.SelectCity(game.State.CapitalOf(System.Linq.Enumerable.First(game.Turns.Players, p => p.IsHuman)));
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("03b_turn40_city.png");
                    game.SelectCity(null);
                }
                game.EndTurn();
                yield return null;
                Debug.Log($"[AUTOPLAY] turn {game.Turns.Turn}: units " +
                          string.Join(" / ", System.Linq.Enumerable.Select(game.Turns.Players, p => $"{p.Region.displayName}={p.Units.Count}")) +
                          $" | cities {string.Join("/", System.Linq.Enumerable.Select(game.Turns.Players, p => System.Linq.Enumerable.Count(game.Cities, c => c.OwnerIndex == p.Index)))}" +
                          $" | gold {string.Join("/", System.Linq.Enumerable.Select(game.Turns.Players, p => p.Gold))}" +
                          $" | wars {game.State.WarSummary()} | ai {game.LastAiRoundMs} ms");
            }
            Time.timeScale = 1f;
            while (game.Busy) yield return null;
            yield return new WaitForSeconds(1f);

            var winner = game.Winner != null ? game.Turns.Players[game.Winner.Value] : null;
            Debug.Log($"[AUTOPLAY] game over at turn {game.Turns.Turn}: winner={(winner != null ? winner.Region.displayName : "none")}, human won={winner?.IsHuman}, text={game.State.GameOverText}");
            Debug.Log("[AUTOPLAY] scores: " + string.Join(", ", System.Linq.Enumerable.Select(game.Turns.Players,
                p => $"{p.Region.displayName}={game.State.Score(p)} (городов {System.Linq.Enumerable.Count(game.Cities, c => c.OwnerIndex == p.Index)}, узлов {p.Techs.Count})")));
            yield return Shot("04_game_over.png");

            game.Restart();
            yield return new WaitForSeconds(2f);
            var fresh = FindFirstObjectByType<GameController>();
            Debug.Log($"[AUTOPLAY] after restart: turn={fresh.Turns.Turn}, winner={(fresh.Winner == null ? "none" : fresh.Winner.ToString())}, units={fresh.Turns.Players[0].Units.Count}+{fresh.Turns.Players[1].Units.Count}, scenario={GameSetup.ScenarioName(fresh.State.Scenario)}, human={System.Linq.Enumerable.First(fresh.Turns.Players, p => p.IsHuman).Region.displayName}");
            yield return Shot("05_restart.png");
            Debug.Log("[AUTOPLAY] done");
            Application.Quit();
        }
    }
}

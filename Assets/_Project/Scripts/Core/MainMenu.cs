using UnityEngine;
using UnityEngine.SceneManagement;
using static Runeterra.Core.HudSkin;

namespace Runeterra.Core
{
    /// <summary>
    /// Главное меню в стиле пергамента и бронзы: «Новая игра» (выбор сценария, стороны и длины партии),
    /// «Загрузить» и «Выход».
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public string gameScene = "HexMap";
        public string title = "Runeterra 4X";

        private bool _setup;
        private string _message;
        private readonly GameSetup _choice = new GameSetup();

        /// <summary>Стороны партии (id регионов сцены партии), имена лидеров и цвета для выбора в меню.</summary>
        private static readonly (string id, string name, string leader, Color color)[] Sides =
        {
            ("palestine", "Палестина (Айюбиды)", "Салах ад-Дин", new Color(0f, 0.478f, 0.239f)),
            ("jerusalem_kingdom", "Крестоносцы", "Ричард Львиное Сердце", new Color(0.93f, 0.91f, 0.86f)),
            ("byzantium", "Византия", "Исаак II Ангел", new Color(0.45f, 0.12f, 0.45f)),
            ("rum", "Султанат Рум", "Кылыч-Арслан II", new Color(0.12f, 0.45f, 0.72f)),
            ("abbasids", "Аббасидский халифат", "ан-Насир", new Color(0.12f, 0.12f, 0.14f)),
            ("mosul", "Зангиды Мосула", "Изз ад-Дин Масуд", new Color(0.78f, 0.33f, 0.10f)),
        };

        private void Awake() => _choice.HumanRegion = Sides[0].id;

        private void OnGUI()
        {
            Ensure();
            Fill(new Rect(0, 0, W, H), TealDark);
            Label(new Rect(0, 60, W, 70), $"<size={Sz(56)}>{title}</size>", CenterTitle);
            Label(new Rect(0, 128, W, 30), "Левант, Анатолия и Месопотамия · осень 1187 года", CenterLight);
            if (_setup) DrawSetup();
            else DrawMain();
        }

        private static GUIStyle _centerTitle;
        private static GUIStyle CenterTitle => _centerTitle ??= new GUIStyle(Title) { alignment = TextAnchor.MiddleCenter };

        private void DrawMain()
        {
            var r = new Rect(W / 2f - 200, H / 2f - 110, 400, 280);
            Window(r);
            float x = r.x + 50, w = r.width - 100;
            if (Button(new Rect(x, r.y + 36, w, 56), "Новая игра", BtnBig)) _setup = true;
            GUI.enabled = SaveSystem.HasSave;
            if (Button(new Rect(x, r.y + 108, w, 56), "Загрузить", BtnBig) && !LoadGame())
                _message = "Сохранение от старой версии игры — начните новую партию";
            GUI.enabled = true;
            if (_message != null) Label(new Rect(r.x, r.yMax + 16, r.width, 26), _message, CenterLight);
            if (Button(new Rect(x, r.y + 180, w, 56), "Выход", BtnBig)) Quit();
        }

        private void DrawSetup()
        {
            var r = new Rect(W / 2f - 480, 180, 960, Mathf.Min(640, H - 200));
            Window(r);
            float pad = 28, x = r.x + pad, y = r.y + 16, w = r.width - pad * 2;
            Label(new Rect(x, y, w, 32), "Новая партия", Heading);
            y += 42;

            Label(new Rect(x, y, w, 24), "<b>Сценарий</b>", InkMid);
            y += 28;
            float half = (w - 12) / 2f;
            foreach (var s in new[] { Scenario.Historical1187, Scenario.PeacefulDevelopment })
            {
                var b = new Rect(x + (s == Scenario.Historical1187 ? 0 : half + 12), y, half, 36);
                if (GUI.Toggle(R(b), _choice.Scenario == s, GameSetup.ScenarioName(s), Btn) && _choice.Scenario != s) _choice.Scenario = s;
            }
            y += 42;
            Label(new Rect(x, y, w, 44), GameSetup.ScenarioText(_choice.Scenario), BodySmall);
            y += 52;

            Label(new Rect(x, y, w, 24), "<b>Сторона</b>", InkMid);
            y += 28;
            float cw = (w - 24) / 3f, ch = 58;
            for (int i = 0; i < Sides.Length; i++)
            {
                var side = Sides[i];
                var b = new Rect(x + (i % 3) * (cw + 12), y + (i / 3) * (ch + 10), cw, ch);
                bool on = _choice.HumanRegion == side.id;
                if (GUI.Toggle(R(b), on, "", Btn) && !on) _choice.HumanRegion = side.id;
                Fill(new Rect(b.x + 10, b.y + 10, 10, ch - 20), side.color);
                // На выбранной (тёмной) кнопке — светлый текст.
                string ink = on ? LGold : "#2b2118", sub = on ? LMuted : CMuted;
                Label(new Rect(b.x + 30, b.y + 8, cw - 36, 24), $"<color={ink}><b>{side.name}</b></color>", Body);
                Label(new Rect(b.x + 30, b.y + 32, cw - 36, 20), $"<color={sub}>{side.leader}</color>", CaptionInk);
            }
            y += (ch + 10) * 2 + 8;

            Label(new Rect(x, y, w, 24), "<b>Длина партии</b>", InkMid);
            y += 28;
            float lw = (w - 24) / 3f;
            for (int i = 0; i < GameSetup.Lengths.Length; i++)
            {
                int turns = GameSetup.Lengths[i];
                var b = new Rect(x + i * (lw + 12), y, lw, 36);
                bool on = _choice.TurnLimit == turns;
                if (GUI.Toggle(R(b), on, $"{GameSetup.LengthName(turns)} — {turns} ходов", Btn) && !on) _choice.TurnLimit = turns;
            }
            y += 48;

            if (Button(new Rect(r.x + pad, r.yMax - 70, 200, 48), "Назад", BtnBig)) _setup = false;
            if (Button(new Rect(r.xMax - pad - 260, r.yMax - 70, 260, 48), "Начать", BtnBig)) StartGame(_choice);
        }

        public void ShowSetup(bool show) => _setup = show;

        /// <summary>Новая партия с настройками по умолчанию.</summary>
        public void NewGame() => StartGame(new GameSetup { HumanRegion = Sides[0].id });

        public void StartGame(GameSetup setup)
        {
            SaveSystem.PendingLoad = null;
            GameSetup.Pending = setup;
            SceneManager.LoadScene(gameScene);
        }

        /// <summary>Продолжить сохранённую партию.</summary>
        public bool LoadGame()
        {
            var save = SaveSystem.Read();
            if (save == null) return false;
            SaveSystem.PendingLoad = save;
            SceneManager.LoadScene(gameScene);
            return true;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

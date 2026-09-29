using UnityEngine;
using UnityEngine.SceneManagement;

namespace Runeterra.Core
{
    /// <summary>Главное меню: «Новая игра», «Загрузить» и «Выход».</summary>
    public class MainMenu : MonoBehaviour
    {
        public string gameScene = "HexMap";
        public string title = "Runeterra 4X";

        private GUIStyle _title, _button;

        private static float Scale => Mathf.Max(1f, Screen.height / 900f);

        private void OnGUI()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _button = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            }
            _title.fontSize = Mathf.RoundToInt(64 * Scale);
            _button.fontSize = Mathf.RoundToInt(24 * Scale);

            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            float w = 320 * Scale, h = 70 * Scale;
            GUI.Label(new Rect(cx - 500 * Scale, cy - 220 * Scale, 1000 * Scale, 100 * Scale), title, _title);
            if (GUI.Button(new Rect(cx - w / 2f, cy - 40 * Scale, w, h), "Новая игра", _button)) NewGame();
            GUI.enabled = SaveSystem.HasSave;
            if (GUI.Button(new Rect(cx - w / 2f, cy + 50 * Scale, w, h), "Загрузить", _button)) LoadGame();
            GUI.enabled = true;
            if (GUI.Button(new Rect(cx - w / 2f, cy + 140 * Scale, w, h), "Выход", _button)) Quit();
        }

        public void NewGame()
        {
            SaveSystem.PendingLoad = null;
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

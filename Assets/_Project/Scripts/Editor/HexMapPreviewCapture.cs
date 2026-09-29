using System.IO;
using Runeterra.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Runeterra.EditorTools
{
    /// <summary>Рендерит превью сцены гекс-карты в PNG (удобно для проверки из batchmode).</summary>
    public static class HexMapPreviewCapture
    {
        [MenuItem("Runeterra/Capture Hex Map Preview")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/HexMap.unity");
            var map = Object.FindFirstObjectByType<HexMapView>();
            var game = Object.FindFirstObjectByType<Runeterra.Core.GameController>();
            if (game != null) game.Initialize();
            else map.Build();
            var cam = map.mapCamera;

            Save(cam, "Logs/hexmap_preview.png");

            // Обзор всей карты сверху.
            cam.transform.SetPositionAndRotation(map.transform.position + new Vector3(0f, map.mapRadius * 2.6f * map.hexSize, -map.mapRadius * 0.9f * map.hexSize),
                Quaternion.Euler(72f, 0f, 0f));
            var far = cam.farClipPlane;
            cam.farClipPlane = 500f;
            var fog = RenderSettings.fog;
            RenderSettings.fog = false;
            Save(cam, "Logs/hexmap_overview.png");
            RenderSettings.fog = fog;
            cam.farClipPlane = far;
        }

        private static void Save(Camera cam, string file)
        {
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;

            var path = Path.GetFullPath(file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[Runeterra] Превью сохранено: {path}");
        }
    }
}

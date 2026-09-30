using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Runeterra.EditorTools
{
    /// <summary>Сборка под Linux. Меню: Runeterra → Build Linux. Результат: Builds/Linux/Runeterra4X.x86_64</summary>
    public static class LinuxBuilder
    {
        public const string OutputPath = "Builds/Linux/Runeterra4X.x86_64";

        /// <summary>Контент → сцены → сборка (для пакетного режима: -executeMethod Runeterra.EditorTools.LinuxBuilder.BuildAll).</summary>
        [MenuItem("Runeterra/Rebuild Content, Scenes and Linux")]
        public static void BuildAll()
        {
            ContentBuilder.Build();
            HexMapSceneBuilder.Build();
            Build();
        }

        [MenuItem("Runeterra/Build Linux")]
        public static void Build()
        {
            PlayerSettings.productName = "Runeterra 4X";
            PlayerSettings.runInBackground = true; // автопроверка не должна вставать, если окно без фокуса
            var options = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Runeterra] Сборка: {report.summary.result}, ошибок: {report.summary.totalErrors}, размер: {report.summary.totalSize / (1024 * 1024)} МБ, путь: {OutputPath}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}

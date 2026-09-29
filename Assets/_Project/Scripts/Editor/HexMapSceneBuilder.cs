using System.IO;
using Runeterra.Core;
using Runeterra.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Runeterra.EditorTools
{
    /// <summary>Создаёт тестовую сцену с гекс-картой. Меню: Runeterra → Create Hex Map Scene.</summary>
    public static class HexMapSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/HexMap.unity";
        private const string MenuScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string MaterialPath = "Assets/_Project/Art/Materials/HexBase.mat";
        private const string RegionsFolder = "Assets/_Project/Data/Regions";
        private const string UnitsFolder = "Assets/_Project/Data/Units";
        private const string MarketPath = "Assets/_Project/Data/Districts/District_Market.asset";

        [MenuItem("Runeterra/Create Hex Map Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));

            var shader = Shader.Find("Runeterra/HexTerrain");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetFloat("_Glossiness", 0.85f);
            material.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(material);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Camera.main;
            camera.gameObject.AddComponent<HexCameraController>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.72f, 0.80f);
            camera.farClipPlane = 200f;

            var map = new GameObject("HexMap").AddComponent<HexMapView>();
            map.baseMaterial = material;
            const string fogPath = "Assets/_Project/Art/Materials/FogOverlay.mat";
            var fog = AssetDatabase.LoadAssetAtPath<Material>(fogPath);
            if (fog == null)
            {
                fog = new Material(Shader.Find("Runeterra/FogOverlay"));
                AssetDatabase.CreateAsset(fog, fogPath);
            }
            map.fogMaterial = fog;
            map.mapCamera = camera;
            foreach (var guid in AssetDatabase.FindAssets("t:RegionData", new[] { RegionsFolder }))
                map.regions.Add(AssetDatabase.LoadAssetAtPath<RegionData>(AssetDatabase.GUIDToAssetPath(guid)));
            // Первый регион — игрок (Палестина), остальные — ИИ.
            map.regions.Sort((a, b) => (b.id == "palestine").CompareTo(a.id == "palestine"));

            var game = new GameObject("Game").AddComponent<GameController>();
            game.map = map;
            // Магазин: строитель, разведчик, воин, лучник — по цене.
            foreach (var guid in AssetDatabase.FindAssets("t:UnitData", new[] { UnitsFolder }))
                game.shop.Add(AssetDatabase.LoadAssetAtPath<Runeterra.Units.UnitData>(AssetDatabase.GUIDToAssetPath(guid)));
            game.shop.Sort((a, b) => a.goldCost != b.goldCost ? a.goldCost.CompareTo(b.goldCost) : a.role.CompareTo(b.role));
            game.market = AssetDatabase.LoadAssetAtPath<Runeterra.Cities.DistrictData>(MarketPath);
            game.port = AssetDatabase.LoadAssetAtPath<Runeterra.Cities.DistrictData>("Assets/_Project/Data/Districts/District_Port.asset");
            foreach (var guid in AssetDatabase.FindAssets("t:GoodData", new[] { "Assets/_Project/Data/Goods" }))
                game.goods.Add(AssetDatabase.LoadAssetAtPath<Runeterra.Economy.GoodData>(AssetDatabase.GUIDToAssetPath(guid)));
            game.goods.Sort((a, b) => a.basePrice.CompareTo(b.basePrice));
            foreach (var guid in AssetDatabase.FindAssets("t:TechData", new[] { "Assets/_Project/Data/Tech" }))
                game.techs.Add(AssetDatabase.LoadAssetAtPath<Runeterra.Tech.TechData>(AssetDatabase.GUIDToAssetPath(guid)));
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/_Project/Data/Buildings" }))
                game.buildings.Add(AssetDatabase.LoadAssetAtPath<Runeterra.Economy.BuildingData>(AssetDatabase.GUIDToAssetPath(guid)));

            EditorSceneManager.SaveScene(scene, ScenePath);

            BuildMenuScene();

            // Порядок сборки: меню, затем игра.
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
            AssetDatabase.SaveAssets();
            Debug.Log($"[Runeterra] Сцена гекс-карты создана: {ScenePath}, регионов: {map.regions.Count}");
        }

        private static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.14f, 0.20f);
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            menu.gameScene = Path.GetFileNameWithoutExtension(ScenePath);
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }
    }
}

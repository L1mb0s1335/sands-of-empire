using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Core;
using UnityEngine;

namespace Runeterra.Map
{
    /// <summary>
    /// Визуал гекс-карты: рельеф с уступами, смешение цветов, береговые пляжи, горы/холмы/леса,
    /// города, границы регионов и подсветка клеток. Логика карты — в HexGrid.
    /// </summary>
    public class HexMapView : MonoBehaviour
    {
        [Header("Генерация")]
        [Min(4)] public int mapRadius = 14;
        public int seed = 1337;
        [Min(0.1f)] public float hexSize = 1f;

        [Header("Регионы")]
        public List<RegionData> regions = new List<RegionData>();
        [Tooltip("Стартовые точки регионов (по индексу). Если нет — ближайшая к центру суша.")]
        public List<HexCoord> regionStarts = new List<HexCoord>();
        [Min(0)] public int cityTerritoryRadius = 1;

        [Header("Визуал")]
        public Material baseMaterial;
        [Tooltip("Прозрачный материал тумана войны (Runeterra/FogOverlay)")]
        public Material fogMaterial;
        public Camera mapCamera;
        public bool showGrid = true;
        public bool applyAtmosphere = true;

        public HexGrid Grid { get; private set; }
        public bool IsBuilt => Grid != null;

        private readonly Dictionary<HexCoord, RegionData> _owners = new Dictionary<HexCoord, RegionData>();
        /// <summary>Клетка территории → клетка города, которому она принадлежит.</summary>
        private readonly Dictionary<HexCoord, HexCoord> _territoryCity = new Dictionary<HexCoord, HexCoord>();
        private readonly Dictionary<HexCoord, RegionData> _cityOwners = new Dictionary<HexCoord, RegionData>();
        private readonly List<(CityData data, HexCoord coord, RegionData region)> _placedCities = new List<(CityData, HexCoord, RegionData)>();
        private readonly List<GameObject> _borderObjects = new List<GameObject>();
        private readonly Dictionary<HexCoord, GameObject> _cityObjects = new Dictionary<HexCoord, GameObject>();
        private readonly Dictionary<HexCoord, GameObject> _districtObjects = new Dictionary<HexCoord, GameObject>();
        private readonly Dictionary<HexCoord, string> _cityLabels = new Dictionary<HexCoord, string>();
        private GameObject _featuresObject;
        private GameObject _roadsObject;
        private readonly Dictionary<string, CityData> _cities = new Dictionary<string, CityData>();
        private readonly Dictionary<HexCoord, float> _heights = new Dictionary<HexCoord, float>();
        private readonly Dictionary<HexCoord, Color> _tileColors = new Dictionary<HexCoord, Color>();
        private readonly Dictionary<string, GameObject> _overlays = new Dictionary<string, GameObject>();
        private readonly List<Transform> _labels = new List<Transform>();
        private readonly List<Object> _generated = new List<Object>();
        private readonly int[] _dirOfEdge = new int[6];
        private MeshCollider _collider;

        // Палитра. Альфа = 1 - гладкость (вода блестит).
        private static readonly Color Ocean = new Color(0.08f, 0.24f, 0.45f, 0.08f);
        private static readonly Color Shallow = new Color(0.17f, 0.47f, 0.60f, 0.1f);
        private static readonly Color Plains = new Color(0.66f, 0.63f, 0.36f);
        private static readonly Color Grassland = new Color(0.35f, 0.55f, 0.23f);
        private static readonly Color Desert = new Color(0.86f, 0.74f, 0.50f);
        private static readonly Color HillsGround = new Color(0.50f, 0.50f, 0.30f);
        private static readonly Color MountainGround = new Color(0.45f, 0.41f, 0.37f);
        private static readonly Color Sand = new Color(0.88f, 0.81f, 0.60f);
        private static readonly Color Dirt = new Color(0.40f, 0.32f, 0.23f);
        private static readonly Color Rock = new Color(0.47f, 0.45f, 0.43f);
        private static readonly Color RockDark = new Color(0.33f, 0.31f, 0.30f);
        private static readonly Color Snow = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Bark = new Color(0.33f, 0.23f, 0.14f);
        private static readonly Color Leaves = new Color(0.16f, 0.36f, 0.14f);
        private static readonly Color LeavesLight = new Color(0.26f, 0.48f, 0.18f);
        private static readonly Color Palm = new Color(0.30f, 0.55f, 0.20f);
        private static readonly Color StoneWall = new Color(0.86f, 0.80f, 0.68f);
        private static readonly Color StoneRoof = new Color(0.93f, 0.89f, 0.80f);
        private static readonly Color Gold = new Color(0.95f, 0.74f, 0.22f);
        private static readonly Color Turquoise = new Color(0.20f, 0.52f, 0.66f);
        private static readonly Color RiverValley = new Color(0.30f, 0.58f, 0.30f);
        private static readonly Color MarshGround = new Color(0.30f, 0.40f, 0.26f);

        private const float WaterLevel = -0.07f;
        private const float MapFloor = -0.35f;

        private void Awake()
        {
            for (int d = 0; d < 6; d++) _dirOfEdge[EdgeCorners(d).Item1] = d;
            if (Application.isPlaying && !IsBuilt) Build();
        }

        private void LateUpdate()
        {
            if (mapCamera == null) return;
            var rot = mapCamera.transform.rotation;
            foreach (var label in _labels)
                if (label != null) label.rotation = rot;
        }

        [ContextMenu("Rebuild")]
        public void Build()
        {
            for (int d = 0; d < 6; d++) _dirOfEdge[EdgeCorners(d).Item1] = d;
            Clear();
            Grid = new HexGrid(mapRadius, seed);
            if (mapCamera == null) mapCamera = Camera.main;
            if (applyAtmosphere) ApplyAtmosphere();

            PlaceRegionTerritory();
            ComputeHeightsAndColors();
            BuildTerrain();
            BuildFeatures();
            BuildBorders();
            BuildCities();
        }

        // ---------- Публичное API для игровой логики ----------

        public Vector3 SurfacePosition(HexCoord c)
        {
            var p = HexMetrics.ToWorld(c, hexSize);
            p.y = _heights.TryGetValue(c, out var h) ? Mathf.Max(h, WaterLevel * hexSize) : 0f;
            return transform.TransformPoint(p);
        }

        public bool RaycastHex(Ray ray, out HexCoord coord)
        {
            coord = default;
            if (_collider == null || !_collider.Raycast(ray, out var hit, 1000f)) return false;
            coord = WorldToHex(transform.InverseTransformPoint(hit.point));
            return Grid.TryGetTile(coord, out _);
        }

        public RegionData OwnerOf(HexCoord c) => _owners.TryGetValue(c, out var r) ? r : null;

        /// <summary>Цвет клетки на карте (для мини-карты).</summary>
        public Color TileColor(HexCoord c) => _tileColors.TryGetValue(c, out var col) ? col : Color.black;

        /// <summary>Города, расставленные при построении карты, с исходными владельцами.</summary>
        public IReadOnlyList<(CityData data, HexCoord coord, RegionData region)> PlacedCities => _placedCities;

        /// <summary>Клетки территории города (как они были розданы при построении карты).</summary>
        public IEnumerable<HexCoord> TerritoryOf(HexCoord cityCoord)
        {
            foreach (var kv in _territoryCity)
                if (kv.Value == cityCoord) yield return kv.Key;
        }

        /// <summary>Рынок на клетке: навесы в цветах владельца.</summary>
        public void ShowMarket(HexCoord tile, RegionData owner)
        {
            if (_districtObjects.TryGetValue(tile, out var old) && old != null) SafeDestroy(old);
            var b = new MeshBuilder();
            var rng = TileRandom(tile, 5);
            var paving = new Color(0.80f, 0.74f, 0.62f);
            b.Prism(Vector3.zero, 0.62f * hexSize, 0.02f * hexSize, 6, Shade(paving, 0.85f), paving, Mathf.PI / 6f);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f + (float)rng.NextDouble() * 0.4f;
                var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.36f * hexSize + Vector3.up * 0.02f * hexSize;
                float yaw = -a * Mathf.Rad2Deg + 90f;
                var awning = i % 2 == 0 ? owner.primaryColor : owner.secondaryColor;
                // Прилавок и навес на столбах.
                b.Box(pos, new Vector3(0.2f, 0.07f, 0.12f) * hexSize, yaw, Bark, Shade(Bark, 1.2f));
                var rot = Quaternion.Euler(0f, yaw, 0f);
                foreach (var corner in new[] { new Vector3(-0.09f, 0, -0.055f), new Vector3(0.09f, 0, -0.055f), new Vector3(-0.09f, 0, 0.055f), new Vector3(0.09f, 0, 0.055f) })
                    b.Prism(pos + rot * corner * hexSize, 0.008f * hexSize, 0.16f * hexSize, 4, Bark, Bark);
                var top = pos + Vector3.up * 0.16f * hexSize;
                var hx = rot * new Vector3(0.12f, 0f, 0f) * hexSize;
                var hz = rot * new Vector3(0f, 0f, 0.08f) * hexSize;
                var ridge = Vector3.up * 0.05f * hexSize;
                b.Quad(top - hx - hz, top + hx - hz, top + hx + ridge, top - hx + ridge, awning, Vector3.up - (rot * Vector3.forward));
                b.Quad(top - hx + hz, top + hx + hz, top + hx + ridge, top - hx + ridge, Shade(awning, 0.85f), Vector3.up + (rot * Vector3.forward));
            }
            // Колодец в центре площади.
            b.Prism(Vector3.up * 0.02f * hexSize, 0.08f * hexSize, 0.06f * hexSize, 8, StoneWall, new Color(0.2f, 0.45f, 0.6f, 0.1f));
            var go = CreateMeshObject($"Market_{tile.q}_{tile.r}", b.ToMesh("Market"), MakeMaterial(Color.white));
            go.transform.localPosition = LocalCenter(tile);
            _districtObjects[tile] = go;
            RebuildFeatures();
        }

        /// <summary>
        /// Синхронизация города с правилами игры: владелец, территория, подпись.
        /// Если города на карте ещё нет (основан поселенцем) — он появляется.
        /// </summary>
        public void UpdateCity(HexCoord cityCoord, CityData data, RegionData owner, IEnumerable<HexCoord> territory, string label)
        {
            var tile = Grid.GetTile(cityCoord);
            bool isNew = !_cities.ContainsKey(data.id);
            tile.CityId = data.id;
            _cities[data.id] = data;
            _cityOwners[cityCoord] = owner;
            _cityLabels[cityCoord] = label;

            foreach (var c in _territoryCity.Where(kv => kv.Value == cityCoord).Select(kv => kv.Key).ToList())
            {
                _territoryCity.Remove(c);
                _owners.Remove(c);
            }
            foreach (var c in territory)
            {
                _territoryCity[c] = cityCoord;
                _owners[c] = owner;
            }
            BuildBorders();
            BuildCity(tile);
            if (isNew) RebuildFeatures();
        }

        // ---------- Туман войны ----------

        /// <summary>Состояние клетки для зрителя: 0 — не разведана, 1 — разведана, 2 — видна сейчас.</summary>
        private System.Func<HexCoord, int> _fog;
        private GameObject _fogClouds, _fogDim;

        public int FogOf(HexCoord c) => _fog == null ? 2 : _fog(c);

        /// <summary>
        /// Перерисовать туман: над неразведанным — облака (выше холмов, пики гор торчат),
        /// разведанное вне обзора — затемнено. Города под облаками скрыты, границы видны только на разведанном.
        /// </summary>
        public void SetFog(System.Func<HexCoord, int> fog)
        {
            _fog = fog;
            if (_fogClouds != null) SafeDestroy(_fogClouds);
            if (_fogDim != null) SafeDestroy(_fogDim);

            var clouds = new MeshBuilder();
            var dim = new MeshBuilder();
            foreach (var tile in Grid.Tiles)
            {
                int state = fog(tile.Coord);
                var center = HexMetrics.ToWorld(tile.Coord, hexSize);
                if (state == 0)
                {
                    var rng = TileRandom(tile.Coord, 9);
                    float v = 0.6f + (float)rng.NextDouble() * 0.07f;
                    var top = new Color(v, v + 0.02f, v + 0.06f);
                    float h = (0.46f + (float)rng.NextDouble() * 0.08f) * hexSize;
                    clouds.Prism(center + Vector3.down * 0.4f * hexSize, hexSize * 1.02f, h + 0.4f * hexSize, 6, Shade(top, 0.8f), top, -Mathf.PI / 6f);
                }
                else if (state == 1)
                {
                    var c = LocalCenter(tile.Coord);
                    c.y = Mathf.Max(c.y, WaterLevel * hexSize) + 0.03f * hexSize;
                    var shade = new Color(0.02f, 0.03f, 0.06f, 0.45f);
                    for (int i = 0; i < 6; i++)
                        dim.Triangle(c, c + HexMetrics.Corner(i, hexSize), c + HexMetrics.Corner((i + 1) % 6, hexSize), shade, Vector3.up);
                }
            }
            _fogClouds = CreateMeshObject("FogClouds", clouds.ToMesh("FogClouds"), MakeMaterial(Color.white));
            var fogMat = fogMaterial != null ? new Material(fogMaterial) : new Material(Shader.Find("Runeterra/FogOverlay"));
            _generated.Add(fogMat);
            _fogDim = CreateMeshObject("FogDim", dim.ToMesh("FogDim"), fogMat);
            _fogDim.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            foreach (var kv in _cityObjects)
                if (kv.Value != null) kv.Value.SetActive(fog(kv.Key) > 0);
            foreach (var kv in _districtObjects)
                if (kv.Value != null) kv.Value.SetActive(fog(kv.Key) > 0);
            BuildBorders();
        }

        /// <summary>Дороги: грунтовые полосы от центра клетки к соседним дорогам и городам.</summary>
        public void RebuildRoads()
        {
            if (_roadsObject != null) SafeDestroy(_roadsObject);
            var b = new MeshBuilder();
            var dirt = new Color(0.62f, 0.52f, 0.36f);
            float w = 0.09f * hexSize;
            foreach (var tile in Grid.Tiles)
            {
                if (!tile.HasRoad) continue;
                var center = LocalCenter(tile.Coord) + Vector3.up * 0.014f * hexSize;
                b.Prism(center - Vector3.up * 0.005f * hexSize, w, 0.006f * hexSize, 6, dirt, dirt);
                for (int d = 0; d < 6; d++)
                {
                    if (!Grid.TryGetTile(tile.Coord.Neighbor(d), out var n) || (!n.HasRoad && n.CityId == null)) continue;
                    var other = LocalCenter(n.Coord) + Vector3.up * 0.014f * hexSize;
                    var mid = (center + other) * 0.5f;
                    var side = Vector3.Cross(Vector3.up, (other - center).normalized) * w;
                    b.Quad(center - side, center + side, mid + side, mid - side, dirt, Vector3.up);
                    // Со стороны города дорогу доводим до его центра.
                    if (n.CityId != null) b.Quad(mid - side, mid + side, other + side, other - side, dirt, Vector3.up);
                }
            }
            _roadsObject = CreateMeshObject("Roads", b.ToMesh("Roads"), MakeMaterial(Color.white));
            _roadsObject.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Порт: деревянный причал в сторону воды и лодка.</summary>
        public void ShowPort(HexCoord tile, RegionData owner)
        {
            if (_districtObjects.TryGetValue(tile, out var old) && old != null) SafeDestroy(old);
            var b = new MeshBuilder();
            var plank = new Color(0.55f, 0.40f, 0.25f);
            HexCoord water = tile;
            for (int d = 0; d < 6; d++)
                if (Grid.TryGetTile(tile.Neighbor(d), out var n) && n.Terrain.IsWater()) { water = n.Coord; break; }
            var dir = (HexMetrics.ToWorld(water, hexSize) - HexMetrics.ToWorld(tile, hexSize)).normalized;
            float yaw = -Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            var dock = dir * 0.55f * hexSize;
            b.Box(dock + Vector3.up * 0.02f * hexSize, new Vector3(0.75f, 0.03f, 0.18f) * hexSize, yaw, plank, Shade(plank, 1.15f));
            foreach (float t in new[] { 0.2f, 0.55f, 0.9f })
            foreach (float side in new[] { -0.08f, 0.08f })
            {
                var p = dir * t * hexSize + Vector3.Cross(Vector3.up, dir) * side * hexSize;
                b.Prism(p - Vector3.up * 0.12f * hexSize, 0.015f * hexSize, 0.16f * hexSize, 5, Shade(plank, 0.7f), plank);
            }
            // Лодка у причала с парусом в цвете владельца.
            var boat = dir * 0.95f * hexSize + Vector3.Cross(Vector3.up, dir) * 0.22f * hexSize - Vector3.up * 0.05f * hexSize;
            b.Box(boat, new Vector3(0.36f, 0.06f, 0.12f) * hexSize, yaw, plank, Shade(plank, 1.2f));
            b.Prism(boat, 0.01f * hexSize, 0.32f * hexSize, 4, Bark, Bark);
            var up = Vector3.up * 0.3f * hexSize;
            b.Triangle(boat + up, boat + Vector3.up * 0.08f * hexSize, boat + Vector3.up * 0.08f * hexSize + dir * 0.18f * hexSize,
                owner.primaryColor, Vector3.Cross(dir, Vector3.up));
            b.Triangle(boat + up, boat + Vector3.up * 0.08f * hexSize + dir * 0.18f * hexSize, boat + Vector3.up * 0.08f * hexSize,
                Shade(owner.primaryColor, 0.9f), -Vector3.Cross(dir, Vector3.up));
            var go = CreateMeshObject($"Port_{tile.q}_{tile.r}", b.ToMesh("Port"), MakeMaterial(Color.white));
            go.transform.localPosition = LocalCenter(tile);
            _districtObjects[tile] = go;
            RebuildFeatures();
        }

        /// <summary>Смена владельца города: его территория и флаг перекрашиваются.</summary>
        public void SetCityOwner(HexCoord cityCoord, RegionData region)
        {
            _cityOwners[cityCoord] = region;
            foreach (var kv in _territoryCity)
                if (kv.Value == cityCoord) _owners[kv.Key] = region;
            BuildBorders();
            BuildCity(Grid.GetTile(cityCoord));
        }

        public CityData CityAt(HexCoord c)
        {
            var tile = Grid.GetTile(c);
            return tile?.CityId != null && _cities.TryGetValue(tile.CityId, out var city) ? city : null;
        }

        /// <summary>Подсветка набора клеток кольцом заданного цвета. Пустой набор — убрать слой.</summary>
        public void SetRingOverlay(string layer, IEnumerable<HexCoord> coords, Color color, float inner = 0.72f, float outer = 0.9f)
        {
            var b = new MeshBuilder();
            foreach (var c in coords)
            {
                var center = LocalSurface(c) + Vector3.up * 0.02f * hexSize;
                for (int i = 0; i < 6; i++)
                {
                    var i0 = center + HexMetrics.Corner(i, hexSize * inner);
                    var i1 = center + HexMetrics.Corner((i + 1) % 6, hexSize * inner);
                    var o0 = center + HexMetrics.Corner(i, hexSize * outer);
                    var o1 = center + HexMetrics.Corner((i + 1) % 6, hexSize * outer);
                    b.Quad(i0, i1, o1, o0, color, Vector3.up);
                }
            }
            SetOverlayMesh(layer, b);
        }

        /// <summary>Маркеры пути: точки на клетках, цвет на каждую.</summary>
        public void SetPathOverlay(string layer, IList<(HexCoord coord, Color color)> points)
        {
            var b = new MeshBuilder();
            foreach (var (c, color) in points)
                b.Prism(LocalSurface(c) + Vector3.up * 0.02f * hexSize, 0.14f * hexSize, 0.04f * hexSize, 8, color, color);
            SetOverlayMesh(layer, b);
        }

        public void ClearOverlay(string layer)
        {
            if (_overlays.TryGetValue(layer, out var go) && go != null) go.SetActive(false);
        }

        // ---------- Атмосфера ----------

        private void ApplyAtmosphere()
        {
            var fog = new Color(0.62f, 0.72f, 0.80f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = 25f * hexSize;
            RenderSettings.fogEndDistance = 70f * hexSize;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.66f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.47f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.23f, 0.20f);
            QualitySettings.shadowDistance = 60f * hexSize;

            if (mapCamera != null)
            {
                mapCamera.clearFlags = CameraClearFlags.SolidColor;
                mapCamera.backgroundColor = fog;
            }

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                light.color = new Color(1f, 0.95f, 0.86f);
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.75f;
                break;
            }
        }

        // ---------- Регионы ----------

        private void PlaceRegionTerritory()
        {
            HexCoord? firstCapital = null;
            var capitals = new List<HexCoord>();
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                if (region == null) continue;
                HexCoord start;
                if (i < regionStarts.Count) start = regionStarts[i];
                else if (i == 0 || firstCapital == null) start = NearestLand(default);
                else start = FarthestReachableLand(firstCapital.Value);
                if (i == 0) firstCapital = start;

                foreach (var city in region.cities)
                {
                    if (city == null) continue;
                    // Исторический город ставится по координатам (на ближайшую сушу), остальные — по смещению.
                    var coord = city.HasGeo ? NearestLand(Grid.FromLonLat(city.lon, city.lat)) : start + city.startOffset;
                    if (!Grid.TryGetTile(coord, out var tile)) continue;

                    foreach (var t in TilesInRange(coord, cityTerritoryRadius))
                    {
                        // Окрестности «придуманного» города — всегда пригодная суша; у исторических берег и горы как есть.
                        if (!city.HasGeo && !t.Terrain.IsPassable())
                        {
                            t.Terrain = TerrainType.Grassland;
                            t.Feature = TileFeature.None;
                        }
                        if (!_owners.ContainsKey(t.Coord))
                        {
                            _owners[t.Coord] = region;
                            _territoryCity[t.Coord] = coord;
                        }
                    }
                    if (tile.Terrain == TerrainType.Hills || tile.Terrain == TerrainType.Desert ||
                        tile.Terrain == TerrainType.Mountains || tile.Terrain == TerrainType.Marsh) tile.Terrain = TerrainType.Plains;
                    tile.Feature = TileFeature.None;
                    tile.Resource = null;
                    tile.CityId = city.id;
                    _cities[city.id] = city;
                    _cityOwners[coord] = region;
                    _placedCities.Add((city, coord, region));
                    if (city.isCapital) capitals.Add(coord);

                    // Региональные ресурсы страны: у каждого города гарантированы месторождения.
                    foreach (var good in region.specialties)
                        if (good != null) Grid.EnsureResource(coord, good.id, city.isCapital ? 2 : 1);
                }
            }
            // Все столицы связаны по суше — войска и караваны могут дойти.
            Grid.EnsureLandConnected(capitals);
        }

        // ---------- Месторождения ----------

        private GameObject _depositsObject;

        /// <summary>Значки месторождений региональных ресурсов: цветной самоцвет на клетке.</summary>
        public void ShowDeposits(IEnumerable<Runeterra.Economy.GoodData> goods)
        {
            if (_depositsObject != null) SafeDestroy(_depositsObject);
            var colors = goods.Where(g => g != null && g.fromDeposit).ToDictionary(g => g.id, g => g.depositColor);
            var b = new MeshBuilder();
            foreach (var tile in Grid.Tiles)
            {
                if (tile.Resource == null || tile.CityId != null || !colors.TryGetValue(tile.Resource, out var col)) continue;
                var pos = LocalCenter(tile.Coord) + new Vector3(0.38f, 0f, -0.3f) * hexSize;
                b.Prism(pos, 0.12f * hexSize, 0.03f * hexSize, 6, Shade(StoneWall, 0.7f), StoneWall);
                b.Cone(pos + Vector3.up * 0.03f * hexSize, 0.08f * hexSize, 0.14f * hexSize, 4, Shade(col, 0.7f), col);
            }
            _depositsObject = CreateMeshObject("Deposits", b.ToMesh("Deposits"), MakeMaterial(Color.white));
            _depositsObject.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private HexCoord NearestLand(HexCoord from)
        {
            HexTile best = null;
            int bestDist = int.MaxValue;
            foreach (var t in Grid.Tiles)
            {
                if (!t.Terrain.IsPassable()) continue;
                int d = t.Coord.DistanceTo(from);
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best?.Coord ?? from;
        }

        /// <summary>
        /// Самая удалённая от from клетка суши, до которой можно дойти по суше
        /// (чтобы соперники могли встретиться). Не у самого края карты.
        /// </summary>
        private HexCoord FarthestReachableLand(HexCoord from)
        {
            var seen = new HashSet<HexCoord> { from };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(from);
            HexCoord best = from;
            int bestDist = -1;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int dist = c.DistanceTo(from);
                if (dist > bestDist && !Grid.IsEdge(c)) { bestDist = dist; best = c; }
                foreach (var n in Grid.Neighbors(c))
                    if (n.Terrain.IsPassable() && seen.Add(n.Coord)) queue.Enqueue(n.Coord);
            }
            return best;
        }

        private IEnumerable<HexTile> TilesInRange(HexCoord center, int range)
        {
            for (int dq = -range; dq <= range; dq++)
            for (int dr = Mathf.Max(-range, -dq - range); dr <= Mathf.Min(range, -dq + range); dr++)
                if (Grid.TryGetTile(center + new HexCoord(dq, dr), out var t))
                    yield return t;
        }

        // ---------- Рельеф ----------

        private void ComputeHeightsAndColors()
        {
            foreach (var tile in Grid.Tiles)
            {
                var rng = TileRandom(tile.Coord, 1);
                float jitter = (float)rng.NextDouble();
                float h = tile.Terrain switch
                {
                    TerrainType.Ocean or TerrainType.Coast => WaterLevel,
                    TerrainType.Hills => 0.13f + jitter * 0.03f,
                    TerrainType.Mountains => 0.18f + jitter * 0.03f,
                    _ => 0.01f + jitter * 0.03f,
                };
                _heights[tile.Coord] = h * hexSize;

                Color c = tile.Terrain switch
                {
                    TerrainType.Ocean => Ocean,
                    TerrainType.Coast => Shallow,
                    TerrainType.Plains => Plains,
                    TerrainType.Grassland => Grassland,
                    TerrainType.Desert => Desert,
                    TerrainType.Hills => Color.Lerp(HillsGround, Grassland, tile.Moisture * 0.6f),
                    TerrainType.River => RiverValley,
                    TerrainType.Marsh => MarshGround,
                    _ => MountainGround,
                };
                if (!tile.Terrain.IsWater())
                {
                    float v = 0.92f + jitter * 0.14f;
                    c = new Color(c.r * v, c.g * v, c.b * v, 1f);
                    if (tile.Feature == TileFeature.Forest) c = Color.Lerp(c, Leaves, 0.35f);
                }
                _tileColors[tile.Coord] = c;
            }
        }

        private Color CornerColor(HexTile tile, int corner)
        {
            var own = _tileColors[tile.Coord];
            bool water = tile.Terrain.IsWater();
            var sum = own;
            int count = 1;
            bool touchesOther = false;
            foreach (int dir in new[] { _dirOfEdge[corner], _dirOfEdge[(corner + 5) % 6] })
            {
                if (!Grid.TryGetTile(tile.Coord.Neighbor(dir), out var n)) continue;
                if (n.Terrain.IsWater() != water) { touchesOther = true; continue; }
                sum += _tileColors[n.Coord];
                count++;
            }
            var blended = sum / count;
            if (touchesOther) blended = water ? Color.Lerp(blended, Shallow * 1.25f, 0.6f) : Color.Lerp(blended, Sand, 0.75f);
            blended.a = own.a;
            return blended;
        }

        private void BuildTerrain()
        {
            var b = new MeshBuilder();
            float gridFactor = showGrid ? 0.84f : 1f;
            foreach (var tile in Grid.Tiles)
            {
                var center = LocalCenter(tile.Coord);
                var own = _tileColors[tile.Coord];
                bool water = tile.Terrain.IsWater();
                var corners = new Color[6];
                for (int i = 0; i < 6; i++) corners[i] = CornerColor(tile, i);

                for (int i = 0; i < 6; i++)
                {
                    int j = (i + 1) % 6;
                    var a0 = center + HexMetrics.Corner(i, hexSize * 0.55f);
                    var a1 = center + HexMetrics.Corner(j, hexSize * 0.55f);
                    var m0 = center + HexMetrics.Corner(i, hexSize * 0.95f);
                    var m1 = center + HexMetrics.Corner(j, hexSize * 0.95f);
                    var o0 = center + HexMetrics.Corner(i, hexSize);
                    var o1 = center + HexMetrics.Corner(j, hexSize);
                    var cm0 = Color.Lerp(own, corners[i], 0.85f);
                    var cm1 = Color.Lerp(own, corners[j], 0.85f);
                    float g = water ? Mathf.Lerp(gridFactor, 1f, 0.5f) : gridFactor;
                    var co0 = Shade(corners[i], g);
                    var co1 = Shade(corners[j], g);

                    b.Triangle(center, a0, a1, own, Vector3.up);
                    b.Quad(a0, a1, m1, m0, own, own, cm1, cm0, Vector3.up);
                    b.Quad(m0, m1, o1, o0, co0, co1, co1, co0, Vector3.up);
                }

                // Уступы к более низким соседям и юбка по краю карты.
                float h = _heights[tile.Coord];
                for (int dir = 0; dir < 6; dir++)
                {
                    var nc = tile.Coord.Neighbor(dir);
                    float nh = _heights.TryGetValue(nc, out var v) ? v : MapFloor * hexSize;
                    if (nh >= h - 0.0001f) continue;
                    var (ci, cj) = EdgeCorners(dir);
                    var top0 = center + HexMetrics.Corner(ci, hexSize);
                    var top1 = center + HexMetrics.Corner(cj, hexSize);
                    var bot0 = new Vector3(top0.x, nh, top0.z);
                    var bot1 = new Vector3(top1.x, nh, top1.z);
                    bool toWater = Grid.TryGetTile(nc, out var n) && n.Terrain.IsWater();
                    Color wallTop, wallBottom;
                    if (water) { wallTop = Shade(own, 0.6f); wallBottom = Shade(own, 0.35f); }
                    else if (toWater) { wallTop = Sand; wallBottom = Shade(Sand, 0.7f); }
                    else { wallTop = Color.Lerp(own, Dirt, 0.6f); wallBottom = Shade(Dirt, 0.8f); }
                    var outward = HexMetrics.ToWorld(HexCoord.Directions[dir], 1f);
                    b.Quad(top0, top1, bot1, bot0, wallTop, wallTop, wallBottom, wallBottom, outward);
                }
            }

            var mesh = b.ToMesh("HexTerrain");
            var go = CreateMeshObject("Terrain", mesh, MakeMaterial(Color.white));
            _collider = go.AddComponent<MeshCollider>();
            _collider.sharedMesh = mesh;
        }

        // ---------- Горы, холмы, леса ----------

        /// <summary>Перестроить декор (после вырубки леса под район и т.п.).</summary>
        public void RebuildFeatures()
        {
            if (_featuresObject != null) SafeDestroy(_featuresObject);
            BuildFeatures();
        }

        private void BuildFeatures()
        {
            var b = new MeshBuilder();
            foreach (var tile in Grid.Tiles)
            {
                if (tile.CityId != null || _districtObjects.ContainsKey(tile.Coord)) continue;
                var center = LocalCenter(tile.Coord);
                var rng = TileRandom(tile.Coord, 2);

                if (tile.Terrain == TerrainType.Mountains) AddMountain(b, center, rng, tile.Elevation);
                else if (tile.Terrain == TerrainType.Hills) AddHills(b, center, rng, _tileColors[tile.Coord]);

                if (tile.Feature == TileFeature.Forest) AddForest(b, center, rng, tile.Terrain == TerrainType.Hills);
                else if (tile.Feature == TileFeature.Oasis) AddOasis(b, center, rng);
            }
            _featuresObject = CreateMeshObject("Features", b.ToMesh("HexFeatures"), MakeMaterial(Color.white));
        }

        private void AddMountain(MeshBuilder b, Vector3 center, System.Random rng, float elevation)
        {
            int peaks = 1 + rng.Next(3);
            for (int p = 0; p < peaks; p++)
            {
                bool main = p == 0;
                var offset = main ? Jitter(rng, 0.12f) : Jitter(rng, 0.45f);
                float radius = hexSize * (main ? 0.62f : 0.32f + (float)rng.NextDouble() * 0.12f);
                float height = hexSize * (main ? 0.85f + (elevation - 0.84f) * 3f : 0.4f + (float)rng.NextDouble() * 0.2f);
                int sides = 6 + rng.Next(3);
                float rot = (float)rng.NextDouble() * Mathf.PI;
                var basePos = center + offset;
                var apex = Jitter(rng, 0.08f);
                // Скала снизу, снег сверху.
                float snowAt = height > 0.7f * hexSize ? 0.62f : 0.8f;
                b.Cone(basePos, radius, height * snowAt, sides, RockDark, Rock, radius * (1f - snowAt), rot, apex * snowAt);
                b.Cone(basePos + Vector3.up * height * snowAt + apex * snowAt, radius * (1f - snowAt), height * (1f - snowAt),
                    sides, height > 0.7f * hexSize ? Snow : Rock, height > 0.7f * hexSize ? Snow : Rock, 0f, rot, apex * (1f - snowAt));
            }
        }

        private void AddHills(MeshBuilder b, Vector3 center, System.Random rng, Color ground)
        {
            int count = 2 + rng.Next(2);
            for (int i = 0; i < count; i++)
            {
                var pos = center + Jitter(rng, 0.45f);
                float r = hexSize * (0.28f + (float)rng.NextDouble() * 0.12f);
                float h = hexSize * (0.12f + (float)rng.NextDouble() * 0.08f);
                b.Cone(pos, r, h * 0.6f, 7, Shade(ground, 0.85f), ground, r * 0.55f, (float)rng.NextDouble() * 3f);
                b.Cone(pos + Vector3.up * h * 0.6f, r * 0.55f, h * 0.4f, 7, ground, Shade(ground, 1.08f), 0f, (float)rng.NextDouble() * 3f);
            }
        }

        private void AddForest(MeshBuilder b, Vector3 center, System.Random rng, bool sparse)
        {
            int count = sparse ? 3 + rng.Next(2) : 5 + rng.Next(3);
            for (int i = 0; i < count; i++)
            {
                var pos = center + Jitter(rng, 0.62f);
                float s = hexSize * (0.8f + (float)rng.NextDouble() * 0.5f);
                var leaves = Color.Lerp(Leaves, LeavesLight, (float)rng.NextDouble());
                b.Prism(pos, 0.035f * s, 0.1f * s, 4, Bark, Bark);
                b.Cone(pos + Vector3.up * 0.08f * s, 0.17f * s, 0.24f * s, 6, Shade(leaves, 0.8f), leaves, 0f, (float)rng.NextDouble() * 3f);
                b.Cone(pos + Vector3.up * 0.2f * s, 0.12f * s, 0.2f * s, 6, Shade(leaves, 0.9f), Shade(leaves, 1.15f), 0f, (float)rng.NextDouble() * 3f);
            }
        }

        private void AddOasis(MeshBuilder b, Vector3 center, System.Random rng)
        {
            var water = new Color(0.20f, 0.52f, 0.65f, 0.1f);
            b.Prism(center + Vector3.up * 0.005f, 0.3f * hexSize, 0.01f * hexSize, 10, water, water);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + (float)rng.NextDouble();
                var pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.42f * hexSize;
                b.Prism(pos, 0.025f * hexSize, 0.3f * hexSize, 4, Bark, Bark);
                b.Cone(pos + Vector3.up * 0.3f * hexSize, 0.16f * hexSize, 0.05f * hexSize, 6, Palm, Shade(Palm, 1.2f));
            }
        }

        // ---------- Границы ----------

        private void BuildBorders()
        {
            foreach (var go in _borderObjects)
                if (go != null) SafeDestroy(go);
            _borderObjects.Clear();

            var byRegion = new Dictionary<RegionData, List<HexCoord>>();
            foreach (var kv in _owners)
            {
                if (!byRegion.TryGetValue(kv.Value, out var list)) byRegion[kv.Value] = list = new List<HexCoord>();
                list.Add(kv.Key);
            }

            float outer = hexSize * 0.97f, inner = hexSize * 0.84f;
            foreach (var kv in byRegion)
            {
                var b = new MeshBuilder();
                var color = kv.Key.secondaryColor;
                var fade = Shade(color, 0.75f);
                foreach (var coord in kv.Value)
                {
                    if (_fog != null && _fog(coord) == 0) continue;
                    var center = LocalCenter(coord) + Vector3.up * 0.012f * hexSize;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        var n = coord.Neighbor(dir);
                        if (_owners.TryGetValue(n, out var owner) && owner == kv.Key) continue;
                        var (a, c) = EdgeCorners(dir);
                        b.Quad(center + HexMetrics.Corner(a, outer), center + HexMetrics.Corner(c, outer),
                            center + HexMetrics.Corner(c, inner), center + HexMetrics.Corner(a, inner),
                            color, color, fade, fade, Vector3.up);
                    }
                }
                _borderObjects.Add(CreateMeshObject($"Border_{kv.Key.id}", b.ToMesh($"Border_{kv.Key.id}"), MakeMaterial(Color.white, color * 0.5f)));
            }
        }

        // ---------- Города ----------

        private void BuildCities()
        {
            foreach (var tile in Grid.Tiles)
                if (tile.CityId != null) BuildCity(tile);
        }

        private void BuildCity(HexTile tile)
        {
            if (_cityObjects.TryGetValue(tile.Coord, out var old) && old != null) SafeDestroy(old);
            var city = _cities[tile.CityId];
            var region = _cityOwners[tile.Coord];
            var rng = TileRandom(tile.Coord, 3);
            var b = new MeshBuilder();

            // Плотная застройка: светлый камень, плоские крыши.
            int houses = city.isCapital ? 11 : 7;
            for (int i = 0; i < houses; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = (city.isCapital ? 0.32f : 0.12f) + (float)rng.NextDouble() * 0.4f;
                var pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist * hexSize;
                var size = new Vector3(0.14f + (float)rng.NextDouble() * 0.1f, 0.1f + (float)rng.NextDouble() * 0.14f,
                    0.14f + (float)rng.NextDouble() * 0.1f) * hexSize;
                float tint = 0.92f + (float)rng.NextDouble() * 0.1f;
                b.Box(pos, size, (float)rng.NextDouble() * 90f, Shade(StoneWall, tint), Shade(StoneRoof, tint));
            }

            if (city.isCapital)
            {
                // Восьмигранное здание с золотым куполом.
                b.Prism(Vector3.zero, 0.24f * hexSize, 0.16f * hexSize, 8, Turquoise, StoneRoof, Mathf.PI / 8f);
                b.Prism(Vector3.up * 0.16f * hexSize, 0.13f * hexSize, 0.05f * hexSize, 12, Turquoise, Turquoise);
                b.Dome(Vector3.up * 0.21f * hexSize, 0.13f * hexSize, 14, 5, Gold, Shade(Gold, 1.1f));
            }
            else
            {
                b.Prism(Vector3.zero, 0.07f * hexSize, 0.34f * hexSize, 6, StoneWall, StoneRoof);
            }

            // Городская стена с зубцами и воротами.
            int segments = 18;
            for (int i = 0; i < segments; i++)
            {
                if (i == segments / 4) continue; // ворота
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.7f * hexSize;
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.7f * hexSize;
                var mid = (p0 + p1) * 0.5f;
                float yaw = -Mathf.Atan2(p1.z - p0.z, p1.x - p0.x) * Mathf.Rad2Deg;
                b.Box(mid, new Vector3((p1 - p0).magnitude + 0.01f * hexSize, 0.09f * hexSize, 0.05f * hexSize), yaw, Shade(StoneWall, 0.82f), Shade(StoneRoof, 0.9f));
                b.Box(mid + Vector3.up * 0.09f * hexSize, new Vector3(0.05f, 0.035f, 0.05f) * hexSize, yaw, Shade(StoneWall, 0.82f), Shade(StoneRoof, 0.9f));
                if (i % 3 == 0) b.Prism(p0, 0.045f * hexSize, 0.15f * hexSize, 6, Shade(StoneWall, 0.78f), Shade(StoneRoof, 0.85f));
            }

            // Флаг региона.
            var pole = new Vector3(0.36f, 0f, 0.22f) * hexSize;
            b.Prism(pole, 0.012f * hexSize, 0.55f * hexSize, 4, Bark, Bark);
            var f0 = pole + Vector3.up * 0.55f * hexSize;
            var f1 = f0 + Vector3.right * 0.24f * hexSize;
            var f2 = f1 + Vector3.down * 0.15f * hexSize;
            var f3 = f0 + Vector3.down * 0.15f * hexSize;
            b.Quad(f0, f1, f2, f3, region.primaryColor, Vector3.back);
            b.Quad(f0, f1, f2, f3, region.primaryColor, Vector3.forward);
            b.Triangle(f0, f0 + Vector3.right * 0.09f * hexSize + Vector3.down * 0.075f * hexSize, f3,
                region.secondaryColor, Vector3.back);

            var go = CreateMeshObject($"City_{city.id}", b.ToMesh($"City_{city.id}"), MakeMaterial(Color.white));
            go.transform.localPosition = LocalCenter(tile.Coord);
            _cityObjects[tile.Coord] = go;
            if (_fog != null) go.SetActive(_fog(tile.Coord) > 0);

            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = Vector3.up * 0.95f * hexSize;
            label.text = _cityLabels.TryGetValue(tile.Coord, out var custom) ? custom : city.isCapital ? $"★ {city.displayName}" : city.displayName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.07f * hexSize;
            label.fontSize = 64;
            label.color = Color.white;
            _labels.Add(label.transform);
        }

        // ---------- Утилиты ----------

        private Vector3 LocalCenter(HexCoord c)
        {
            var p = HexMetrics.ToWorld(c, hexSize);
            p.y = _heights.TryGetValue(c, out var h) ? h : 0f;
            return p;
        }

        private Vector3 LocalSurface(HexCoord c) => transform.InverseTransformPoint(SurfacePosition(c));

        private System.Random TileRandom(HexCoord c, int salt) =>
            new System.Random(unchecked(seed * 486187739 + c.q * 73856093 + c.r * 19349663 + salt * 83492791));

        private Vector3 Jitter(System.Random rng, float radius)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float d = Mathf.Sqrt((float)rng.NextDouble()) * radius * hexSize;
            return new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private GameObject CreateMeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            _generated.Add(mesh);
            _generated.Add(go);
            return go;
        }

        private void SetOverlayMesh(string layer, MeshBuilder b)
        {
            if (!_overlays.TryGetValue(layer, out var go) || go == null)
            {
                go = new GameObject($"Overlay_{layer}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = MakeMaterial(Color.white, new Color(0.35f, 0.35f, 0.35f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _overlays[layer] = go;
                _generated.Add(go);
            }
            var filter = go.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null) SafeDestroy(filter.sharedMesh);
            filter.sharedMesh = b.ToMesh($"Overlay_{layer}");
            go.SetActive(b.VertexCount > 0);
        }

        /// <summary>Индексы углов (для HexMetrics.Corner), образующих ребро в сторону HexCoord.Directions[dir].</summary>
        private static (int, int) EdgeCorners(int dir)
        {
            var d = HexMetrics.ToWorld(HexCoord.Directions[dir], 1f);
            float angle = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            int a = (Mathf.RoundToInt(angle / 60f) + 6) % 6;
            return (a, (a + 1) % 6);
        }

        private Material MakeMaterial(Color color, Color emission = default)
        {
            var mat = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Runeterra/HexTerrain"));
            mat.color = color;
            if (emission != default && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
            }
            _generated.Add(mat);
            return mat;
        }

        private HexCoord WorldToHex(Vector3 p)
        {
            float q = (HexMetrics.Sqrt3 / 3f * p.x + 1f / 3f * p.z) / hexSize;
            float r = (-2f / 3f * p.z) / hexSize;
            float s = -q - r;
            int rq = Mathf.RoundToInt(q), rr = Mathf.RoundToInt(r), rs = Mathf.RoundToInt(s);
            float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        private void Clear()
        {
            foreach (var o in _generated)
                if (o != null) SafeDestroy(o);
            _generated.Clear();
            _owners.Clear();
            _territoryCity.Clear();
            _cityOwners.Clear();
            _placedCities.Clear();
            _borderObjects.Clear();
            _cityObjects.Clear();
            _districtObjects.Clear();
            _cityLabels.Clear();
            _featuresObject = null;
            _roadsObject = null;
            _depositsObject = null;
            _fogClouds = null;
            _fogDim = null;
            _cities.Clear();
            _heights.Clear();
            _tileColors.Clear();
            _overlays.Clear();
            _labels.Clear();
            _collider = null;
        }

        private static void SafeDestroy(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}

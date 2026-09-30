using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Map;
using Runeterra.Units;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Связывает карту, состояние партии и интерфейс. Правила — в GameState, ИИ — в AiPlayer.
    /// Ввод: ЛКМ — выбрать юнита или город, ПКМ — идти / атаковать, B — построить рынок,
    /// Tab — следующий юнит, Пробел/Enter — конец хода. Первый регион карты — игрок.
    /// </summary>
    public partial class GameController : MonoBehaviour
    {
        public HexMapView map;
        [Tooltip("Юниты, которые можно купить в городе, в порядке кнопок")]
        public List<UnitData> shop = new List<UnitData>();
        public DistrictData market;
        public DistrictData port;
        public List<GoodData> goods = new List<GoodData>();
        public List<BuildingData> buildings = new List<BuildingData>();
        public List<Runeterra.Tech.TechData> techs = new List<Runeterra.Tech.TechData>();

        public GameState State { get; private set; }
        public TurnManager Turns => State?.Turns;
        public List<City> Cities => State?.Cities;
        public int? Winner => State?.Winner;
        /// <summary>Идут анимации — ввод и передача хода ждут.</summary>
        public bool Busy => UnitView.Animating > 0 || _cityShots > 0;

        private AiPlayer _ai;
        private Minimap _minimap;
        private bool _showUnits;
        private Vector2 _unitScroll;
        private int _human;

        private int FogState(HexCoord c) => State.Vision.IsVisible(_human, c) ? 2 : State.Vision.IsExplored(_human, c) ? 1 : 0;

        private void ApplyFog() => map.SetFog(FogState);

        /// <summary>Цель на клетке, которую игрок видит (в тумане атаковать нельзя).</summary>
        private bool VisibleTarget(Unit unit, HexCoord c) => State.IsTarget(unit, c) && State.Vision.IsVisible(unit.OwnerIndex, c);
        private readonly Dictionary<Caravan, CaravanView> _caravans = new Dictionary<Caravan, CaravanView>();
        private int _cityShots;
        private Unit _selected;
        private City _selectedCity;
        private HexCoord? _hovered;
        private Dictionary<HexCoord, int> _reachable = new Dictionary<HexCoord, int>();
        private List<HexCoord> _previewPath;
        private int _previewTurns;
        private string _lastMessage;

        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.25f);
        private static readonly Color ReachColor = new Color(0.9f, 0.95f, 1f);
        private static readonly Color PathNow = new Color(1f, 0.95f, 0.5f);
        private static readonly Color PathLater = new Color(0.95f, 0.55f, 0.35f);
        private static readonly Color HoverColor = new Color(1f, 1f, 1f);
        private static readonly Color AttackColor = new Color(0.95f, 0.2f, 0.15f);
        private static readonly Color BuildColor = new Color(0.35f, 0.95f, 0.45f);

        private void Start() => Initialize();

        public void Initialize()
        {
            if (State != null) return;
            if (map == null) map = FindFirstObjectByType<HexMapView>();
            var save = SaveSystem.PendingLoad;
            SaveSystem.PendingLoad = null;
            if (save != null && (map.mapRadius != save.mapRadius || map.seed != save.seed))
            {
                map.mapRadius = save.mapRadius;
                map.seed = save.seed;
                map.Build();
            }
            if (!map.IsBuilt) map.Build();

            var players = new List<PlayerState>();
            foreach (var region in map.regions)
                if (region != null)
                    players.Add(new PlayerState(players.Count, region, isHuman: players.Count == 0) { Gold = region.startingGold });

            State = new GameState(map.Grid, players, market, p => _ai.PlayTurn(p), goods, buildings, techs);
            State.SetPort(port);
            State.Shop.AddRange(shop);
            _ai = new AiPlayer(State, shop, market, buildings);
            State.Trade.Dispatched += c => _caravans[c] = CaravanView.Create(c, map, Players[c.OwnerIndex].Region.primaryColor, map.baseMaterial);
            State.Trade.Moved += c => { if (_caravans.TryGetValue(c, out var v)) v.Refresh(); };
            State.Trade.Finished += (c, ok) =>
            {
                if (_caravans.TryGetValue(c, out var v)) v.Finish();
                _caravans.Remove(c);
            };
            State.RoadsChanged += map.RebuildRoads;

            // Туман войны для игрока.
            _human = players.First(p => p.IsHuman).Index;
            State.Vision.Changed += idx => { if (idx == _human) ApplyFog(); };
            UnitView.IsShown = u => u.OwnerIndex == _human || State.Vision.IsVisible(_human, u.Coord);
            CaravanView.IsShown = c => c.OwnerIndex == _human || State.Vision.IsVisible(_human, c.Coord);
            CityWallsBar.IsShown = c => State.Vision.IsExplored(_human, c.Coord);
            State.UnitCreated += u => UnitView.Create(u, map, Players[u.OwnerIndex].Region, map.baseMaterial);
            // Облик города перестраивается раз в кадр: за ход ИИ город может меняться много раз.
            State.CityChanged += c =>
            {
                _dirtyCities.Add(c);
                if (_selectedCity == c && c.OwnerIndex != Turns.Current.Index) _selectedCity = null;
            };
            State.Message += m =>
            {
                _lastMessage = m;
                if (AutoPlayCheck.Active && (m.Contains("войн") || m.Contains("Мир:") || m.Contains("претензи") || m.Contains("пакт") ||
                                             m.Contains("союз") || m.Contains("оалиц") || m.Contains("разрывает") || m.Contains("захватывает") || m.Contains("сходит со сцены")))
                    Debug.Log($"[DIPLO] ход {Turns.Turn}: {m}");
            };
            State.CityShot += (city, target) => StartCoroutine(CityShotAnimation(city, target));
            State.CityFounded += city =>
            {
                OnCityChanged(city);
                CreateWallsBar(city);
            };

            if (save != null)
            {
                InitializeFromSave(save, players);
                return;
            }

            foreach (var placed in map.PlacedCities)
            {
                var owner = players.FirstOrDefault(p => p.Region == placed.region);
                if (owner == null) continue;
                var city = new City(placed.data, placed.coord, owner.Index);
                foreach (var t in map.TerritoryOf(placed.coord)) city.Territory.Add(t);
                if (State.Grain != null) city.Warehouse.Add(State.Grain, 10); // стартовый запас зерна
                State.Cities.Add(city);
            }
            foreach (var city in State.Cities)
            {
                CreateWallsBar(city);
                OnCityChanged(city);
            }
            foreach (var p in players) SpawnStartingUnits(p);
            State.Diplomacy.Setup();
            Turns.PlayerTurnStarted += p => { if (p.IsHuman && Winner == null) SelectNextUnit(); };

            map.ShowDeposits(goods);
            _minimap = new Minimap(State, map, _human);
            FocusCamera();
            Turns.Start();
            State.Vision.RefreshAll();
            ApplyFog();
        }

        private IReadOnlyList<PlayerState> Players => State.Players;

        /// <summary>Продолжение сохранённой партии: состояние из файла, затем карта и туман подтягиваются под него.</summary>
        private void InitializeFromSave(SaveSystem.SaveData save, List<PlayerState> players)
        {
            var units = shop.Concat(players.SelectMany(p => p.Region.startingUnits)).Where(u => u != null).Distinct();
            SaveSystem.Apply(save, State, _ai, new SaveSystem.Content
            {
                Units = units, Goods = goods, Buildings = buildings, Techs = techs,
                Districts = new[] { market, port }.Where(d => d != null),
                Cities = map.PlacedCities.Select(pc => pc.data).Concat(map.regions.Where(r => r != null).SelectMany(r => r.cities)),
            });
            foreach (var city in State.Cities)
            {
                CreateWallsBar(city);
                OnCityChanged(city);
            }
            foreach (var c in State.Trade.Caravans)
            {
                _caravans[c] = CaravanView.Create(c, map, Players[c.OwnerIndex].Region.primaryColor, map.baseMaterial);
                _caravans[c].Snap();
            }
            map.RebuildFeatures();
            map.RebuildRoads();
            map.ShowDeposits(goods);
            Turns.PlayerTurnStarted += p => { if (p.IsHuman && Winner == null) SelectNextUnit(); };

            _minimap = new Minimap(State, map, _human);
            FocusCamera();
            State.Vision.RefreshAll();
            ApplyFog();
            if (Turns.Current.IsHuman) SelectNextUnit();
            _lastMessage = $"Партия загружена: ход {Turns.Turn}";
        }

        // ---------- Сохранение ----------

        /// <summary>Сохранять можно в свой ход, когда нет анимаций.</summary>
        public bool CanSave => State != null && Winner == null && !Busy && Turns.Current.IsHuman;

        public bool SaveGame()
        {
            if (!CanSave) return false;
            try
            {
                SaveSystem.Save(State, _ai);
                _lastMessage = $"Партия сохранена (ход {Turns.Turn})";
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                _lastMessage = $"Не удалось сохранить: {e.Message}";
                return false;
            }
        }

        /// <summary>Загрузить сохранение: сцена партии перезапускается с состоянием из файла.</summary>
        public bool LoadGame()
        {
            var save = SaveSystem.Read();
            if (save == null) { _lastMessage = "Сохранения нет или оно повреждено"; return false; }
            SaveSystem.PendingLoad = save;
            Restart();
            return true;
        }

        /// <summary>Снимок состояния в JSON (для автопроверки сохранения).</summary>
        public string Snapshot() => SaveSystem.ToJson(SaveSystem.Capture(State, _ai));

        private void SpawnStartingUnits(PlayerState player)
        {
            var capital = State.CapitalOf(player);
            if (capital == null) return;
            // Сначала соседние клетки, чтобы юниты не прятались в застройке столицы.
            var spots = Enumerable.Range(0, 6).Select(d => capital.Coord.Neighbor((d + 3) % 6)).Append(capital.Coord);
            foreach (var data in player.Region.startingUnits)
            {
                if (data == null) continue;
                var spot = spots.FirstOrDefault(State.IsFreeLand);
                if (!State.IsFreeLand(spot)) break;
                State.Spawn(data, player, spot);
            }
        }

        private System.Collections.IEnumerator CityShotAnimation(City city, HexCoord target)
        {
            if (!Application.isPlaying) yield break;
            _cityShots++;
            try
            {
                var from = map.SurfacePosition(city.Coord) + Vector3.up * 0.6f * map.hexSize;
                yield return Projectile.Fly(from, map.SurfacePosition(target) + Vector3.up * 0.3f * map.hexSize, map.baseMaterial, map.hexSize);
            }
            finally
            {
                _cityShots--;
            }
        }

        private void CreateWallsBar(City city) =>
            CityWallsBar.Create(city, map.SurfacePosition(city.Coord) + Vector3.up * 1.15f * map.hexSize, map.hexSize, map.mapCamera, map.baseMaterial)
                .transform.SetParent(map.transform, true);

        private static string CityLabel(City city) => (city.IsCapital ? "★ " : "") + $"{city.Data.displayName}  {city.Population}";

        private readonly HashSet<City> _dirtyCities = new HashSet<City>();

        private void LateUpdate()
        {
            if (_dirtyCities.Count == 0) return;
            foreach (var c in _dirtyCities.ToList()) OnCityChanged(c);
            _dirtyCities.Clear();
        }

        private void OnCityChanged(City city)
        {
            var region = Players[city.OwnerIndex].Region;
            map.UpdateCity(city.Coord, city.Data, region, city.Territory, CityLabel(city));
            if (city.MarketCoord != null) map.ShowMarket(city.MarketCoord.Value, region);
            if (city.PortCoord != null) map.ShowPort(city.PortCoord.Value, region);
            if (_selectedCity == city && city.OwnerIndex != Turns.Current.Index) _selectedCity = null;
        }

        private void FocusCamera()
        {
            var human = Players.FirstOrDefault(p => p.IsHuman);
            var capital = human != null ? State.CapitalOf(human) : null;
            var cam = map.mapCamera != null ? map.mapCamera.GetComponent<HexCameraController>() : null;
            if (cam != null && capital != null) cam.FocusOn(map.SurfacePosition(capital.Coord) + new Vector3(0f, 0f, -1.2f * map.hexSize));
        }

        // ---------- Ввод ----------

        private void Update()
        {
            if (State == null || map.mapCamera == null) return;

            _hovered = map.RaycastHex(map.mapCamera.ScreenPointToRay(Input.mousePosition), out var c) && !MouseOverGui()
                ? c
                : (HexCoord?)null;

            // Esc и клик по карте сначала закрывают верхнее открытое окно HUD.
            bool escUsed = Input.GetKeyDown(KeyCode.Escape) && Winner == null && CloseTopWindow();
            if (Input.GetKeyDown(KeyCode.Escape) && !escUsed && Winner == null && _selected == null && _selectedCity == null)
            {
                ShowMenu(true);
                escUsed = true;
            }
            bool clickUsed = Input.GetMouseButtonDown(0) && _hovered != null && Winner == null && CloseTopWindow();

            if (Winner != null || Busy || !Turns.Current.IsHuman)
            {
                RefreshOverlays();
                return;
            }

            if (Input.GetMouseButtonDown(0) && _hovered != null && !clickUsed) Click(_hovered.Value);
            if (Input.GetMouseButtonDown(1) && _selected != null && _hovered != null) Command(_hovered.Value);
            if (Input.GetKeyDown(KeyCode.B) && _selected != null) TryBuildDistrict(market);
            if (Input.GetKeyDown(KeyCode.P) && _selected != null) TryBuildDistrict(port);
            if (Input.GetKeyDown(KeyCode.R) && _selected != null && State.BuildRoad(_selected)) AfterAction();
            if (Input.GetKeyDown(KeyCode.F) && _selected != null) TryFoundCity();
            if (Input.GetKeyDown(KeyCode.Tab)) SelectNextUnit();
            if (Input.GetKeyDown(KeyCode.T)) _showTreasury = !_showTreasury;
            if (Input.GetKeyDown(KeyCode.Y)) _showTech = !_showTech;
            if (Input.GetKeyDown(KeyCode.U)) _showUnits = !_showUnits;
            if (Input.GetKeyDown(KeyCode.G)) _showDiplomacy = !_showDiplomacy;
            if (Input.GetKeyDown(KeyCode.Escape) && !escUsed) { Select(null); _selectedCity = null; }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) EndTurn();

            UpdatePreview();
            RefreshOverlays();
        }

        /// <summary>ЛКМ: свой юнит, затем (повторный клик) свой город на той же клетке.</summary>
        private void Click(HexCoord c)
        {
            var unit = State.UnitAt(c);
            var city = State.CityAt(c);
            bool ownUnit = unit != null && unit.OwnerIndex == Turns.Current.Index;
            bool ownCity = city != null && city.OwnerIndex == Turns.Current.Index;

            if (ownUnit && (_selected != unit || !ownCity))
            {
                Select(unit);
                _selectedCity = null;
            }
            else if (ownCity)
            {
                Select(null);
                _selectedCity = city;
            }
            else
            {
                Select(null);
                _selectedCity = null;
            }
        }

        public void SelectCity(City city)
        {
            Select(null);
            _selectedCity = city;
        }

        public void SetCityTab(int tab) => _cityTab = tab;
        public void ShowTreasury(bool show) => _showTreasury = show;

        public void SelectUnit(Unit unit)
        {
            Select(unit);
            _selectedCity = null;
        }

        private void Select(Unit unit)
        {
            _selected = unit;
            _previewPath = null;
            RecomputeReachable();
        }

        private void SelectNextUnit()
        {
            var units = Turns.Current.Units;
            if (units.Count == 0) { Select(null); return; }
            int start = _selected != null ? units.IndexOf(_selected) + 1 : 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[(start + i) % units.Count];
                if (u.MovesLeft > 0) { Select(u); _selectedCity = null; return; }
            }
            Select(null);
        }

        private void RecomputeReachable() =>
            _reachable = _selected != null ? State.ReachableFor(_selected) : new Dictionary<HexCoord, int>();

        /// <summary>ПКМ: по врагу — атака (с подходом), по клетке — движение.</summary>
        private void Command(HexCoord target)
        {
            if (_selected.MovesLeft <= 0) return;
            if (VisibleTarget(_selected, target))
            {
                if (GameState.CanFight(_selected)) State.AttackTarget(_selected, target);
            }
            else State.MoveUnit(_selected, State.PathFor(_selected, target));
            AfterAction();
        }

        private void TryFoundCity()
        {
            if (_selected == null || State.FoundCity(_selected) == null) return;
            AfterAction();
        }

        private void TryBuildDistrict(DistrictData d)
        {
            if (_selected == null || d == null || !State.BuildDistrict(_selected, d)) return;
            AfterAction();
        }

        private void AfterAction()
        {
            _previewPath = null;
            if (_selected != null && !_selected.IsAlive) Select(null);
            RecomputeReachable();
            if (Winner != null) { Select(null); _selectedCity = null; }
        }

        private void UpdatePreview()
        {
            _previewPath = null;
            if (_selected == null || _hovered == null || _hovered.Value == _selected.Coord) return;
            var target = _hovered.Value;
            if (VisibleTarget(_selected, target))
            {
                bool inRange = _selected.Data.IsRanged && _selected.Coord.DistanceTo(target) <= _selected.Data.range;
                _previewPath = GameState.CanFight(_selected) && !inRange ? State.PathToAdjacent(_selected, target) : null;
                if (_previewPath != null && _selected.Data.IsRanged)
                {
                    int cut = _previewPath.FindIndex(p => p.DistanceTo(target) <= _selected.Data.range);
                    if (cut >= 0) _previewPath = _previewPath.Take(cut + 1).ToList();
                }
            }
            else _previewPath = State.PathFor(_selected, target);
            _previewTurns = _previewPath == null ? 0 : SimulatePath(_selected, _previewPath, out _);
        }

        /// <summary>Сколько ходов займёт путь с учётом оставшихся очков в этом ходу.</summary>
        private int SimulatePath(Unit unit, List<HexCoord> path, out List<int> turnOfStep)
        {
            turnOfStep = new List<int>();
            int turn = 1, moves = unit.MovesLeft;
            if (moves <= 0) { turn = 2; moves = unit.MaxMoves; }
            foreach (var step in path)
            {
                if (moves <= 0) { turn++; moves = unit.MaxMoves; }
                moves = Mathf.Max(0, moves - map.Grid.GetTile(step).MoveCost());
                turnOfStep.Add(turn);
            }
            return turn;
        }

        private (int, HexCoord, int, HexCoord?, int, int) _overlayState;

        private void RefreshOverlays()
        {
            // Перестраиваем меши подсветки только при изменении состояния.
            var state = (_selected?.Id ?? 0, _selected?.Coord ?? default, _selected?.MovesLeft ?? 0, _hovered,
                _previewPath?.Count ?? -1,
                (Busy ? 100000 : 0) + Players.Sum(p => p.Units.Count) * 100 + Cities.Count(ci => ci.HasMarket) + Cities.Count(ci => ci.HasPort) * 7 +
                (_selectedCity != null ? 50 : 0) +
                Cities.Sum(ci => ci.Walls) * 1000000);
            if (state.Equals(_overlayState)) return;
            _overlayState = state;

            if (_selected != null)
            {
                map.SetRingOverlay("selected", new[] { _selected.Coord }, SelectedColor, 0.62f, 0.93f);
                map.SetRingOverlay("reach", _reachable.Keys, ReachColor, 0.8f, 0.9f);
                var targets = Players.Where(p => p.Index != _selected.OwnerIndex).SelectMany(p => p.Units).Select(e => e.Coord)
                    .Concat(Cities.Select(ci => ci.Coord))
                    .Where(t => State.CanAttackNow(_selected, t) && State.Vision.IsVisible(_human, t))
                    .Distinct();
                map.SetRingOverlay("attack", targets, AttackColor, 0.62f, 0.93f);
                var spots = _selected.Data.buildCharges > 0 ? MarketSpots(_selected.OwnerIndex)
                    : _selected.Data.canFoundCity ? FoundSpots(_selected) : Enumerable.Empty<HexCoord>();
                map.SetRingOverlay("build", spots, BuildColor, 0.45f, 0.6f);
            }
            else
            {
                map.ClearOverlay("selected");
                map.ClearOverlay("reach");
                map.ClearOverlay("attack");
                map.ClearOverlay("build");
            }

            if (_selectedCity != null) map.SetRingOverlay("city", new[] { _selectedCity.Coord }, SelectedColor, 0.62f, 0.93f);
            else map.ClearOverlay("city");

            if (_previewPath != null && _selected != null)
            {
                SimulatePath(_selected, _previewPath, out var turns);
                var points = new List<(HexCoord, Color)>();
                for (int i = 0; i < _previewPath.Count; i++) points.Add((_previewPath[i], turns[i] == 1 ? PathNow : PathLater));
                map.SetPathOverlay("path", points);
            }
            else map.ClearOverlay("path");

            if (_hovered != null) map.SetRingOverlay("hover", new[] { _hovered.Value }, HoverColor, 0.9f, 0.96f);
            else map.ClearOverlay("hover");
        }

        /// <summary>Клетки рядом с поселенцем (до 5), где можно основать город.</summary>
        private IEnumerable<HexCoord> FoundSpots(Unit settler) =>
            map.Grid.Tiles.Where(t => t.Coord.DistanceTo(settler.Coord) <= 5 && State.CanFoundCity(settler, t.Coord) == null)
                .Select(t => t.Coord);

        /// <summary>Клетки, где строитель игрока может поставить рынок.</summary>
        private IEnumerable<HexCoord> MarketSpots(int owner) =>
            Cities.Where(c => c.OwnerIndex == owner)
                .SelectMany(c => Enumerable.Range(0, 6).Select(d => c.Coord.Neighbor(d))
                    .Where(t => (!c.HasMarket && State.DistrictTileOk(c, market, t)) || (port != null && !c.HasPort && State.DistrictTileOk(c, port, t))));

        // ---------- Ходы ----------

        /// <summary>Сколько миллисекунд заняли ходы ИИ после последнего «Конца хода» игрока.</summary>
        public long LastAiRoundMs { get; private set; }

        public void EndTurn()
        {
            if (Winner != null) return;
            Select(null);
            _selectedCity = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Turns.EndTurn();
            LastAiRoundMs = sw.ElapsedMilliseconds;
        }

        public void Restart() => UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);

        /// <summary>Сыграть текущий ход игрока логикой ИИ (для автопроверки сборки).</summary>
        public void AutoPlayHumanTurn()
        {
            if (Winner == null && Turns.Current.IsHuman)
            {
                foreach (var p in State.Diplomacy.Proposals.ToList())
                    State.Diplomacy.Answer(p, Turns.Current.Index, _ai.Accepts(Turns.Current.Index, p.From, p.Kind));
                _ai.PlayTurn(Turns.Current);
            }
            AfterAction();
        }
    }
}

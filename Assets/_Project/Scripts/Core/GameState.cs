using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Map;
using Runeterra.Units;

namespace Runeterra.Core
{
    /// <summary>
    /// Состояние партии и все правила: движение, бой, захват, экономика, постройки.
    /// Чистый C# — визуал подписывается на события.
    /// </summary>
    public class GameState
    {
        public const int CityIncome = 2;

        public HexGrid Grid { get; }
        public TurnManager Turns { get; }
        public List<City> Cities { get; } = new List<City>();
        public DistrictData Market { get; }
        public IReadOnlyList<GoodData> Goods { get; }
        public IReadOnlyList<BuildingData> BuildingTypes { get; }
        public GoodData Grain { get; }
        public DistrictData Port { get; private set; }
        public TradeSystem Trade { get; }
        public TechSystem Tech { get; }
        public Visibility Vision { get; }
        public Diplomacy Diplomacy { get; }

        /// <summary>Сценарий и длина партии (после TurnLimit — подсчёт очков).</summary>
        public Scenario Scenario { get; private set; } = Scenario.Historical1187;
        public int TurnLimit { get; private set; } = 200;

        public void Configure(Scenario scenario, int turnLimit)
        {
            Scenario = scenario;
            TurnLimit = turnLimit;
            Diplomacy.WarsDisabled = scenario == Scenario.PeacefulDevelopment;
        }

        /// <summary>
        /// Очки развития: жители ×2, города ×10, узлы развития ×4, эпохи ×10, постройки ×2, районы ×3,
        /// казна и резерв /20, доход от торговли (пошлины) /10.
        /// </summary>
        public int Score(PlayerState p)
        {
            var mine = Cities.Where(c => c.OwnerIndex == p.Index).ToList();
            return mine.Sum(c => 2 * c.Population + 10 + 2 * c.Buildings.Values.Sum() + (c.HasMarket ? 3 : 0) + (c.HasPort ? 3 : 0)) +
                   4 * p.Techs.Count + 10 * p.Epochs.Count + (Math.Max(0, p.Gold) + p.Reserve) / 20 + p.TradeIncomeTotal / 10;
        }

        /// <summary>Конец партии по сроку: побеждает сторона с наибольшими очками.</summary>
        private void CheckTurnLimit(int turn)
        {
            if (Winner != null || turn <= TurnLimit) return;
            var best = Players.Where(p => !IsEliminated(p)).OrderByDescending(Score).First();
            Winner = best.Index;
            GameOverText = $"Срок партии вышел ({TurnLimit} ходов): по очкам побеждает {best.Region.displayName} — {Score(best)}";
        }

        /// <summary>Стороны воюют (только тогда можно атаковать, захватывать, грабить караваны).</summary>
        public bool AtWar(int a, int b) => Diplomacy.AtWar(a, b);

        /// <summary>Началась война: хук для торговли (разрыв соглашений).</summary>
        internal void OnWarStarted(int a, int b) => Trade.OnWar(a, b);

        internal void OnPeaceMade(int a, int b) { }

        /// <summary>Военная сила стороны: сумма сил боевых юнитов и по 10 за город.</summary>
        public int MilitaryStrength(int player) =>
            Players[player].Units.Where(u => u.IsAlive && CanFight(u)).Sum(u => u.Data.IsRanged ? u.RangedStrength : u.MeleeStrength) +
            10 * Cities.Count(c => c.OwnerIndex == player);

        /// <summary>Общая мощь стороны (для коалиций): войско, города, жители.</summary>
        public int PowerScore(int player) =>
            MilitaryStrength(player) + Cities.Where(c => c.OwnerIndex == player).Sum(c => 25 + 3 * c.Population);

        /// <summary>Идущие войны одной строкой (для журнала автопроверки).</summary>
        public string WarSummary()
        {
            var wars = new List<string>();
            foreach (var a in Players)
            foreach (var b in Players.Where(p => p.Index > a.Index))
                if (AtWar(a.Index, b.Index)) wars.Add($"{a.Index}-{b.Index}");
            return wars.Count == 0 ? "нет" : string.Join(",", wars);
        }

        /// <summary>Как торговля между сторонами влияет на их мнение за ход.</summary>
        internal int TradeOpinion(int a, int b) => Trade.OpinionBonus(a, b);

        private PlayerState OwnerOf(City city) => Players[city.OwnerIndex];

        /// <summary>Сид партии: из него и ключей события вычисляются все броски (см. <see cref="DetRandom"/>).</summary>
        public int GameSeed { get; private set; }

        /// <summary>id следующего юнита (часть состояния партии: сохраняется и входит в хеш).</summary>
        public int NextUnitId { get; internal set; } = 1;

        internal void SetGameSeed(int seed) => GameSeed = seed;

        /// <summary>Бросок в [0, 1) по ключам: сид партии, текущий ход, вид события и до трёх ключей.</summary>
        public double Roll(DetRandom.Kind kind, long a = 0, long b = 0, long c = 0) =>
            DetRandom.Unit(DetRandom.Hash(GameSeed, Turns.Turn, kind, a, b, c));

        /// <summary>Целое в [0, n) по ключам.</summary>
        public int RollIndex(int n, DetRandom.Kind kind, long a = 0, long b = 0, long c = 0) =>
            DetRandom.Range(DetRandom.Hash(GameSeed, Turns.Turn, kind, a, b, c), n);

        /// <summary>Ключ города для бросков (по id, не зависит от порядка в списке).</summary>
        public static long CityKey(City city) => DetRandom.StringKey(city.Data.id);

        public Season Season => Seasons.Of(Turns.Turn);
        public int Year => Seasons.Year(Turns.Turn);
        public int? Winner { get; private set; }
        public string GameOverText { get; private set; }

        public IReadOnlyList<PlayerState> Players => Turns.Players;

        public event Action<Unit> UnitCreated;
        /// <summary>Сменился владелец города или появился район.</summary>
        public event Action<City> CityChanged;
        public event Action<string> Message;
        /// <summary>Город выстрелил по клетке (для анимации).</summary>
        public event Action<City, HexCoord> CityShot;
        /// <summary>Основан новый город.</summary>
        public event Action<City> CityFounded;
        /// <summary>Проложена дорога или построен порт — карту нужно обновить.</summary>
        public event Action RoadsChanged;

        /// <summary>Сообщение в журнал (для подсистем вроде торговли).</summary>
        public void Report(string message) => Message?.Invoke(message);

        public void SetPort(DistrictData port) => Port = port;

        /// <summary>Восстановление из сохранения: итог партии.</summary>
        internal void Restore(int? winner, string gameOverText)
        {
            Winner = winner;
            GameOverText = gameOverText;
        }

        /// <summary>Юнит из сохранения: в список игрока и на карту.</summary>
        internal void AttachUnit(PlayerState player, Unit unit)
        {
            ApplyLeaderMovement(unit);
            unit.Promoted += u => Message?.Invoke($"{Name(u)} получает звание «{u.LevelName}» (+{u.Level * Unit.StrengthPerLevel} к силе)");
            player.Units.Add(unit);
            UnitCreated?.Invoke(unit);
        }

        public GameState(HexGrid grid, List<PlayerState> players, DistrictData market, Action<PlayerState> aiTurn,
            IReadOnlyList<GoodData> goods = null, IReadOnlyList<BuildingData> buildings = null,
            IReadOnlyList<Runeterra.Tech.TechData> techs = null)
        {
            Grid = grid;
            Market = market;
            Goods = goods ?? new List<GoodData>();
            BuildingTypes = buildings ?? new List<BuildingData>();
            Grain = Goods.FirstOrDefault(g => g.id == "grain");
            Trade = new TradeSystem(this);
            Tech = new TechSystem(this, techs);
            Vision = new Visibility(this);
            Diplomacy = new Diplomacy(this);
            GameSeed = grid.Seed;
            Turns = new TurnManager(players, aiTurn);
            Turns.PlayerTurnStarted += BeginPlayerTurn;
            RoadsChanged += Trade.ClearRouteCache;
            CityFounded += _ => Trade.ClearRouteCache();
            Turns.NewTurnStarted += t =>
            {
                Diplomacy.NewTurn(t);
                CheckTurnLimit(t);
            };
        }

        // ---------- Запросы ----------

        public Unit UnitAt(HexCoord c) => Turns.UnitAt(c);

        /// <summary>Враг на клетке, которого этот игрок сейчас видит.</summary>
        public Unit VisibleEnemyAt(HexCoord c, int ownerIndex) =>
            Vision.IsVisible(ownerIndex, c) ? EnemyAt(c, ownerIndex) : null;

        /// <summary>Юнит стороны, с которой идёт война.</summary>
        public Unit EnemyAt(HexCoord c, int ownerIndex)
        {
            var u = UnitAt(c);
            return u != null && u.OwnerIndex != ownerIndex && AtWar(u.OwnerIndex, ownerIndex) ? u : null;
        }

        /// <summary>Чужой юнит (любой стороны) — через него пройти нельзя.</summary>
        public Unit ForeignAt(HexCoord c, int ownerIndex)
        {
            var u = UnitAt(c);
            return u != null && u.OwnerIndex != ownerIndex ? u : null;
        }

        public City CityAt(HexCoord c) => Cities.FirstOrDefault(city => city.Coord == c);

        public City CityOwningTile(HexCoord c) => Cities.FirstOrDefault(city => city.Territory.Contains(c));

        public int? OwnerOfTile(HexCoord c) => CityOwningTile(c)?.OwnerIndex;

        public bool IsOccupied(HexCoord c) => UnitAt(c) != null;

        public bool IsFreeLand(HexCoord c) =>
            Grid.TryGetTile(c, out var t) && t.Terrain.IsPassable() && !IsOccupied(c);

        public City CapitalOf(PlayerState player) =>
            Cities.FirstOrDefault(c => c.OwnerIndex == player.Index && c.IsCapital && c.FounderIndex == player.Index)
            ?? Cities.FirstOrDefault(c => c.OwnerIndex == player.Index);

        public int IncomeOf(PlayerState player)
        {
            int sum = Cities.Where(c => c.OwnerIndex == player.Index).Sum(CityGold);
            if (player.Region.leaderAbility == LeaderAbility.ImperialTreasury) sum += Cities.Count(c => c.OwnerIndex == player.Index);
            return player.Epochs.Contains(TechSystem.EpochTrade) ? (int)Math.Round(sum * 1.1f) : sum;
        }

        /// <summary>Содержание построек города за ход.</summary>
        public int CityUpkeep(City city) => city.Buildings.Sum(kv => kv.Key.upkeep * kv.Value);

        /// <summary>Содержание всех построек стороны за ход (рынок и порт — районы, они бесплатны).</summary>
        public int BuildingUpkeep(PlayerState player) => Cities.Where(c => c.OwnerIndex == player.Index).Sum(CityUpkeep);

        /// <summary>Золото города за ход (за вычетом краж).</summary>
        public int CityGold(City city) =>
            CityIncome + city.Population / 2 + (city.HasMarket && Market != null ? Market.goldPerTurn + (OwnerOf(city).Has("markets") ? 1 : 0) : 0) +
            (city.HasPort && Port != null ? Port.goldPerTurn : 0) - CrimeLoss(city);

        // ---------- Рост и производство ----------

        public const int MinFoundDistance = 4;

        private IEnumerable<HexTile> TerritoryTiles(City city) =>
            city.Territory.Select(c => Grid.GetTile(c)).Where(t => t != null);

        /// <summary>
        /// Излишек еды за ход: база города + половина «съедобных» клеток (луга, речные долины, оазисы, побережье)
        /// минус прокорм жителей (каждый житель сверх первого ест 1). Может быть отрицательным — голод.
        /// </summary>
        public int CityFood(City city) =>
            city.Data.food + TerritoryTiles(city).Count(t =>
                t.Terrain == TerrainType.Grassland || t.Terrain == TerrainType.Coast || t.Terrain == TerrainType.River ||
                t.Feature == TileFeature.Oasis) / 2
            - (city.Population - 1) - (int)Math.Ceiling(WaterDeficit(city));

        /// <summary>Производство за ход: база + население + половина холмов и лесов территории.</summary>
        public int CityProduction(City city)
        {
            var owner = OwnerOf(city);
            int p = city.Data.production + city.Population +
                    TerritoryTiles(city).Count(t => t.Terrain == TerrainType.Hills || t.Feature == TileFeature.Forest) / 2;
            if (owner.Has("mills") && TerritoryTiles(city).Count(t => t.Terrain == TerrainType.Grassland || t.Terrain == TerrainType.Plains) >= 3) p += 1;
            if (owner.Has("centralization") && CapitalOf(owner) is City cap && cap != city && cap.Coord.DistanceTo(city.Coord) > 6) p -= 1;
            return Math.Max(1, (int)Math.Round(p * NobilityFactor(city)));
        }

        /// <summary>Недовольство знати налогом на роскошь (от 15%): производство Городков и крупнее −ставка.</summary>
        public float NobilityFactor(City city)
        {
            var owner = Players[city.OwnerIndex];
            return owner.LuxuryTax >= 0.15f && city.Tier >= SettlementTier.Town ? 1f - owner.LuxuryTax : 1f;
        }

        /// <summary>Порог роста: 10 + 5 × население; подушный налог замедляет рост (×(1 + 2 × ставка)).</summary>
        public int GrowthThreshold(City city) =>
            (int)Math.Round((10 + 5 * city.Population) * (1f + 2f * Players[city.OwnerIndex].PeopleTax));

        /// <summary>Ходов до роста; -1 — рост остановлен (нет излишка еды).</summary>
        public int TurnsToGrow(City city)
        {
            int food = CityFood(city);
            if (food <= 0) return -1;
            return Math.Max(1, (GrowthThreshold(city) - city.FoodStock + food - 1) / food);
        }

        public int TurnsToBuild(City city, BuildItem item)
        {
            int left = item.Cost - (item.Is(city.CurrentBuild) ? city.ProductionStock : 0);
            return Math.Max(1, (left + CityProduction(city) - 1) / Math.Max(1, CityProduction(city)));
        }

        public bool CanBuild(City city, BuildItem item, out string reason)
        {
            reason = null;
            if (item.Unit != null && item.Unit.canFoundCity && city.Population < 2) reason = "нужно население 2";
            else if (item.Unit != null && MissingGoods(city, item.Unit.goodsCost) is string lackingForUnit) reason = lackingForUnit;
            else if (item.District != null && HasDistrict(city, item.District)) reason = "уже построен";
            else if (item.District != null && DistrictSpot(city, item.District) == null)
                reason = item.District == Port ? "нет своей клетки у воды" : "нет места рядом";
            else if (item.Building != null && city.IsFull(item.Building))
                reason = item.Building.maxPerCity > 1 ? $"не больше {item.Building.maxPerCity}" : "уже построен";
            else if (item.Building != null && (int)city.Tier < item.Building.requiredTier -
                     (item.Building.id == "canal" && OwnerOf(city).Has("canals") ? 1 : 0))
                reason = $"нужна ступень «{SettlementTiers.Name(item.Building.requiredTier)}»";
            else if (item.Building != null && MissingGoods(city, item.Building) is string missing) reason = missing;
            return reason == null;
        }

        /// <summary>Выбор производства. Смена заказа сбрасывает накопленное, кроме остатка от готового.</summary>
        /// <summary>Ускорение постройки/района за золото: 2 золота за каждое недостающее очко производства (с инфляцией).</summary>
        public int RushCost(City city)
        {
            var item = city.CurrentBuild;
            if (item == null || item.Unit != null) return 0;
            int left = Math.Max(0, item.Cost - city.ProductionStock);
            return (int)Math.Round(left * 2f * (1f + Inflation(Players[city.OwnerIndex])));
        }

        public string CanRush(City city, PlayerState p)
        {
            if (city.OwnerIndex != p.Index) return "чужой город";
            if (city.CurrentBuild == null || city.CurrentBuild.Unit != null) return "только постройки и районы";
            if (!CanBuild(city, city.CurrentBuild, out var reason)) return reason;
            if (RushCost(city) <= 0) return "уже готово";
            if (p.Gold < RushCost(city)) return "не хватает золота";
            return null;
        }

        /// <summary>Выкупить остаток производства — постройка будет готова в начале следующего хода.</summary>
        public bool Rush(City city, PlayerState p)
        {
            if (CanRush(city, p) != null) return false;
            int cost = RushCost(city);
            p.Gold -= cost;
            city.ProductionStock = city.CurrentBuild.Cost;
            Message?.Invoke($"{city.Data.displayName}: {city.CurrentBuild.Name} ускорено за {cost} золота");
            return true;
        }

        public void SetBuild(City city, BuildItem item)
        {
            if (item != null && !CanBuild(city, item, out _)) return;
            if (city.CurrentBuild != null && (item == null || !item.Is(city.CurrentBuild))) city.ProductionStock = 0;
            city.CurrentBuild = item;
        }

        /// <summary>Каких товаров не хватает на складе для постройки (null — всего хватает).</summary>
        public string MissingGoods(City city, BuildingData building) => MissingGoods(city, building.goodsCost);

        public string MissingGoods(City city, IEnumerable<GoodAmount> cost)
        {
            var lacking = cost.Where(g => g.good != null && !city.Warehouse.Has(g.good, g.amount))
                .Select(g => $"{g.good.displayName} {g.amount}").ToList();
            return lacking.Count == 0 ? null : "нужно: " + string.Join(", ", lacking);
        }

        private static void TakeGoods(City city, IEnumerable<GoodAmount> cost)
        {
            foreach (var g in cost)
                if (g.good != null) city.Warehouse.Take(g.good, g.amount);
        }

        // ---------- Товары и сезоны ----------

        /// <summary>Выход сырья за ход в этом сезоне: (подходящих клеток / tilesPerUnit) × множитель сезона.</summary>
        public float GoodOutput(City city, GoodData good, Season season)
        {
            if (!good.IsRaw) return 0f;
            int tiles = TerritoryTiles(city).Count(good.FromTile);
            float blight = city.Blights.ContainsKey(good) ? 0.5f : 1f;
            float tech = good == Grain ? GrainMultiplier(OwnerOf(city)) : 1f;
            return (float)tiles / good.tilesPerUnit * good.SeasonMultiplier(season) * blight * tech;
        }

        /// <summary>Узлы «Земли и воды»: орошение +25%, севооборот +10%, интенсивное +30% минус истощение 3%/год.</summary>
        public float GrainMultiplier(PlayerState p)
        {
            float m = 1f;
            if (p.Has("irrigation")) m += 0.25f;
            if (p.Has("rotation")) m += 0.1f;
            if (p.Has("intensive")) m += 0.3f - Math.Min(0.3f, 0.03f * p.SoilExhaustionYears);
            return m;
        }

        // ---------- Мастерские ----------

        public const float ClusterBonusPerWorkshop = 0.25f;
        public const int ClusterRadius = 4;

        /// <summary>
        /// Бонус кластера: одинаковые мастерские в этом городе и своих городах в радиусе 4
        /// дают +25% выпуска за каждую сверх первой (не больше ×2).
        /// </summary>
        public float ClusterMultiplier(City city, BuildingData workshop)
        {
            int total = Cities.Where(c => c.OwnerIndex == city.OwnerIndex && c.Coord.DistanceTo(city.Coord) <= ClusterRadius)
                .Sum(c => c.Count(workshop));
            float per = OwnerOf(city).Has("guilds") ? 0.35f : ClusterBonusPerWorkshop;
            return Math.Min(2f, 1f + per * Math.Max(0, total - 1));
        }

        // ---------- Вода и ступени поселений ----------

        public const int BaseWater = 4;

        /// <summary>Запас воды: 4 (колодцы во дворах) + 3 за оазис и 2 за клетку речной долины на территории + постройки.</summary>
        public int WaterCapacity(City city) =>
            BaseWater + 3 * TerritoryTiles(city).Count(t => t.Feature == TileFeature.Oasis) +
            2 * TerritoryTiles(city).Count(t => t.Terrain == TerrainType.River) +
            city.Buildings.Sum(kv => (kv.Key.waterBonus + (kv.Key.id == "canal" && OwnerOf(city).Has("canals") ? 2 : 0)) * kv.Value) +
            (OwnerOf(city).Has("wells") ? 1 : 0);

        /// <summary>Расход воды: по 1 на жителя, 0.5 на мастерскую, 1 на каждые 4 обработанных поля.</summary>
        public float WaterUse(City city)
        {
            int workshops = city.Buildings.Where(kv => kv.Key.IsWorkshop).Sum(kv => kv.Value);
            int fields = Math.Min(TerritoryTiles(city).Count(t => t.Terrain == TerrainType.Grassland || t.Terrain == TerrainType.Plains),
                city.Population * 2);
            return city.Population + 0.5f * workshops + fields / 4f * (OwnerOf(city).Has("irrigation") ? 0.5f : 1f);
        }

        public float WaterDeficit(City city) => Math.Max(0f, WaterUse(city) - WaterCapacity(city));

        /// <summary>Доля выпуска мастерских при нехватке воды.</summary>
        public float WaterFactor(City city) => WaterDeficit(city) <= 0f ? 1f : WaterCapacity(city) / WaterUse(city);

        /// <summary>Есть ли дорога (только дороги и города) до своего города с портом.</summary>
        public bool RoadToPort(City city)
        {
            if (city.HasPort) return true;
            var seen = new HashSet<HexCoord> { city.Coord };
            var queue = new Queue<HexCoord>();
            queue.Enqueue(city.Coord);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var other = CityAt(c);
                if (other != null && other.OwnerIndex == city.OwnerIndex && other.HasPort) return true;
                foreach (var n in Grid.Neighbors(c))
                    if ((n.HasRoad || n.CityId != null) && seen.Add(n.Coord)) queue.Enqueue(n.Coord);
            }
            return false;
        }

        /// <summary>Условия ступеней. Возвращает список невыполненных условий для следующей ступени.</summary>
        public List<string> MissingForTier(City city, SettlementTier tier)
        {
            var miss = new List<string>();
            int[] pop = { 1, 3, 4, 5, 8 };
            if (city.Population < pop[(int)tier]) miss.Add($"население {pop[(int)tier]} ({pop[(int)tier]}000 жителей)");
            if (tier >= SettlementTier.Village && WaterDeficit(city) > 0f) miss.Add("хватает воды");
            if (tier >= SettlementTier.Town && !city.HasMarket) miss.Add("рынок");
            if (tier >= SettlementTier.City && !RoadToPort(city)) miss.Add("дорога к порту");
            if (tier >= SettlementTier.TradeCapital)
            {
                if (!city.HasPort) miss.Add("свой порт");
                if (city.Buildings.Where(kv => kv.Key.IsWorkshop).Sum(kv => kv.Value) < 2) miss.Add("2 мастерские");
                if (!city.Buildings.Keys.Any(b => b.storageBonus > 0)) miss.Add("амбар");
            }
            return miss;
        }

        public SettlementTier ComputeTier(City city)
        {
            var tier = SettlementTier.Hamlet;
            for (var t = SettlementTier.Village; t <= SettlementTier.TradeCapital; t++)
            {
                if (MissingForTier(city, t).Count > 0) break;
                tier = t;
            }
            return tier;
        }

        // ---------- Беды больших поселений ----------

        public float FireChance(City city) => city.Tier < SettlementTier.Town ? 0f
            : Math.Max(0.005f, 0.04f - city.Buildings.Sum(kv => kv.Key.fireReduction * kv.Value));

        /// <summary>Преступность: 5% за каждого жителя сверх 4 (с «Города»), стража снижает. Ворует долю золота города.</summary>
        public float CrimeRate(City city) => city.Tier < SettlementTier.City ? 0f
            : Math.Min(0.4f, Math.Max(0f, 0.05f * (city.Population - 4) - city.Buildings.Sum(kv => kv.Key.crimeReduction * kv.Value))) *
              (OwnerOf(city).Has("centralization") ? 0.5f : 1f);

        public float EpidemicChance(City city) => city.Tier < SettlementTier.City ? 0f
            : Math.Max(0.005f, 0.03f + (WaterDeficit(city) > 0f ? 0.02f : 0f) - city.Buildings.Sum(kv => kv.Key.epidemicReduction * kv.Value)) *
              (OwnerOf(city).Has("medicine") ? 0.5f : 1f);

        public int CrimeLoss(City city) => (int)Math.Floor(CrimeRate(city) * (CityIncome + city.Population / 2 +
            (city.HasMarket && Market != null ? Market.goldPerTurn : 0) + (city.HasPort && Port != null ? Port.goldPerTurn : 0)));

        private void UpdateTierAndDisasters(City city)
        {
            // Повышение — сразу; понижение — только если условия не выполняются 2 хода подряд.
            var tier = ComputeTier(city);
            if (tier < city.Tier && ++city.TierFailTurns < 2) tier = city.Tier;
            if (tier >= city.Tier) city.TierFailTurns = 0;
            if (tier != city.Tier)
            {
                Message?.Invoke($"{city.Data.displayName}: теперь «{SettlementTiers.Name(tier)}»");
                city.Tier = tier;
                CityChanged?.Invoke(city);
            }

            long key = CityKey(city);
            if (Roll(DetRandom.Kind.Fire, key, city.OwnerIndex) < FireChance(city))
            {
                var owner = Players[city.OwnerIndex];
                if (owner.Reserve >= ReserveFireCost)
                {
                    owner.Reserve -= ReserveFireCost;
                    Message?.Invoke($"{city.Data.displayName}: пожар потушен, ущерб покрыт резервной казной (−{ReserveFireCost})");
                    goto fireDone;
                }
                var wood = Goods.FirstOrDefault(g => g.id == "wood");
                if (wood != null) city.Warehouse.Take(wood, city.Warehouse.Get(wood) * 0.5f);
                city.ProductionStock /= 2;
                var msg = $"{city.Data.displayName}: пожар! Сгорела половина дерева и производства";
                var burnable = city.Buildings.Keys.Where(b => b.IsWorkshop).ToList();
                if (burnable.Count > 0 && Roll(DetRandom.Kind.FireBurnsWorkshop, key, city.OwnerIndex) < 0.3)
                {
                    var b = burnable[RollIndex(burnable.Count, DetRandom.Kind.FireWorkshopPick, key, city.OwnerIndex)];
                    if (--city.Buildings[b] <= 0) city.Buildings.Remove(b);
                    var masters = city.MastersOf(b);
                    if (masters.Count > city.Count(b)) masters.RemoveAt(masters.Count - 1);
                    msg += $", сгорела {b.displayName}";
                }
                Message?.Invoke(msg);
            }
            fireDone:

            if (Roll(DetRandom.Kind.Epidemic, key, city.OwnerIndex) < EpidemicChance(city))
            {
                LosePopulation(city, "эпидемия");
                if (city.Population >= 8) LosePopulation(city, "эпидемия");
            }
        }

        // ---------- Налоги и страховка ----------

        public const int ReserveGrainPrice = 3;
        public const int ReserveFireCost = 15;
        public const float HeavyTax = 0.15f;
        public const float FleeChance = 0.35f;

        /// <summary>Налог с людей: ставка × 2 золота за каждого жителя.</summary>
        private static float Scribes(PlayerState p) => p.Has("scribes") ? 1.1f : 1f;

        public int PeopleTaxOf(PlayerState p) =>
            (int)Math.Round(p.PeopleTax * 2f * Scribes(p) * Cities.Where(c => c.OwnerIndex == p.Index).Sum(c => c.Population));

        /// <summary>Земельный налог: ставка × половина стоимости добытого за ход сырья.</summary>
        public int LandTaxOf(PlayerState p) =>
            (int)Math.Round(p.LandTax * 0.5f * Scribes(p) * (p.Has("registry") ? 1.25f : 1f) *
                            Cities.Where(c => c.OwnerIndex == p.Index).Sum(c => c.LastRawValue));

        public int LuxuryTaxOf(PlayerState p) =>
            (int)Math.Round(p.LuxuryTax * Scribes(p) * (p.Has("luxury") ? 1.5f : 1f) * (p.Has("iqta") ? 0.5f : 1f) *
                            Cities.Where(c => c.OwnerIndex == p.Index).Sum(c => c.LastLuxuryValue));

        /// <summary>Доля пошлины, уходящая контрабандой: (пошлина − 10%) × 2.5, стража в городе −20%, от 0 до 80%.</summary>
        public static float SmugglingShare(float tariff, bool guarded) =>
            Math.Max(0f, Math.Min(0.8f, (tariff - 0.1f) * 2.5f - (guarded ? 0.2f : 0f)));

        public bool HasGuard(City city) => city.Buildings.Keys.Any(b => b.crimeReduction > 0);

        /// <summary>
        /// Сбор налогов в начале хода. Минусы: тяжёлый земельный налог при неурожае гонит крестьян;
        /// подушный замедляет рост; налог на роскошь от 15% злит знать (−производство).
        /// </summary>
        private void CollectTaxes(PlayerState p)
        {
            p.LandIncome = LandTaxOf(p);
            p.PeopleIncome = PeopleTaxOf(p);
            p.LuxuryIncome = LuxuryTaxOf(p);
            p.Gold += p.LandIncome + p.PeopleIncome + p.LuxuryIncome;
            PayInterest(p);

            if (p.LandTax < HeavyTax) return;
            foreach (var city in Cities.Where(c => c.OwnerIndex == p.Index && c.BadHarvest && c.Population > 1).ToList())
                if (Roll(DetRandom.Kind.PeasantsFlee, CityKey(city), p.Index) < FleeChance) LosePopulation(city, "неурожай и земельный налог — крестьяне бегут");
        }

        // ---------- Монета и займы ----------

        public const float DebaseStep = 0.05f;
        public const float MaxDebasement = 0.5f;
        public const int LoanStep = 50;
        public const int BankruptcyTurns = 12;

        private int TotalPopulation(PlayerState p) => Cities.Where(c => c.OwnerIndex == p.Index).Sum(c => c.Population);

        /// <summary>Инфляция = порча × 1.5: всё, что покупается за золото, дорожает.</summary>
        public float Inflation(PlayerState p) => p.Debasement * 1.5f;

        public int BuyCost(PlayerState p, UnitData data) => (int)Math.Round(data.goldCost * (1f + Inflation(p)));

        public int DebaseGain(PlayerState p) => 10 + 2 * TotalPopulation(p);
        public int RecoinCost(PlayerState p) => 15 + TotalPopulation(p);

        public bool IsBankrupt(PlayerState p) => Turns.Turn < p.BankruptUntil;

        /// <summary>Испортить монету: сразу +золото, но инфляция и уход торговцев.</summary>
        public bool Debase(PlayerState p)
        {
            if (p.Debasement >= MaxDebasement - 0.001f) return false;
            p.Debasement = Math.Min(MaxDebasement, p.Debasement + DebaseStep);
            int gain = DebaseGain(p);
            p.Gold += gain;
            Message?.Invoke($"{p.Region.displayName} портит монету (+{gain}); порча {p.Debasement:0%}, инфляция {Inflation(p):0%}");
            return true;
        }

        /// <summary>Перечеканка: вернуть доверие к монете за золото.</summary>
        public bool Recoin(PlayerState p)
        {
            int cost = RecoinCost(p);
            if (p.Debasement <= 0.001f || p.Gold < cost) return false;
            p.Gold -= cost;
            p.Debasement = Math.Max(0f, p.Debasement - DebaseStep);
            Message?.Invoke($"{p.Region.displayName} перечеканивает монету (−{cost}); порча {p.Debasement:0%}");
            return true;
        }

        /// <summary>
        /// Кредитный рейтинг: долг к годовому доходу (<0.5 — A, <1 — B, <2 — C, иначе D);
        /// каждый пропущенный платёж −1 ступень; банкрот — D, после банкротства не выше C.
        /// </summary>
        public char CreditRating(PlayerState p)
        {
            if (IsBankrupt(p)) return 'D';
            float yearIncome = Math.Max(1, IncomeOf(p) * Seasons.TurnsPerYear);
            float ratio = p.Debt / yearIncome;
            int grade = ratio < 0.5f ? 0 : ratio < 1f ? 1 : ratio < 2f ? 2 : 3;
            grade = Math.Min(3, grade + p.MissedPayments);
            if (Turns.Turn < p.StigmaUntil) grade = Math.Max(grade, 2);
            return "ABCD"[grade];
        }

        public static string RatingText(char r) => r switch
        {
            'A' => "надёжный",
            'B' => "хороший",
            'C' => "рискованный",
            _ => "дефолт — займов нет",
        };

        /// <summary>Годовая ставка по рейтингу: A 8%, B 14%, C 24%. Растёт вместе с долгом.</summary>
        public float InterestRate(PlayerState p)
        {
            float r = CreditRating(p) switch { 'A' => 0.08f, 'B' => 0.14f, 'C' => 0.24f, _ => 0.3f };
            if (p.Has("treasury")) r *= 0.75f;
            if (p.Has("exchange")) r = Math.Max(0.02f, r - 0.02f);
            return r;
        }

        public int InterestPerTurn(PlayerState p) =>
            p.Debt <= 0 ? 0 : (int)Math.Ceiling(p.Debt * InterestRate(p) / Seasons.TurnsPerYear);

        public int CreditLimit(PlayerState p) =>
            (int)((CreditRating(p) switch { 'A' => 200, 'B' => 120, 'C' => 60, _ => 0 }) * (p.Has("houses") ? 1.5f : 1f));

        public bool CanBorrow(PlayerState p) => p.Debt + LoanStep <= CreditLimit(p);

        public bool Borrow(PlayerState p)
        {
            if (!CanBorrow(p)) return false;
            p.Debt += LoanStep;
            p.Gold += LoanStep;
            Message?.Invoke($"{p.Region.displayName} занимает {LoanStep} у купеческого дома; долг {p.Debt}, ставка {InterestRate(p):0%}/год");
            return true;
        }

        public bool Repay(PlayerState p)
        {
            int amount = Math.Min(LoanStep, p.Debt);
            if (amount <= 0 || p.Gold < amount) return false;
            p.Gold -= amount;
            p.Debt -= amount;
            if (p.Debt == 0) p.MissedPayments = 0;
            return true;
        }

        /// <summary>Проценты каждый ход. Два пропущенных платежа подряд — банкротство.</summary>
        private void PayInterest(PlayerState p)
        {
            p.InterestLastTurn = 0;
            int due = InterestPerTurn(p);
            if (due <= 0) return;
            if (p.Gold >= due)
            {
                p.Gold -= due;
                p.InterestLastTurn = due;
                return;
            }
            p.MissedPayments++;
            Message?.Invoke($"{p.Region.displayName}: нечем платить проценты ({due}) — рейтинг падает");
            if (p.MissedPayments < 2) return;
            p.Debt = 0;
            p.MissedPayments = 0;
            p.BankruptUntil = Turns.Turn + BankruptcyTurns;
            p.StigmaUntil = Turns.Turn + BankruptcyTurns * 2;
            Message?.Invoke($"{p.Region.displayName}: банкротство! Долг списан, но купцы {BankruptcyTurns} ходов не торгуют с его землями");
        }

        // ---------- Работники ----------

        public const int SkillStepTurns = 4;
        public const float SkillPerStep = 0.1f;
        public const float MaxSkill = 2f;

        /// <summary>Мастерство мастера: +10% за каждый год стажа (4 хода), не больше ×2.</summary>
        public static float SkillOf(int experience) => Math.Min(MaxSkill, 1f + SkillPerStep * (experience / SkillStepTurns));

        /// <summary>Среднее мастерство мастеров этого типа мастерских в городе.</summary>
        public float WorkshopSkill(City city, BuildingData w)
        {
            var list = city.MastersOf(w);
            return list.Count == 0 ? 1f : list.Average(e => SkillOf(e));
        }

        /// <summary>Свободные жители становятся новыми мастерами пустующих мастерских.</summary>
        private void AssignMasters(City city)
        {
            foreach (var w in city.Buildings.Keys.Where(b => b.IsWorkshop).OrderByDescending(b => b.output.good.basePrice).ToList())
            {
                var list = city.MastersOf(w);
                while (list.Count < city.Count(w) && city.FreeWorkers > 0) list.Add(0);
            }
        }

        /// <summary>
        /// Город теряет жителя. Если свободных нет — уходит самый опытный мастер:
        /// его место займёт новичок, и выпуск надолго упадёт.
        /// </summary>
        public void LosePopulation(City city, string reason)
        {
            if (city.Population <= 1) return;
            city.Population--;
            var msg = $"{city.Data.displayName}: {reason}, население {city.Population}";
            if (city.Population - 1 < city.Employed)
            {
                var (w, idx) = city.Masters.SelectMany(kv => kv.Value.Select((e, i) => (kv.Key, i, e)))
                    .OrderByDescending(x => x.e).Select(x => (x.Key, x.i)).First();
                int exp = city.Masters[w][idx];
                city.Masters[w].RemoveAt(idx);
                msg += $". Потерян мастер ({w.displayName}, стаж {exp} х.) — выпуск упадёт";
            }
            Message?.Invoke(msg);
            CityChanged?.Invoke(city);
        }

        public const int DraftCooldown = 4;

        public string CanDraft(City city, PlayerState player)
        {
            if (city.OwnerIndex != player.Index) return "чужой город";
            if (city.Population < 2) return "нужно население 2";
            if (Turns.Turn < city.DraftCooldownUntil) return $"снова через {city.DraftCooldownUntil - Turns.Turn} х.";
            if (SpawnSpot(city) == null) return "нет места рядом";
            if (Shop.Count == 0) return "некого призывать";
            return null;
        }

        /// <summary>Юниты, из которых берётся ополченец (первый ближнего боя).</summary>
        public List<UnitData> Shop { get; } = new List<UnitData>();

        /// <summary>Призыв ополчения: бесплатный Воин, но город теряет жителя (возможно, мастера).</summary>
        public Unit Draft(City city, PlayerState player)
        {
            if (CanDraft(city, player) != null) return null;
            var warrior = Shop.FirstOrDefault(d => d.role == UnitRole.Melee);
            if (warrior == null) return null;
            var unit = Spawn(warrior, player, SpawnSpot(city).Value);
            unit.SpendAllMoves();
            city.DraftCooldownUntil = Turns.Turn + DraftCooldown;
            LosePopulation(city, "призвано ополчение");
            return unit;
        }

        /// <summary>Сколько раз за ход мастерские этого типа выполнят рецепт (ограничено мастерами и сырьём).</summary>
        public float WorkshopBatches(City city, BuildingData w)
        {
            float batches = (w.batchesPerTurn + (OwnerOf(city).Has("workshops") ? 1 : 0)) * city.MastersOf(w).Count;
            foreach (var input in w.inputs.Where(i => i.good != null && i.amount > 0))
                batches = Math.Min(batches, (float)Math.Floor(city.Warehouse.Get(input.good) / input.amount));
            return Math.Max(0f, batches);
        }

        /// <summary>Мастерские по цепочке: сначала низшие ступени, чтобы полуфабрикат шёл дальше в тот же ход.</summary>
        private void RunWorkshops(City city)
        {
            AssignMasters(city);
            foreach (var w in city.Buildings.Keys.Where(b => b.IsWorkshop).OrderBy(b => b.output.good.tier).ToList())
            {
                float batches = WorkshopBatches(city, w);
                if (batches <= 0f) continue;
                foreach (var input in w.inputs.Where(i => i.good != null)) city.Warehouse.Take(input.good, input.amount * batches);
                float chains = OwnerOf(city).Has("chains") && w.output.good.tier >= 2 ? 1.25f : 1f;
                if (OwnerOf(city).Epochs.Contains(TechSystem.EpochCraft)) chains *= 1.1f;
                city.Warehouse.Add(w.output.good, w.output.amount * batches * ClusterMultiplier(city, w) * WorkshopSkill(city, w) * WaterFactor(city) * chains);
                var masters = city.MastersOf(w);
                for (int i = 0; i < masters.Count; i++) masters[i]++;
            }
        }

        /// <summary>Жители потребляют предметы спроса (ткань, сласти) — это держит на них цену.</summary>
        private void Consume(City city)
        {
            city.LastLuxuryValue = 0f;
            foreach (var good in Goods.Where(g => g.consumedPerPop > 0f))
                city.LastLuxuryValue += city.Warehouse.Take(good, good.consumedPerPop * city.Population * (OwnerOf(city).Has("luxury") ? 1.5f : 1f)) * good.basePrice;
        }

        // ---------- Монокультура ----------

        public const float MonocultureShare = 0.6f;
        public const float BlightChance = 0.15f;

        /// <summary>Доля самого ценного сырья в годовой добыче города (стоимостью). Выше 60% — монокультура.</summary>
        public (GoodData good, float share) Monoculture(City city)
        {
            var values = Goods.Where(g => g.IsRaw).Select(g =>
                (g, v: Enumerable.Range(0, 4).Sum(sz => GoodOutput(city, g, (Season)sz)) * g.basePrice)).ToList();
            float total = values.Sum(x => x.v);
            if (total <= 0f) return (null, 0f);
            var top = values.OrderByDescending(x => x.v).First();
            return (top.g, top.v / total);
        }

        private void CheckBlight(City city)
        {
            foreach (var g in city.Blights.Keys.ToList())
                if (--city.Blights[g] <= 0) city.Blights.Remove(g);
            if (Season != Season.Harvest) return;
            var (good, share) = Monoculture(city);
            if (good == null || share < MonocultureShare || city.Blights.ContainsKey(good)) return;
            if (Roll(DetRandom.Kind.Blight, CityKey(city), city.OwnerIndex) >= BlightChance * (OwnerOf(city).Has("rotation") ? 0.5f : 1f)) return;
            city.Blights[good] = Seasons.TurnsPerYear;
            Message?.Invoke($"{city.Data.displayName}: болезнь урожая — {good.displayName} весь год вдвое меньше (монокультура {share:0%})");
        }

        /// <summary>Сколько зерна город проест в засуху (по 1 на жителя).</summary>
        public int DroughtGrainNeed(City city) =>
            (int)Math.Ceiling(city.Population * (OwnerOf(city).Has("calendar") ? 0.75f : 1f));

        /// <summary>Добыча, порча и (в засуху) проедание зерна. Нехватка зерна бьёт по запасу еды.</summary>
        private void ProcessGoods(City city)
        {
            CheckBlight(city);
            city.LastRawValue = 0f;
            city.BadHarvest = city.Blights.Count > 0;
            foreach (var good in Goods)
            {
                float output = GoodOutput(city, good, Season);
                city.Warehouse.Add(good, output);
                city.LastRawValue += output * good.basePrice;
            }
            RunWorkshops(city);
            Consume(city);
            city.Warehouse.Spoil();
            if (Season != Season.Drought || Grain == null) return;
            int need = DroughtGrainNeed(city);
            float eaten = city.Warehouse.Take(Grain, need);
            int shortfall = (int)Math.Ceiling(need - eaten - 0.001f);
            if (shortfall <= 0) return;
            // Страховка: резервная казна закупает недостающее зерно по 3 золота.
            var owner = Players[city.OwnerIndex];
            int grainPrice = owner.Has("treasury") ? 2 : ReserveGrainPrice;
            int covered = Math.Min(shortfall, owner.Reserve / grainPrice);
            if (covered > 0)
            {
                owner.Reserve -= covered * grainPrice;
                shortfall -= covered;
                Message?.Invoke($"{city.Data.displayName}: резервная казна закупила {covered} зерна (−{covered * grainPrice})");
                if (shortfall <= 0) return;
            }
            city.BadHarvest = true;
            city.FoodStock -= shortfall * 2;
            Message?.Invoke($"{city.Data.displayName}: засуха, не хватает {shortfall} зерна — запасы еды тают");
        }

        private void GrowAndProduce(PlayerState player, City city)
        {
            UpdateTierAndDisasters(city);
            ProcessGoods(city);
            city.FoodStock += CityFood(city);
            if (city.FoodStock < 0)
            {
                city.FoodStock = 0;
                if (city.Population > 1) LosePopulation(city, "голод");
            }
            if (city.FoodStock >= GrowthThreshold(city))
            {
                city.FoodStock -= GrowthThreshold(city);
                city.Population++;
                ExpandBorders(city);
                Message?.Invoke($"{city.Data.displayName}: население выросло до {city.Population}");
                CityChanged?.Invoke(city);
            }

            var item = city.CurrentBuild;
            if (item == null) return;
            if (!CanBuild(city, item, out var reason))
            {
                // Например, рынок уже поставил строитель — снимаем заказ, накопленное сохраняем.
                if ((item.District != null && HasDistrict(city, item.District)) || (item.Building != null && city.IsFull(item.Building)))
                {
                    city.CurrentBuild = null;
                    Message?.Invoke($"{city.Data.displayName}: {item.Name} — {reason}, выберите другое производство");
                    return;
                }
            }
            city.ProductionStock += CityProduction(city);
            if (city.ProductionStock < item.Cost) return;
            if (!CanBuild(city, item, out var waitReason))
            {
                if (item.Building != null) Message?.Invoke($"{city.Data.displayName}: {item.Name} ждёт товаров — {waitReason}");
                return;
            }

            if (item.Unit != null)
            {
                var spot = SpawnSpot(city);
                if (spot == null) return; // ждём, пока освободится место
                if (item.Unit.canFoundCity) LosePopulation(city, "ушли поселенцы");
                TakeGoods(city, item.Unit.goodsCost);
                var unit = Spawn(item.Unit, player, spot.Value);
                unit.SpendAllMoves();
                ArmFromCity(unit, city);
            }
            else if (item.District != null)
            {
                SetDistrict(city, item.District, DistrictSpot(city, item.District).Value);
                if (item.District == Port) RoadsChanged?.Invoke();
            }
            else
            {
                foreach (var g in item.Building.goodsCost) city.Warehouse.Take(g.good, g.amount);
                city.AddBuilding(item.Building);
            }
            city.ProductionStock -= item.Cost;
            Message?.Invoke($"{city.Data.displayName}: построено — {item.Name}");
            // Готово — город ждёт нового заказа (остаток производства сохраняется).
            city.CurrentBuild = null;
            CityChanged?.Invoke(city);
        }

        /// <summary>Границы растут с населением: забираем ничьи клетки в радиусе.</summary>
        public void ExpandBorders(City city)
        {
            int r = city.BorderRadius;
            for (int dq = -r; dq <= r; dq++)
            for (int dr = Math.Max(-r, -dq - r); dr <= Math.Min(r, -dq + r); dr++)
            {
                var c = city.Coord + new HexCoord(dq, dr);
                if (Grid.TryGetTile(c, out _) && CityOwningTile(c) == null) city.Territory.Add(c);
            }
        }

        // ---------- Основание городов ----------

        public string CanFoundCity(Unit unit, HexCoord at)
        {
            if (!unit.Data.canFoundCity) return "не поселенец";
            if (!Grid.TryGetTile(at, out var tile) || !tile.Terrain.IsPassable()) return "неподходящая клетка";
            var owner = CityOwningTile(at);
            if (owner != null && owner.OwnerIndex != unit.OwnerIndex) return "чужая территория";
            if (Cities.Any(c => c.Coord.DistanceTo(at) < MinFoundDistance)) return $"слишком близко к городу (нужно {MinFoundDistance} клетки)";
            if (IsDistrictTile(at)) return "здесь район";
            return null;
        }

        public City FoundCity(Unit unit)
        {
            if (unit.MovesLeft <= 0 || CanFoundCity(unit, unit.Coord) != null) return null;
            var player = Players[unit.OwnerIndex];
            var names = player.Region.cityNames;
            int founded = Cities.Count(c => c.FounderIndex == player.Index && !c.Data.isCapital && c.Data.name.StartsWith("Founded"));
            string name = founded < names.Count ? names[founded] : $"{player.Region.displayName} {founded + 1}";

            var data = UnityEngine.ScriptableObject.CreateInstance<CityData>();
            data.name = $"Founded_{name}";
            data.id = $"{player.Region.id}_{founded + 1}";
            data.displayName = name;
            data.startingPopulation = 1;
            data.food = 2;
            data.production = 1;

            Grid.GetTile(unit.Coord).Feature = TileFeature.None;
            Grid.GetTile(unit.Coord).CityId = data.id;
            var city = new City(data, unit.Coord, player.Index);
            ExpandBorders(city);
            Cities.Add(city);
            unit.Consume();
            Turns.RemoveDead();
            Vision.RefreshAll();
            CityFounded?.Invoke(city);
            Message?.Invoke($"{player.Region.displayName} основывает город {name}");
            return city;
        }

        /// <summary>
        /// Клетки, закрытые для юнита: вражеские юниты и вражеские города (со стенами или если он не захватчик).
        /// Сквозь своих проходить можно, но останавливаться на занятой клетке нельзя (см. MoveUnit).
        /// </summary>
        public Func<HexCoord, bool> BlockedFor(Unit unit, HexCoord? allowGoal = null)
        {
            // Снимок позиций на время одного поиска пути: A* спрашивает о тысячах клеток,
            // перебирать всех юнитов и города на каждый вопрос слишком дорого.
            int me = unit.OwnerIndex;
            var foreignUnits = new HashSet<HexCoord>(Players.Where(p => p.Index != me).SelectMany(p => p.Units).Where(u => u.IsAlive).Select(u => u.Coord));
            var closedCities = new HashSet<HexCoord>(Cities.Where(c => c.OwnerIndex != me &&
                (!AtWar(c.OwnerIndex, me) || !unit.Data.canCapture || c.Walls > 0)).Select(c => c.Coord));
            return c => (allowGoal == null || c != allowGoal.Value) && (foreignUnits.Contains(c) || closedCities.Contains(c));
        }

        /// <summary>Город стороны, с которой идёт война.</summary>
        public bool IsEnemyCity(HexCoord c, int ownerIndex)
        {
            var city = CityAt(c);
            return city != null && city.OwnerIndex != ownerIndex && AtWar(city.OwnerIndex, ownerIndex);
        }

        public bool IsForeignCity(HexCoord c, int ownerIndex)
        {
            var city = CityAt(c);
            return city != null && city.OwnerIndex != ownerIndex;
        }

        public List<HexCoord> PathFor(Unit unit, HexCoord target) =>
            HexPathfinder.FindPath(Grid, unit.Coord, target, BlockedFor(unit));

        /// <summary>Путь к клетке рядом с целью (сама цель занята).</summary>
        public List<HexCoord> PathToAdjacent(Unit unit, HexCoord target)
        {
            var path = HexPathfinder.FindPath(Grid, unit.Coord, target, BlockedFor(unit, target));
            if (path == null || path.Count == 0) return null;
            path.RemoveAt(path.Count - 1);
            return path;
        }

        public Dictionary<HexCoord, int> ReachableFor(Unit unit)
        {
            if (unit.MovesLeft <= 0) return new Dictionary<HexCoord, int>();
            var result = HexPathfinder.Reachable(Grid, unit.Coord, unit.MovesLeft, BlockedFor(unit));
            foreach (var c in result.Keys.Where(IsOccupied).ToList()) result.Remove(c);
            return result;
        }

        // ---------- Движение и захват ----------

        public void MoveUnit(Unit unit, List<HexCoord> path, Func<HexCoord, bool> stopAt = null)
        {
            try { MoveUnitCore(unit, path, stopAt); }
            // Обзор меняется у того, кто ходил; игроку-человеку — чтобы видеть чужие отряды в движении.
            finally
            {
                Vision.Refresh(Players[unit.OwnerIndex]);
                foreach (var p in Players) if (p.IsHuman && p.Index != unit.OwnerIndex) Vision.Refresh(p);
            }
        }

        private void MoveUnitCore(Unit unit, List<HexCoord> path, Func<HexCoord, bool> stopAt)
        {
            if (path == null || path.Count == 0 || Winner != null) return;
            path = TrimToFreeStop(unit, path, stopAt);
            if (path.Count == 0) return;
            unit.MoveAlong(Grid, path, c => IsEnemyCity(c, unit.OwnerIndex) || (stopAt != null && stopAt(c)));
            var city = CityAt(unit.Coord);
            if (city != null && IsEnemyCity(city.Coord, unit.OwnerIndex) && unit.Data.canCapture && city.Walls == 0)
                Capture(city, unit.OwnerIndex);
        }

        /// <summary>
        /// Обрезает путь так, чтобы юнит не закончил движение на клетке, занятой своим юнитом
        /// (проходить сквозь своих можно).
        /// </summary>
        private List<HexCoord> TrimToFreeStop(Unit unit, List<HexCoord> path, Func<HexCoord, bool> stopAt)
        {
            int moves = unit.MovesLeft, count = 0;
            foreach (var step in path)
            {
                if (moves <= 0) break;
                moves = Math.Max(0, moves - Grid.GetTile(step).MoveCost());
                count++;
                if (IsEnemyCity(step, unit.OwnerIndex) || (stopAt != null && stopAt(step))) break;
            }
            while (count > 0 && IsOccupied(path[count - 1])) count--;
            return path.GetRange(0, count);
        }

        private void Capture(City city, int newOwner)
        {
            int oldOwner = city.OwnerIndex;
            var oldRegion = Players[city.OwnerIndex].Region;
            var newRegion = Players[newOwner].Region;
            city.OwnerIndex = newOwner;
            city.ResetWallsAfterCapture();
            Trade.ClearRouteCache();
            Diplomacy.AddOpinion(oldOwner, newOwner, -20);
            // При штурме гибнет половина мастеров, у выживших стаж сгорает наполовину.
            foreach (var list in city.Masters.Values)
            {
                list.RemoveRange(0, list.Count / 2);
                for (int i = 0; i < list.Count; i++) list[i] /= 2;
            }
            if (city.Population > 1) city.Population--;
            CityChanged?.Invoke(city);
            Message?.Invoke($"{newRegion.displayName} захватывает город {city.Data.displayName} ({oldRegion.displayName})");

            // Шесть сторон: падение столицы игрока — поражение; остальные стороны воюют, пока у них есть города.
            if (city.IsCapital && city.FounderIndex != newOwner && Players[city.FounderIndex].IsHuman && Winner == null)
            {
                Winner = newOwner;
                GameOverText = $"{newRegion.displayName} захватывает столицу {city.Data.displayName}";
            }
            CheckElimination(Players[oldOwner]);
            var human = Players.FirstOrDefault(p => p.IsHuman);
            if (Winner == null && human != null && Players.Where(p => p != human).All(IsEliminated))
            {
                Winner = human.Index;
                GameOverText = $"{human.Region.displayName} покоряет все державы";
            }
        }

        /// <summary>Сторона выбыла: у неё не осталось городов.</summary>
        public bool IsEliminated(PlayerState p) => !Cities.Any(c => c.OwnerIndex == p.Index);

        /// <summary>Потерявшая последний город сторона выбывает: войско расходится, караваны пропадают.</summary>
        private void CheckElimination(PlayerState p)
        {
            if (!IsEliminated(p)) return;
            foreach (var u in p.Units.ToList()) u.Consume();
            Turns.RemoveDead();
            Trade.RemoveOwner(p.Index);
            Message?.Invoke($"{p.Region.displayName} теряет последний город и сходит со сцены");
        }

        // ---------- Бой ----------

        /// <summary>Прибавка к защите юнита от местности: холмы +5, лес +3.</summary>
        public int DefenseBonus(HexCoord c)
        {
            if (!Grid.TryGetTile(c, out var t)) return 0;
            int bonus = 0;
            if (t.Terrain == TerrainType.Hills) bonus += 5;
            if (t.Feature == TileFeature.Forest) bonus += 3;
            return bonus;
        }

        /// <summary>Блокада: вражеский боевой юнит в 2 клетках от города — стены не чинятся.</summary>
        public bool IsBlockaded(City city) =>
            Players.Where(p => AtWar(p.Index, city.OwnerIndex)).SelectMany(p => p.Units)
                .Any(u => u.IsAlive && CanFight(u) && u.Coord.DistanceTo(city.Coord) <= 2);

        public int CityStrength(City city)
        {
            int s = City.BaseStrength + (city.IsCapital ? City.CapitalStrengthBonus : 0) + (OwnerOf(city).Has("fortifications") ? 5 : 0) +
                    (OwnerOf(city).Region.leaderAbility == LeaderAbility.MosulCitadel ? MosulCitadelStrength : 0);
            var garrison = UnitAt(city.Coord);
            if (garrison != null && garrison.OwnerIndex == city.OwnerIndex && garrison.MeleeStrength > 0) s += 5;
            return s;
        }

        /// <summary>Вражеский город с целыми стенами — цель атаки вместо гарнизона.</summary>
        public City DefendedEnemyCity(HexCoord c, int ownerIndex)
        {
            var city = CityAt(c);
            return city != null && city.OwnerIndex != ownerIndex && AtWar(city.OwnerIndex, ownerIndex) && city.Walls > 0 ? city : null;
        }

        public static bool CanFight(Unit unit) => unit.Data.IsRanged || Combat.CanAttackAtAll(unit);

        /// <summary>Есть ли на клетке цель для этого юнита (город со стенами или вражеский юнит).</summary>
        public bool IsTarget(Unit unit, HexCoord c) =>
            DefendedEnemyCity(c, unit.OwnerIndex) != null || EnemyAt(c, unit.OwnerIndex) != null;

        /// <summary>Можно ли атаковать клетку прямо сейчас, без подхода.</summary>
        public bool CanAttackNow(Unit unit, HexCoord c)
        {
            if (!CanFight(unit) || unit.MovesLeft <= 0 || !IsTarget(unit, c)) return false;
            int dist = unit.Coord.DistanceTo(c);
            return unit.Data.IsRanged ? dist <= unit.Data.range : dist == 1;
        }

        /// <summary>Прогноз: урон цели, ответный урон, описание цели.</summary>
        public (int dealt, int taken, string target) PreviewAttack(Unit unit, HexCoord c)
        {
            var city = DefendedEnemyCity(c, unit.OwnerIndex);
            if (city != null)
            {
                int cs = CityStrength(city);
                return unit.Data.IsRanged
                    ? (Combat.DamageTo(unit.RangedStrength + unit.AttackBonus, cs), 0, $"стены {city.Data.displayName}")
                    : (Combat.DamageTo(unit.MeleeStrength + unit.AttackBonus, cs), Combat.DamageTo(cs, unit.MeleeStrength + unit.AttackBonus), $"стены {city.Data.displayName}");
            }
            var enemy = EnemyAt(c, unit.OwnerIndex);
            if (enemy == null) return (0, 0, null);
            int bonus = DefenseBonus(c);
            if (unit.Data.IsRanged) return (Combat.RangedPreview(unit, enemy, bonus), 0, enemy.Data.displayName);
            var (d, t) = Combat.Preview(unit, enemy, bonus);
            return (d, t, enemy.Data.displayName);
        }

        /// <summary>Атаковать клетку: подойти при необходимости и ударить/выстрелить.</summary>
        public bool AttackTarget(Unit unit, HexCoord target)
        {
            try { return AttackTargetCore(unit, target); }
            finally { Vision.RefreshAll(); }
        }

        private bool AttackTargetCore(Unit unit, HexCoord target)
        {
            if (!CanFight(unit) || Winner != null || !IsTarget(unit, target)) return false;
            int reach = unit.Data.IsRanged ? unit.Data.range : 1;
            if (unit.Coord.DistanceTo(target) > reach)
            {
                var path = PathToAdjacent(unit, target);
                if (path == null) return false;
                MoveUnit(unit, path, unit.Data.IsRanged ? c => c.DistanceTo(target) <= reach : (Func<HexCoord, bool>)null);
            }
            if (!CanAttackNow(unit, target)) return false;

            var city = DefendedEnemyCity(target, unit.OwnerIndex);
            if (city != null) StrikeCity(unit, city);
            else StrikeUnit(unit, EnemyAt(target, unit.OwnerIndex));
            Turns.RemoveDead();
            return true;
        }

        private void CountBattle(params int[] owners)
        {
            foreach (var o in owners.Distinct()) Players[o].BattlesThisTurn++;
        }

        private void StrikeUnit(Unit unit, Unit enemy)
        {
            CountBattle(unit.OwnerIndex, enemy.OwnerIndex);
            int bonus = DefenseBonus(enemy.Coord);
            string msg;
            if (unit.Data.IsRanged)
            {
                int dealt = Combat.ResolveRanged(unit, enemy, bonus);
                msg = $"{Name(unit)} стреляет: {Name(enemy)} −{dealt}";
            }
            else
            {
                var (dealt, taken) = Combat.Resolve(unit, enemy, bonus);
                msg = $"{Name(unit)} атакует: {Name(enemy)} −{dealt}" + (taken > 0 ? $", ответ −{taken}" : "");
            }
            if (bonus > 0) msg += $" (защита местности +{bonus})";
            if (!enemy.IsAlive) msg += $". {Name(enemy)} уничтожен";
            if (!unit.IsAlive) msg += $". {Name(unit)} погиб";
            Message?.Invoke(msg);
        }

        private void StrikeCity(Unit unit, City city)
        {
            CountBattle(unit.OwnerIndex, city.OwnerIndex);
            int cs = CityStrength(city);
            int dealt, taken = 0;
            if (unit.Data.IsRanged) dealt = Combat.DamageTo(unit.RangedStrength + unit.AttackBonus, cs);
            else
            {
                dealt = Combat.DamageTo(unit.MeleeStrength + unit.AttackBonus, cs);
                taken = Combat.DamageTo(cs, unit.MeleeStrength + unit.AttackBonus);
            }
            if (Players[unit.OwnerIndex].Has("siege")) dealt = (int)Math.Round(dealt * 1.5f);
            unit.SpendAllMoves();
            unit.MarkActed();
            unit.NotifyAttacked(city.Coord);
            city.DamageWalls(dealt);
            if (taken > 0) unit.TakeDamage(taken);
            unit.GainExperience(1);
            var msg = $"{Name(unit)} штурмует {city.Data.displayName}: стены −{dealt}" + (taken > 0 ? $", ответ −{taken}" : "");
            if (city.Walls == 0) msg += ". Стены пали — город можно занять";
            if (!unit.IsAlive) msg += $". {Name(unit)} погиб";
            Message?.Invoke(msg);
            CityChanged?.Invoke(city);
        }

        /// <summary>Города игрока стреляют по самому раненому врагу в радиусе.</summary>
        private void CitiesShoot(PlayerState player)
        {
            foreach (var city in Cities.Where(c => c.OwnerIndex == player.Index && c.Walls > 0).ToList())
            {
                var target = Players.Where(p => AtWar(p.Index, player.Index)).SelectMany(p => p.Units)
                    .Where(u => u.IsAlive && u.Coord.DistanceTo(city.Coord) <= City.Range)
                    .OrderBy(u => u.Health).FirstOrDefault();
                if (target == null) continue;
                int dealt = Combat.DamageTo(CityStrength(city), target.MeleeStrength + DefenseBonus(target.Coord));
                CityShot?.Invoke(city, target.Coord);
                target.TakeDamage(dealt);
                var msg = $"{city.Data.displayName} обстреливает: {Name(target)} −{dealt}";
                if (!target.IsAlive) msg += $". {Name(target)} уничтожен";
                Message?.Invoke(msg);
            }
            Turns.RemoveDead();
        }

        /// <summary>Лечение юнита, который не действовал в прошлый ход.</summary>
        public const int MercyHeal = 10;
        public const int MosulCitadelStrength = 5;
        public const int LionheartAttack = 3;

        /// <summary>Кылыч-Арслан II: конница +1 к движению (пересчитывается и при загрузке).</summary>
        private void ApplyLeaderMovement(Unit unit) =>
            unit.MovementBonus = unit.Data.mounted && Players[unit.OwnerIndex].Region.leaderAbility == LeaderAbility.SteppeRiders ? 1 : 0;

        public int HealAmount(Unit unit)
        {
            var owner = Players[unit.OwnerIndex];
            int bonus = (owner.Has("medicine") ? 5 : 0) + (owner.Has("supply") ? 5 : 0) +
                        (owner.Region.leaderAbility == LeaderAbility.Mercy ? MercyHeal : 0);
            var city = CityAt(unit.Coord);
            if (city != null && city.OwnerIndex == unit.OwnerIndex) return 20 + bonus;
            return (OwnerOfTile(unit.Coord) == unit.OwnerIndex ? 15 : 10) + bonus;
        }

        private string Name(Unit u) => $"{u.Data.displayName} ({Players[u.OwnerIndex].Region.displayName})";

        // ---------- Экономика ----------

        /// <summary>Начало хода игрока: доход, лечение, ремонт стен, обстрел из городов.</summary>
        private void BeginPlayerTurn(PlayerState player)
        {
            try { BeginPlayerTurnCore(player); }
            finally { Vision.RefreshAll(); }
        }

        private void BeginPlayerTurnCore(PlayerState player)
        {
            if (Winner != null) return;
            player.Gold += IncomeOf(player);
            foreach (var unit in player.Units)
            {
                if (!unit.Acted) unit.Heal(HealAmount(unit));
                unit.ClearActed();
            }
            foreach (var city in Cities.Where(c => c.OwnerIndex == player.Index).ToList())
            {
                city.BeginOwnerTurn(player.Has("fortifications") ? 10 : 0, IsBlockaded(city));
                GrowAndProduce(player, city);
            }
            Trade.PlayTurn(player);
            CollectTaxes(player);
            Tech.PlayTurn(player);
            CitiesShoot(player);
        }

        /// <summary>Клетка для нового юнита: сам город, иначе свободная соседняя.</summary>
        public HexCoord? SpawnSpot(City city)
        {
            if (IsFreeLand(city.Coord)) return city.Coord;
            for (int d = 0; d < 6; d++)
            {
                var c = city.Coord.Neighbor(d);
                if (IsFreeLand(c) && !IsForeignCity(c, city.OwnerIndex)) return c;
            }
            return null;
        }

        public bool CanBuy(PlayerState player, City city, UnitData data, out string reason)
        {
            reason = null;
            if (Winner != null) reason = "партия окончена";
            else if (city.OwnerIndex != player.Index) reason = "чужой город";
            else if (data.canFoundCity && city.Population < 2) reason = "нужно население 2";
            else if (MissingGoods(city, data.goodsCost) is string lacking) reason = lacking;
            else if (player.Gold < BuyCost(player, data)) reason = "не хватает золота";
            else if (SpawnSpot(city) == null) reason = "нет места рядом с городом";
            return reason == null;
        }

        /// <summary>Покупка юнита за золото. Купленный юнит ходит со следующего хода.</summary>
        public Unit Buy(PlayerState player, City city, UnitData data)
        {
            if (!CanBuy(player, city, data, out _)) return null;
            player.Gold -= BuyCost(player, data);
            if (data.canFoundCity) LosePopulation(city, "ушли поселенцы");
            TakeGoods(city, data.goodsCost);
            var unit = Spawn(data, player, SpawnSpot(city).Value);
            unit.SpendAllMoves();
            ArmFromCity(unit, city);
            Message?.Invoke($"{city.Data.displayName}: куплен {data.displayName} за {BuyCost(player, data)} золота");
            return unit;
        }

        public const int ArmingWeapons = 2;
        public const int ArmingBonus = 3;

        /// <summary>Боевой юнит, нанятый в городе с оружием на складе, забирает 2 оружия и получает +3 к силе.</summary>
        private void ArmFromCity(Unit unit, City city)
        {
            var weapons = Goods.FirstOrDefault(g => g.id == "weapons");
            if (weapons == null || !GameState.CanFight(unit) || !city.Warehouse.Has(weapons, ArmingWeapons)) return;
            city.Warehouse.Take(weapons, ArmingWeapons);
            int bonus = Players[unit.OwnerIndex].Has("damascus") ? 5 : ArmingBonus;
            unit.BonusStrength += bonus;
            Message?.Invoke($"{city.Data.displayName}: {unit.Data.displayName} получает оружие со склада (+{bonus} к силе)");
        }

        public Unit Spawn(UnitData data, PlayerState player, HexCoord coord)
        {
            var unit = new Unit(data, player.Index, coord, NextUnitId++);
            unit.BonusStrength += Tech.NewUnitBonus(player, unit);
            if (player.Region.leaderAbility == LeaderAbility.Lionheart && CanFight(unit)) unit.AttackBonus = LionheartAttack;
            ApplyLeaderMovement(unit);
            unit.Promoted += u => Message?.Invoke($"{Name(u)} получает звание «{u.LevelName}» (+{u.Level * Unit.StrengthPerLevel} к силе)");
            player.Units.Add(unit);
            UnitCreated?.Invoke(unit);
            Vision.Refresh(player);
            return unit;
        }

        // ---------- Районы и дороги ----------

        public bool HasDistrict(City city, DistrictData d) =>
            d == Market ? city.HasMarket : d == Port ? city.HasPort : false;

        private void SetDistrict(City city, DistrictData d, HexCoord at)
        {
            if (d == Market) city.MarketCoord = at;
            else if (d == Port) city.PortCoord = at;
            Grid.GetTile(at).Feature = TileFeature.None;
            // Вырубленный лес меняет стоимость пути караванов: кэш маршрутов должен это увидеть.
            Trade.ClearRouteCache();
        }

        public bool IsDistrictTile(HexCoord c) => Cities.Any(ci => ci.MarketCoord == c || ci.PortCoord == c);

        public bool IsCoastal(HexCoord c) => Grid.Neighbors(c).Any(t => t.Terrain.IsWater());

        /// <summary>Подходит ли клетка под район: своя, рядом с городом, суша, свободна; порт — у воды.</summary>
        public bool DistrictTileOk(City city, DistrictData d, HexCoord c) =>
            city.Territory.Contains(c) && c.DistanceTo(city.Coord) == 1 && Grid.TryGetTile(c, out var t) &&
            t.Terrain.IsPassable() && CityAt(c) == null && !IsDistrictTile(c) && (d != Port || IsCoastal(c));

        public HexCoord? DistrictSpot(City city, DistrictData d)
        {
            foreach (var c in Enumerable.Range(0, 6).Select(i => city.Coord.Neighbor(i)))
                if (DistrictTileOk(city, d, c)) return c;
            return null;
        }

        /// <summary>Город, для которого строитель может поставить район на своей клетке.</summary>
        public City DistrictCityFor(Unit unit, DistrictData d, out string reason)
        {
            reason = null;
            if (d == null || unit.Data.buildCharges <= 0) { reason = "не строитель"; return null; }
            if (unit.BuildCharges <= 0 || unit.MovesLeft <= 0) { reason = "нет очков движения"; return null; }
            var city = Cities.FirstOrDefault(c => c.OwnerIndex == unit.OwnerIndex && c.Coord.DistanceTo(unit.Coord) == 1);
            if (city == null) { reason = "встаньте рядом со своим городом"; return null; }
            if (HasDistrict(city, d)) { reason = $"в городе {city.Data.displayName} уже есть"; return null; }
            if (!DistrictTileOk(city, d, unit.Coord)) { reason = d == Port ? "нужна своя клетка у воды" : "клетка не подходит"; return null; }
            return city;
        }

        public bool BuildDistrict(Unit unit, DistrictData d)
        {
            var city = DistrictCityFor(unit, d, out _);
            if (city == null) return false;
            SetDistrict(city, d, unit.Coord);
            unit.UseBuildCharge();
            Turns.RemoveDead();
            CityChanged?.Invoke(city);
            if (d == Port) RoadsChanged?.Invoke();
            Message?.Invoke($"{city.Data.displayName}: построен {d.displayName}");
            return true;
        }

        public bool BuildMarket(Unit unit) => BuildDistrict(unit, Market);

        /// <summary>Дорогу может проложить строитель на своей или ничьей суше (постройки не тратит, только ход).</summary>
        public string CanBuildRoad(Unit unit)
        {
            if (unit.Data.buildCharges <= 0) return "не строитель";
            if (unit.MovesLeft <= 0) return "нет очков движения";
            var t = Grid.GetTile(unit.Coord);
            if (!t.Terrain.IsPassable() || CityAt(unit.Coord) != null) return "здесь дорога не нужна";
            if (t.HasRoad) return "дорога уже есть";
            var owner = OwnerOfTile(unit.Coord);
            if (owner != null && owner != unit.OwnerIndex) return "чужая земля";
            return null;
        }

        public bool BuildRoad(Unit unit)
        {
            if (CanBuildRoad(unit) != null) return false;
            Grid.GetTile(unit.Coord).HasRoad = true;
            unit.SpendAllMoves();
            unit.MarkActed();
            RoadsChanged?.Invoke();
            return true;
        }
    }
}

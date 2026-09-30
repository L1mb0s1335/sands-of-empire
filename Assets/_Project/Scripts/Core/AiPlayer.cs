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
    /// ИИ региона. Экономика: строитель ставит рынок, золото тратится на юнитов.
    /// Армия: каждый боевой юнит идёт к ближайшему юниту или городу противника и атакует,
    /// лучники стреляют с дистанции, разведчики занимают пустые города.
    /// </summary>
    public partial class AiPlayer
    {
        private readonly GameState _game;
        private readonly List<UnitData> _shop;
        private readonly DistrictData _market;
        private readonly List<Runeterra.Economy.BuildingData> _buildings;

        /// <summary>Сколько городов ИИ стремится иметь.</summary>
        public const int DesiredCities = 4;

        public AiPlayer(GameState game, List<UnitData> shop, DistrictData market, List<Runeterra.Economy.BuildingData> buildings)
        {
            _game = game;
            _shop = shop;
            _market = market;
            _buildings = buildings ?? new List<Runeterra.Economy.BuildingData>();
        }

        /// <summary>Сколько боевых юнитов нужно, чтобы идти в наступление.</summary>
        public const int ArmyToAttack = 3;

        private readonly HashSet<int> _attacking = new HashSet<int>();
        /// <summary>id юнитов, идущих в наступление (для сохранения).</summary>
        internal HashSet<int> Attacking => _attacking;

        /// <summary>План армии на ход: наступаем ли и на какой город.</summary>
        private struct Plan
        {
            public bool Attacking;
            public City Target;
            public HexCoord? Explore;
        }

        public void PlayTurn(PlayerState player)
        {
            _fields.Clear();
            PlayDiplomacy(player);
            PlayForeignTrade(player);
            var plan = MakePlan(player);
            foreach (var unit in player.Units.ToList())
            {
                if (_game.Winner != null) return;
                if (!unit.IsAlive || unit.MovesLeft <= 0) continue;
                if (unit.Data.buildCharges > 0) PlayBuilder(unit, player);
                else if (unit.Data.canFoundCity) PlaySettler(unit, player);
                else PlayMilitary(unit, player, plan);
            }
            if (_game.Winner != null) return;
            DraftIfThreatened(player);
            ChooseResearch(player);
            ManageTreasury(player);
            ChooseProduction(player);
            Shop(player);
        }

        private Plan MakePlan(PlayerState player)
        {
            var army = player.Units.Where(u => u.IsAlive && GameState.CanFight(u)).ToList();
            var capital = _game.CapitalOf(player);
            var home = capital?.Coord ?? (army.Count > 0 ? army[0].Coord : default);
            // Цель — города тех, с кем идёт война: сначала город по претензии, затем ближайший разведанный.
            bool atWar = _game.Diplomacy.EnemiesOf(player.Index).Any();
            var claimed = new HashSet<string>(_game.Diplomacy.Claims.Where(c => c.Owner == player.Index).Select(c => c.CityId));
            var target = _game.Cities.Where(c => _game.AtWar(c.OwnerIndex, player.Index) && _game.Vision.IsExplored(player.Index, c.Coord))
                .OrderBy(c => claimed.Contains(c.Data.id) ? 0 : 1).ThenBy(c => c.Coord.DistanceTo(home)).FirstOrDefault();

            // Наступаем, когда армия собрана у столицы; при больших потерях — отходим и копим силы.
            // Собранной считаем армию, если у столицы стоит не меньше 3 и хотя бы половина юнитов
            // (все сразу у стен не поместятся).
            int gathered = army.Count(u => u.Coord.DistanceTo(home) <= 3);
            if (!_attacking.Contains(player.Index) && army.Count >= ArmyToAttack && gathered >= Math.Max(ArmyToAttack, army.Count / 2))
                _attacking.Add(player.Index);
            if (army.Count < 2) _attacking.Remove(player.Index);

            // Войны нет — армия стоит дома. Цель войны не видна — идём разведывать.
            var explore = atWar && target == null ? _game.Vision.Frontier(player.Index, army.Count > 0 ? army[0].Coord : home) : null;
            return new Plan
            {
                Attacking = atWar && _attacking.Contains(player.Index) && (target != null || explore != null),
                Target = target,
                Explore = explore,
            };
        }

        // ---------- Экономика ----------

        /// <summary>Производство: сначала поселенцы до нужного числа городов, затем рынок, затем армия.</summary>
        private void ChooseProduction(PlayerState player)
        {
            var cities = _game.Cities.Where(c => c.OwnerIndex == player.Index).ToList();
            foreach (var city in cities)
            {
                if (city.CurrentBuild != null) continue;
                bool settling = player.Units.Any(u => u.Data.canFoundCity) || cities.Any(c => c.CurrentBuild?.Unit?.canFoundCity == true);
                var settler = _shop.FirstOrDefault(d => d.canFoundCity);
                BuildItem item = null;
                int desired = Personality(player.Index).Goal == AiGoal.Growth ? DesiredCities + 2 : DesiredCities;
                if (settler != null && !settling && cities.Count < desired && city.Population >= 2 && FindCitySpot(player, city.Coord) != null)
                    item = new BuildItem(settler);
                else if (_market != null && !city.HasMarket && _game.CanBuild(city, new BuildItem(_market), out _))
                    item = new BuildItem(_market);
                else if (_game.Port != null && !city.HasPort && _game.CanBuild(city, new BuildItem(_game.Port), out _))
                    item = new BuildItem(_game.Port);
                else if (ChooseBuilding(city) is BuildingData building)
                    item = new BuildItem(building);
                else if (WantsArmy(player))
                    item = new BuildItem(NextMilitary(player, city));
                else if (_buildings.FirstOrDefault(b => _game.CanBuild(city, new BuildItem(b), out _)) is BuildingData any)
                    item = new BuildItem(any);
                _game.SetBuild(city, item);
            }
        }

        /// <summary>
        /// Постройка: сначала амбар; затем мастерская, для которой в городе есть сырьё,
        /// с предпочтением уже начатого кластера и самой дорогой продукции.
        /// </summary>
        private Runeterra.Economy.BuildingData ChooseBuilding(City city)
        {
            Runeterra.Economy.BuildingData best = null;
            float bestScore = 0f;
            // Вода: если её мало (или вот-вот станет мало) — колодец/цистерна/канал.
            if (_game.WaterUse(city) + 1f > _game.WaterCapacity(city))
            {
                var water = _buildings.Where(b => b.waterBonus > 0 && _game.CanBuild(city, new BuildItem(b), out _))
                    .OrderByDescending(b => b.waterBonus).FirstOrDefault();
                if (water != null) return water;
            }
            // Беды большого города.
            if (_game.CrimeRate(city) >= 0.1f && _buildings.FirstOrDefault(b => b.crimeReduction > 0 && _game.CanBuild(city, new BuildItem(b), out _)) is BuildingData guard) return guard;
            if (_game.EpidemicChance(city) >= 0.03f && _buildings.FirstOrDefault(b => b.epidemicReduction > 0 && _game.CanBuild(city, new BuildItem(b), out _)) is BuildingData med) return med;

            foreach (var b in _buildings)
            {
                if (!_game.CanBuild(city, new BuildItem(b), out _)) continue;
                if (b.waterBonus > 0 || b.crimeReduction > 0 || b.epidemicReduction > 0 || b.fireReduction > 0) continue;
                if (!b.IsWorkshop) return b; // амбар и прочие хозяйственные — в первую очередь
                if (city.FreeWorkers <= 0) continue; // некому работать
                // Сырьё есть на складе или добывается.
                bool hasInputs = b.inputs.All(i => i.good == null || city.Warehouse.Get(i.good) >= i.amount * 2 ||
                                                   Enumerable.Range(0, 4).Sum(sz => _game.GoodOutput(city, i.good, (Runeterra.Economy.Season)sz)) >= i.amount);
                if (!hasInputs) continue;
                // Уже стоящие мастерские простаивают без сырья — ещё одна не нужна.
                if (city.Has(b) && _game.WorkshopBatches(city, b) < b.batchesPerTurn * city.Count(b)) continue;
                float score = b.output.good.basePrice * b.output.amount - b.inputs.Sum(i => i.good != null ? i.good.basePrice * i.amount : 0f);
                score *= _game.ClusterMultiplier(city, b) + (city.Has(b) ? 0.25f : 0f);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        /// <summary>Следующий боевой юнит: лучники к пехоте поровну; конница, если в городе есть кони.</summary>
        private UnitData NextMilitary(PlayerState player, City city = null)
        {
            int melee = player.Units.Count(u => u.Data.role == UnitRole.Melee);
            int ranged = player.Units.Count(u => u.Data.IsRanged);
            if (ranged < melee) return _shop.First(d => d.IsRanged);
            var cavalry = _shop.FirstOrDefault(d => d.mounted && d.role == UnitRole.Melee);
            if (cavalry != null && city != null && _game.MissingGoods(city, cavalry.goodsCost) == null) return cavalry;
            return _shop.First(d => d.role == UnitRole.Melee && !d.mounted);
        }

        /// <summary>Исследования: самый дешёвый доступный узел; при равенстве — знания, земля, торговля, ремесло, управление, война.</summary>
        private void ChooseResearch(PlayerState player)
        {
            if (player.Researching != null) return;
            var order = new[] { Runeterra.Tech.TechBranch.Knowledge, Runeterra.Tech.TechBranch.LandAndWater, Runeterra.Tech.TechBranch.Trade,
                Runeterra.Tech.TechBranch.Craft, Runeterra.Tech.TechBranch.Governance, Runeterra.Tech.TechBranch.War };
            // Любимая ветвь правителя идёт как бы на 40% дешевле.
            var fav = Personality(player.Index).FavoriteBranch;
            var next = _game.Tech.All.Where(t => _game.Tech.IsAvailable(player, t))
                .OrderBy(t => t.baseCost * (t.branch == fav ? 0.6f : 1f)).ThenBy(t => System.Array.IndexOf(order, t.branch)).FirstOrDefault();
            if (next != null) _game.Tech.Choose(player, next);
        }

        /// <summary>
        /// Умеренные налоги без штрафов и резерв на засуху/пожар. Богатая казна в мирное время —
        /// налоги ниже (быстрее растут города) и резерв больше.
        /// </summary>
        private void ManageTreasury(PlayerState player)
        {
            bool rich = player.Gold > 250 && !_game.Diplomacy.EnemiesOf(player.Index).Any();
            player.LandTax = rich ? 0.05f : 0.1f;
            player.PeopleTax = rich ? 0f : 0.05f;
            player.LuxuryTax = 0.1f;
            if (rich && player.Reserve < 80) { player.Gold -= 20; player.Reserve += 20; }
            // Торговый характер держит пошлину ниже — купцов больше.
            player.Tariff = (float)System.Math.Round(0.15f - 0.1f * Personality(player.Index).Trade, 2);
            if (player.Reserve < 30 && player.Gold >= 70) { player.Gold -= 10; player.Reserve += 10; }

            // Займы: берём, когда враг у ворот, а казна пуста; возвращаем при избытке золота.
            bool threatened = _game.Cities.Where(c => c.OwnerIndex == player.Index).Any(c =>
                _game.Players.Where(p => _game.AtWar(p.Index, player.Index)).SelectMany(p => p.Units)
                    .Any(u => u.IsAlive && u.MeleeStrength > 0 && u.Coord.DistanceTo(c.Coord) <= 4 && _game.Vision.IsVisible(player.Index, u.Coord)));
            char rating = _game.CreditRating(player);
            if (threatened && player.Gold < 40 && (rating == 'A' || rating == 'B')) _game.Borrow(player);
            else if (!threatened && player.Debt > 0 && player.Gold > 120) _game.Repay(player);
        }

        /// <summary>Город без защитника, к которому подошёл враг, призывает ополчение.</summary>
        private void DraftIfThreatened(PlayerState player)
        {
            foreach (var city in _game.Cities.Where(c => c.OwnerIndex == player.Index).ToList())
            {
                bool threat = _game.Players.Where(p => _game.AtWar(p.Index, player.Index)).SelectMany(p => p.Units)
                    .Any(u => u.IsAlive && u.MeleeStrength > 0 && u.Coord.DistanceTo(city.Coord) <= 2 && _game.Vision.IsVisible(player.Index, u.Coord));
                bool defended = player.Units.Any(u => u.IsAlive && GameState.CanFight(u) && u.Coord.DistanceTo(city.Coord) <= 1);
                if (threat && !defended && city.Population >= 3) _game.Draft(city, player);
            }
        }

        /// <summary>Золото — на армию, с небольшим запасом; лишнее — на ускорение построек.</summary>
        private void Shop(PlayerState player)
        {
            int spare = player.Gold - 20 * (_game.Tech.ArmyUpkeep(player) + 5);
            foreach (var city in _game.Cities.Where(c => c.OwnerIndex == player.Index).ToList())
            {
                if (spare <= 0) break;
                // Юнитам нет места — переключаем город на постройку и выкупаем её.
                if (city.CurrentBuild?.Unit != null && _game.SpawnSpot(city) == null && ChooseBuilding(city) is Runeterra.Economy.BuildingData b)
                    _game.SetBuild(city, new BuildItem(b));
                int cost = _game.RushCost(city);
                if (cost > 0 && cost <= spare && _game.CanRush(city, player) == null && _game.Rush(city, player)) spare -= cost;
            }

            var builder = _shop.FirstOrDefault(d => d.buildCharges > 0);
            var capital = _game.CapitalOf(player);

            // Лишнее золото — на поселенцев, пока есть куда расти.
            var settler = _shop.FirstOrDefault(d => d.canFoundCity);
            int cities = _game.Cities.Count(c => c.OwnerIndex == player.Index);
            int desired = Personality(player.Index).Goal == AiGoal.Growth ? DesiredCities + 2 : DesiredCities + 1;
            if (settler != null && player.Gold > 200 && cities < desired && !player.Units.Any(u => u.Data.canFoundCity))
            {
                var from = _game.Cities.Where(c => c.OwnerIndex == player.Index && c.Population >= 3 && FindCitySpot(player, c.Coord) != null)
                    .OrderByDescending(c => c.Population).FirstOrDefault();
                if (from != null && _game.CanBuy(player, from, settler, out _)) _game.Buy(player, from, settler);
            }
            if (builder != null && capital != null && NeedBuilder(player) && player.Gold >= _game.BuyCost(player, builder) + 20 &&
                _game.CanBuy(player, capital, builder, out _))
                _game.Buy(player, capital, builder);

            foreach (var city in _game.Cities.Where(c => c.OwnerIndex == player.Index).ToList())
            {
                if (!WantsArmy(player)) break;
                var want = NextMilitary(player, city);
                // Армию, которую не прокормить, не покупаем: доход должен покрывать содержание.
                // Исключение — большой запас золота: его хватит на содержание надолго.
                if (_game.IncomeOf(player) - _game.Tech.ArmyUpkeep(player) < 3 && player.Gold < 20 * (_game.Tech.ArmyUpkeep(player) + 5)) continue;
                if (player.Gold < _game.BuyCost(player, want) + 15 || !_game.CanBuy(player, city, want, out _)) continue;
                _game.Buy(player, city, want);
            }
        }

        // ---------- Поселенцы ----------

        private void PlaySettler(Unit unit, PlayerState player)
        {
            var spot = FindCitySpot(player, unit.Coord, unit);
            if (spot == null) return;
            if (unit.Coord == spot.Value) { _game.FoundCity(unit); return; }
            var path = _game.PathFor(unit, spot.Value);
            if (path == null) return;
            _game.MoveUnit(unit, path);
            if (unit.Coord == spot.Value && unit.MovesLeft > 0) _game.FoundCity(unit);
        }

        /// <summary>Лучшее место для города недалеко от from: еда, производство, берег; подальше от врага.</summary>
        private HexCoord? FindCitySpot(PlayerState player, HexCoord from, Unit settler = null)
        {
            var settlerData = settler?.Data ?? _shop.FirstOrDefault(d => d.canFoundCity);
            if (settlerData == null) return null;
            var probe = settler ?? new Unit(settlerData, player.Index, from);
            var enemyCities = _game.Cities.Where(c => c.OwnerIndex != player.Index).ToList();
            HexCoord? best = null;
            float bestScore = float.MinValue;
            foreach (var tile in _game.Grid.Tiles)
            {
                int dist = tile.Coord.DistanceTo(from);
                if (dist > 8 || _game.CanFoundCity(probe, tile.Coord) != null) continue;
                if (enemyCities.Any(c => c.Coord.DistanceTo(tile.Coord) < 6)) continue;
                if (_game.IsOccupied(tile.Coord) && _game.UnitAt(tile.Coord) != settler) continue;
                float score = 0f;
                bool coastal = false;
                foreach (var n in _game.Grid.Neighbors(tile.Coord))
                {
                    score += n.Terrain switch
                    {
                        TerrainType.Grassland => 3f,
                        TerrainType.Plains => 2f,
                        TerrainType.Hills => 2f,
                        TerrainType.Coast => 1.5f,
                        _ => 0f,
                    };
                    if (n.Feature == TileFeature.Forest) score += 1f;
                    if (n.Feature == TileFeature.Oasis) score += 3f;
                    if (n.Terrain.IsWater()) coastal = true;
                }
                if (coastal) score += 2f;
                score -= dist * 0.8f;
                if (score > bestScore) { bestScore = score; best = tile.Coord; }
            }
            return best;
        }

        /// <summary>Строитель: рынки у городов без рынка, затем дороги между своими городами.</summary>
        private void PlayBuilder(Unit unit, PlayerState player)
        {
            if (_market != null && _game.DistrictCityFor(unit, _market, out _) != null) { _game.BuildDistrict(unit, _market); return; }

            var marketSpots = _market == null ? new List<HexCoord>() : _game.Cities
                .Where(c => c.OwnerIndex == player.Index && !c.HasMarket)
                .SelectMany(c => Enumerable.Range(0, 6).Select(d => c.Coord.Neighbor(d)).Where(t => _game.DistrictTileOk(c, _market, t)))
                .Where(t => !_game.IsOccupied(t) || _game.UnitAt(t) == unit)
                .OrderBy(t => t.DistanceTo(unit.Coord)).ToList();
            if (marketSpots.Count > 0)
            {
                GoAndDo(unit, marketSpots[0], () => _game.BuildDistrict(unit, _market));
                return;
            }

            var roadTile = NextRoadTile(player, unit);
            if (roadTile == null) return;
            GoAndDo(unit, roadTile.Value, () => _game.BuildRoad(unit));
        }

        private void GoAndDo(Unit unit, HexCoord spot, System.Action action)
        {
            if (unit.Coord != spot)
            {
                var path = _game.PathFor(unit, spot);
                if (path == null) return;
                _game.MoveUnit(unit, path);
            }
            if (unit.Coord == spot && unit.MovesLeft > 0) action();
        }

        /// <summary>Ближайшая клетка без дороги на сухопутном пути от столицы к другим своим городам.</summary>
        private HexCoord? NextRoadTile(PlayerState player, Unit near)
        {
            var capital = _game.CapitalOf(player);
            if (capital == null) return null;
            HexCoord? best = null;
            int bestDist = int.MaxValue;
            foreach (var city in _game.Cities.Where(c => c.OwnerIndex == player.Index && c != capital))
            {
                var route = _game.Trade.LandRoute(capital, city);
                if (route == null) continue;
                foreach (var c in route)
                {
                    var t = _game.Grid.GetTile(c);
                    if (t.HasRoad || t.CityId != null) continue;
                    var owner = _game.OwnerOfTile(c);
                    if (owner != null && owner != player.Index) continue;
                    if (_game.IsOccupied(c) && _game.UnitAt(c) != near) continue;
                    int d = c.DistanceTo(near.Coord);
                    if (d < bestDist) { bestDist = d; best = c; }
                    break;
                }
            }
            return best;
        }

        /// <summary>Нужен ли строитель: есть город без рынка или недостроенные дороги.</summary>
        private bool NeedBuilder(PlayerState player) =>
            !player.Units.Any(u => u.Data.buildCharges > 0) &&
            (_game.Cities.Any(c => c.OwnerIndex == player.Index && !c.HasMarket) ||
             _game.Cities.Count(c => c.OwnerIndex == player.Index) > 1);

        // ---------- Армия ----------

        private void PlayMilitary(Unit unit, PlayerState player, Plan plan)
        {
            var enemyCities = _game.Cities.Where(c => _game.AtWar(c.OwnerIndex, player.Index) && _game.Vision.IsExplored(player.Index, c.Coord)).ToList();
            if (!GameState.CanFight(unit))
            {
                PlayScout(unit, player, enemyCities);
                return;
            }

            var enemyUnits = _game.Players.Where(p => _game.AtWar(p.Index, player.Index)).SelectMany(p => p.Units)
                .Where(u => u.IsAlive && _game.Vision.IsVisible(player.Index, u.Coord)).Select(u => u.Coord).ToList();

            // 1. Цель в досягаемости — бьём самую раненую.
            var now = enemyUnits.Concat(enemyCities.Select(c => c.Coord))
                .Where(c => _game.CanAttackNow(unit, c))
                .OrderBy(c => _game.UnitAt(c)?.Health ?? _game.CityAt(c)?.Walls ?? 999)
                .ToList();
            if (now.Count > 0) { _game.AttackTarget(unit, now[0]); return; }

            // 2. Враг рядом (до 3 клеток) — вступаем в бой.
            var close = enemyUnits.Where(c => c.DistanceTo(unit.Coord) <= 3).OrderBy(c => c.DistanceTo(unit.Coord)).ToList();
            foreach (var c in close)
                if (Engage(unit, c)) return;

            // 3. Армия не собрана — к столице.
            if (!plan.Attacking)
            {
                Rally(unit, player);
                return;
            }

            // 4а. Цели не знаем — идём разведывать.
            if (plan.Target == null)
            {
                if (plan.Explore != null) _game.MoveUnit(unit, _game.PathFor(unit, plan.Explore.Value));
                return;
            }

            // 4. Наступление: у цели в одиночку не лезем — ждём, пока рядом соберутся свои.
            var target = plan.Target.Coord;
            int dist = unit.Coord.DistanceTo(target);
            int friendsNear = player.Units.Count(u => u != unit && u.IsAlive && GameState.CanFight(u) && u.Coord.DistanceTo(unit.Coord) <= 2);
            if (dist <= 5 && dist > 3 && friendsNear < ArmyToAttack - 1) return;
            if (plan.Target.Walls > 0 || _game.UnitAt(target) != null)
            {
                Engage(unit, target, useField: true);
                return;
            }
            if (unit.Data.canCapture) _game.MoveUnit(unit, FieldPath(unit, target) ?? _game.PathFor(unit, target));
            else Engage(unit, target, useField: true);
        }

        // ---------- Поле расстояний ----------

        /// <summary>Поля расстояний до общих целей (город-цель, столица) — одно на ход вместо поиска пути для каждого юнита.</summary>
        private readonly Dictionary<(int, HexCoord), Dictionary<HexCoord, int>> _fields = new Dictionary<(int, HexCoord), Dictionary<HexCoord, int>>();

        /// <summary>
        /// Обратный Дейкстра от цели: стоимость дойти до неё с каждой клетки для юнитов стороны
        /// (через свои юниты пройти можно, через чужих и чужие города — нет).
        /// </summary>
        private Dictionary<HexCoord, int> Field(int owner, HexCoord goal)
        {
            if (_fields.TryGetValue((owner, goal), out var f)) return f;
            var blocked = new HashSet<HexCoord>(_game.Players.Where(p => p.Index != owner).SelectMany(p => p.Units).Where(u => u.IsAlive).Select(u => u.Coord));
            blocked.UnionWith(_game.Cities.Where(c => c.OwnerIndex != owner).Select(c => c.Coord));
            blocked.Remove(goal);
            f = new Dictionary<HexCoord, int> { [goal] = 0 };
            var open = new SortedSet<(int cost, int q, int r)> { (0, goal.q, goal.r) };
            while (open.Count > 0)
            {
                var (cost, q, r) = open.Min;
                open.Remove(open.Min);
                var cur = new HexCoord(q, r);
                if (cost > f[cur]) continue;
                int enter = _game.Grid.GetTile(cur).MoveCost();
                foreach (var n in _game.Grid.Neighbors(cur))
                {
                    if (!n.Terrain.IsPassable() || blocked.Contains(n.Coord)) continue;
                    int nc = cost + (cur == goal ? 1 : enter);
                    if (f.TryGetValue(n.Coord, out var old) && old <= nc) continue;
                    f[n.Coord] = nc;
                    open.Add((nc, n.Coord.q, n.Coord.r));
                }
            }
            return _fields[(owner, goal)] = f;
        }

        /// <summary>Путь к цели по полю расстояний (null — цель недостижима).</summary>
        private List<HexCoord> FieldPath(Unit unit, HexCoord goal)
        {
            var f = Field(unit.OwnerIndex, goal);
            if (!f.ContainsKey(unit.Coord)) return null;
            var path = new List<HexCoord>();
            var cur = unit.Coord;
            while (cur != goal && path.Count < 200)
            {
                HexCoord? best = null;
                int bestCost = f[cur];
                foreach (var n in _game.Grid.Neighbors(cur))
                    if (f.TryGetValue(n.Coord, out var c) && c < bestCost) { bestCost = c; best = n.Coord; }
                if (best == null) return null;
                cur = best.Value;
                path.Add(cur);
            }
            return path;
        }

        /// <summary>Атаковать цель или подойти к ней. false — если к цели нет пути.</summary>
        private bool Engage(Unit unit, HexCoord target, bool useField = false)
        {
            if (_game.IsTarget(unit, target) && _game.AttackTarget(unit, target)) return true;
            List<HexCoord> approach;
            if (useField && FieldPath(unit, target) is List<HexCoord> full && full.Count > 0)
            {
                full.RemoveAt(full.Count - 1);
                approach = full;
            }
            else approach = _game.PathToAdjacent(unit, target);
            if (approach == null || approach.Count == 0) return false;
            _game.MoveUnit(unit, approach, StopForRanged(unit, target));
            return true;
        }

        /// <summary>Разведчик: занимает города без стен и защитника, иначе держится в 3 клетках от врага.</summary>
        private void PlayScout(Unit unit, PlayerState player, List<City> enemyCities)
        {
            foreach (var city in enemyCities.OrderBy(c => c.Coord.DistanceTo(unit.Coord)))
            {
                if (city.Walls == 0 && unit.Data.canCapture)
                {
                    var path = _game.PathFor(unit, city.Coord);
                    if (path == null) continue;
                    _game.MoveUnit(unit, path);
                    return;
                }
                if (unit.Coord.DistanceTo(city.Coord) <= 3) return;
                var approach = _game.PathToAdjacent(unit, city.Coord);
                if (approach == null) continue;
                _game.MoveUnit(unit, approach, c => c.DistanceTo(city.Coord) <= 3);
                return;
            }
            // Вражеских городов не знаем — разведка.
            var frontier = _game.Vision.Frontier(player.Index, unit.Coord);
            if (frontier != null) _game.MoveUnit(unit, _game.PathFor(unit, frontier.Value));
        }

        /// <summary>Идти к столице и ждать подкреплений. false — если стоять уже негде/незачем.</summary>
        private bool Rally(Unit unit, PlayerState player)
        {
            var capital = _game.CapitalOf(player);
            if (capital == null) return false;
            if (unit.Coord.DistanceTo(capital.Coord) <= 2) return true;
            var path = FieldPath(unit, capital.Coord);
            if (path == null) return false;
            _game.MoveUnit(unit, path, c => c.DistanceTo(capital.Coord) <= 2);
            return true;
        }

        private static System.Func<HexCoord, bool> StopForRanged(Unit unit, HexCoord target) =>
            unit.Data.IsRanged ? c => c.DistanceTo(target) <= unit.Data.range : (System.Func<HexCoord, bool>)null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Map;

namespace Runeterra.Core
{
    /// <summary>
    /// Живая торговля. Чистый C#.
    /// • Цена товара на рынке города зависит от запаса: мало — дорого, много — дёшево.
    /// • Торговцы-NPC сами отправляют караваны туда, где выгоднее (с учётом пошлины и дороги).
    /// • Караваны идут по суше (по дорогам быстрее) или морем между портами; могут пропасть.
    /// • Враг на пути останавливает караван; простоявший 3 хода караван разграблен.
    /// • Заморский рынок: чем больше вывезли товара, тем ниже там цена (восстанавливается со временем).
    /// Игрок получает пошлину с наценки каждой доставленной партии.
    /// </summary>
    public partial class TradeSystem
    {
        public const int MaxLoad = 6;
        public const float MinMarginShare = 0.25f;
        public const float TransportCostPerStep = 0.02f;
        public const float ExternalPremium = 1.4f;
        public const float BanditRiskPerTurn = 0.03f;
        public const float SeaRiskPerTurn = 0.02f;

        private readonly GameState _game;
        private readonly SortedDictionary<GoodData, float> _exportPressure = new SortedDictionary<GoodData, float>(ContentOrder.Goods);

        public List<Caravan> Caravans { get; } = new List<Caravan>();

        public event Action<Caravan> Dispatched;
        public event Action<Caravan> Moved;
        public event Action<Caravan, bool> Finished; // true — доставлен, false — пропал

        public TradeSystem(GameState game)
        {
            _game = game;
        }

        /// <summary>id следующего каравана (сохраняется: это ключ бросков риска в пути).</summary>
        public int NextCaravanId { get; internal set; } = 1;
        internal SortedDictionary<GoodData, float> ExportPressure => _exportPressure;
        internal SortedDictionary<(int, GoodData), float> YearExport => _yearExport;

        // ---------- Цены ----------

        /// <summary>Сколько товара город «хочет» держать: чем больше жителей, тем больше.</summary>
        public static float TargetStock(City city) => 6f + 2f * city.Population;

        /// <summary>Цена = база × (желаемый запас / (запас+2))^0.6, в пределах 0.35…3 от базы.</summary>
        public float Price(City city, GoodData good)
        {
            float ratio = TargetStock(city) / (city.Warehouse.Get(good) + 2f);
            float k = (float)DetMath.Pow(ratio, 0.6);
            return good.basePrice * Math.Max(0.35f, Math.Min(3f, k));
        }

        /// <summary>Заморская цена падает от вывоза: база × 1.4 / (1 + вывезено/30).</summary>
        public float ExternalPrice(GoodData good) =>
            good.basePrice * ExternalPremium / (1f + (_exportPressure.TryGetValue(good, out var p) ? p : 0f) / 30f);

        // ---------- Маршруты ----------

        /// <summary>Стоимость шага каравана по суше: дорога 1, иначе очки движения × 2.</summary>
        public static int LandStep(HexTile t)
        {
            if (!t.Terrain.IsPassable()) return TerrainRules.Impassable;
            if (t.HasRoad || t.CityId != null) return 1;
            return TerrainRules.MoveCost(t.Terrain, t.Feature) * 2;
        }

        private int SeaStep(HexTile t, HexCoord a, HexCoord b) =>
            t.Terrain.IsWater() || t.Coord == a || t.Coord == b ? 1 : TerrainRules.Impassable;

        /// <summary>
        /// Кэш сухопутных маршрутов (в обход вражеских городов). Сбрасывается, когда меняются дороги,
        /// города, войны и мир. Вражеские отряды на пути проверяются отдельно при отправке и в дороге.
        /// </summary>
        private readonly Dictionary<(HexCoord, HexCoord, int), List<HexCoord>> _routeCache = new Dictionary<(HexCoord, HexCoord, int), List<HexCoord>>();
        /// <summary>Морские пути зависят только от портов — кэш без сброса.</summary>
        private readonly Dictionary<(HexCoord, HexCoord), List<HexCoord>> _seaCache = new Dictionary<(HexCoord, HexCoord), List<HexCoord>>();
        private readonly Dictionary<HexCoord, List<HexCoord>> _exportCache = new Dictionary<HexCoord, List<HexCoord>>();

        public void ClearRouteCache() => _routeCache.Clear();


        public List<HexCoord> LandRoute(City from, City to)
        {
            var key = (from.Coord, to.Coord, from.OwnerIndex);
            if (_routeCache.TryGetValue(key, out var cached)) return cached;
            return _routeCache[key] = FindLandRoute(from, to);
        }

        private List<HexCoord> FindLandRoute(City from, City to)
        {
            int owner = from.OwnerIndex;
            var hostile = new HashSet<HexCoord>(_game.Cities.Where(c => _game.AtWar(c.OwnerIndex, owner)).Select(c => c.Coord));
            return HexPathfinder.FindPath(_game.Grid, from.Coord, to.Coord, hostile.Count == 0 ? null : (Func<HexCoord, bool>)hostile.Contains, LandStep);
        }

        public List<HexCoord> SeaRoute(HexCoord fromPort, HexCoord toPort)
        {
            if (_seaCache.TryGetValue((fromPort, toPort), out var cached)) return cached;
            return _seaCache[(fromPort, toPort)] = HexPathfinder.FindPath(_game.Grid, fromPort, toPort, null, t => SeaStep(t, fromPort, toPort));
        }

        /// <summary>Морской путь от порта до края карты (выход к заморскому рынку).</summary>
        public List<HexCoord> ExportRoute(HexCoord port)
        {
            if (_exportCache.TryGetValue(port, out var cached)) return cached;
            return _exportCache[port] = FindExportRoute(port);
        }

        private List<HexCoord> FindExportRoute(HexCoord port)
        {
            var edge = _game.Grid.Tiles.Where(t => t.Terrain == TerrainType.Ocean && _game.Grid.IsEdge(t.Coord))
                .OrderBy(t => t.Coord.DistanceTo(port)).FirstOrDefault();
            return edge == null ? null : SeaRoute(port, edge.Coord);
        }

        /// <summary>Вражеская клетка: город соперника или клетка с вражеским юнитом.</summary>
        private bool IsHostile(HexCoord c, int owner) =>
            _game.EnemyAt(c, owner) != null || _game.IsEnemyCity(c, owner);

        private int RouteCost(List<HexCoord> path, bool sea) =>
            path.Sum(c => sea ? 1 : LandStep(_game.Grid.GetTile(c)));

        // ---------- Ход торговцев ----------

        /// <summary>Начало хода владельца: караваны двигаются, затем торговцы отправляют новые.</summary>
        public void PlayTurn(PlayerState player)
        {
            player.TariffIncomeLastTurn = 0;
            player.SmuggledLastTurn = 0;
            player.CaravansDeliveredLastTurn = 0;
            CloseImportReport(player);
            if (_game.Season == Season.Sowing) CheckPriceCrash(player);
            foreach (var c in Caravans.Where(c => c.OwnerIndex == player.Index).ToList()) Advance(c, player);
            foreach (var good in _exportPressure.Keys.ToList()) _exportPressure[good] *= 0.9f;

            // Банкрот — купцы не работают; порченая монета отпугивает часть торговцев.
            if (_game.IsBankrupt(player)) return;
            int perCity = _game.Season == Season.Trade ? 2 : 1;
            foreach (var city in _game.Cities.Where(c => c.OwnerIndex == player.Index && c.HasMarket).ToList())
                for (int i = 0; i < perCity; i++)
                {
                    if (_game.Roll(DetRandom.Kind.MerchantStays, GameState.CityKey(city), player.Index, i) < player.Debasement) continue;
                    if (!DispatchBest(player, city)) break;
                }
        }

        private struct Offer
        {
            public GoodData Good;
            public float Amount;
            public City To;
            public bool Sea;
            public List<HexCoord> Path;
            public float Profit;
        }

        /// <summary>Торговец выбирает самую выгодную партию из этого города.</summary>
        private bool DispatchBest(PlayerState player, City from)
        {
            var best = new Offer { Profit = 0f };
            // Свои города и города партнёров по торговому соглашению (с рынком).
            var partners = _game.Cities.Where(c => c != from &&
                (c.OwnerIndex == player.Index || (c.HasMarket && CanTradeWith(player.Index, c.OwnerIndex)))).ToList();
            // Маршруты считаются один раз на партнёра (а не на каждый товар): поиск пути — самое дорогое.
            var routes = new List<(City to, List<HexCoord> path, bool sea, int cost)>();
            // Клетки у вражеских отрядов: по таким дорогам торговцы караван не отправят.
            var danger = new HashSet<HexCoord>();
            foreach (var u in _game.Players.Where(p => _game.AtWar(p.Index, player.Index)).SelectMany(p => p.Units))
                if (u.IsAlive && u.MeleeStrength > 0)
                {
                    danger.Add(u.Coord);
                    for (int d = 0; d < 6; d++) danger.Add(u.Coord.Neighbor(d));
                }
            foreach (var to in partners)
            {
                var land = LandRoute(from, to);
                // Торговцы не отправляют караван по дороге, которую сейчас держит враг.
                if (land != null && !(danger.Count > 0 && land.Any(danger.Contains))) routes.Add((to, land, false, RouteCost(land, false)));
                if (from.HasPort && to.HasPort && SeaRoute(from.PortCoord.Value, to.PortCoord.Value) is List<HexCoord> sea)
                    routes.Add((to, sea, true, RouteCost(sea, true)));
            }
            var export = from.HasPort ? ExportRoute(from.PortCoord.Value) : null;

            foreach (var good in _game.Goods)
            {
                float reserve = good == _game.Grain ? _game.DroughtGrainNeed(from) : 2f;
                float amount = Math.Min(MaxLoad + (player.Has("houses") ? 3 : 0), (float)Math.Floor(from.Warehouse.Get(good) - reserve));
                if (amount < 1f) continue;
                float buy = Price(from, good);

                foreach (var (to, path, bySea, cost) in routes)
                {
                    float sell = Price(to, good) * (1f - player.Tariff - ImportDuty(to.OwnerIndex, player.Index));
                    float profit = (sell - buy - TransportCostPerStep * cost * good.basePrice) * amount;
                    if (sell - buy < buy * MinMarginShare || profit <= best.Profit) continue;
                    best = new Offer { Good = good, Amount = amount, To = to, Sea = bySea, Path = path, Profit = profit };
                }

                if (export != null)
                {
                    float sell = ExternalPrice(good) * (player.Has("exchange") ? 1.15f : 1f) * (1f - player.Tariff);
                    float profit = (sell - buy - TransportCostPerStep * export.Count * good.basePrice) * amount;
                    if (sell - buy >= buy * MinMarginShare && profit > best.Profit)
                        best = new Offer { Good = good, Amount = amount, To = null, Sea = true, Path = export, Profit = profit };
                }
            }
            if (best.Good == null) return false;

            from.Warehouse.Take(best.Good, best.Amount);
            var caravan = new Caravan(NextCaravanId++, player.Index, best.Good, best.Amount, from, best.To, best.Sea, best.Path, Price(from, best.Good));
            Caravans.Add(caravan);
            Dispatched?.Invoke(caravan);
            return true;
        }

        // ---------- Движение ----------

        /// <summary>Сколько «шагов» каравана за ход (в торговый сезон быстрее).</summary>
        private int Budget(bool sea) => (sea ? 8 : 6) + (_game.Season == Season.Trade ? 3 : 0);

        private void Advance(Caravan c, PlayerState player)
        {
            // Риск: разбойники вне своей земли и без дороги, шторм на море.
            var here = _game.Grid.GetTile(c.Coord);
            bool exposed = c.BySea || (!here.HasRoad && _game.OwnerOfTile(c.Coord) != c.OwnerIndex);
            float risk = (c.BySea ? (player.Has("maritime") ? 0f : SeaRiskPerTurn) : BanditRiskPerTurn) * (player.Has("bills") ? 0.5f : 1f);
            if (exposed && _game.Roll(DetRandom.Kind.CaravanLost, c.Id, player.Index) < risk)
            {
                Finish(c, false, c.BySea ? "потерян в шторм" : "разграблен разбойниками");
                return;
            }

            int budget = Budget(c.BySea) + (player.Has("post") ? 2 : 0);
            bool moved = false;
            while (!c.AtEnd)
            {
                var next = c.Path[c.Position + 1];
                // Враг на дороге или рядом с ней — караван стоит.
                if (!c.BySea && (IsHostile(next, c.OwnerIndex) || EnemyAdjacent(next, c.OwnerIndex)))
                {
                    c.StalledTurns++;
                    if (c.StalledTurns >= 3) Finish(c, false, "не прошёл: дорогу перерезал враг, товар разграблен");
                    else if (moved) Moved?.Invoke(c);
                    return;
                }
                int step = c.BySea ? 1 : LandStep(_game.Grid.GetTile(next));
                if (step > budget && moved) break;
                budget -= step;
                c.Position++;
                moved = true;
                if (budget <= 0) break;
            }
            c.StalledTurns = 0;
            if (moved) Moved?.Invoke(c);
            if (c.AtEnd) Deliver(c, player);
        }

        private bool EnemyAdjacent(HexCoord c, int owner)
        {
            for (int d = 0; d < 6; d++)
            {
                var u = _game.EnemyAt(c.Neighbor(d), owner);
                if (u != null && u.MeleeStrength > 0) return true;
            }
            return false;
        }

        private void Deliver(Caravan c, PlayerState player)
        {
            float price;
            if (c.To != null)
            {
                if (c.To.OwnerIndex != c.OwnerIndex && !CanTradeWith(c.OwnerIndex, c.To.OwnerIndex))
                {
                    Finish(c, false, $"граница {c.To.Data.displayName} закрыта — товар пропал");
                    return;
                }
                price = Price(c.To, c.Good);
                c.To.Warehouse.Add(c.Good, c.Amount);
            }
            else
            {
                price = ExternalPrice(c.Good) * (player.Has("exchange") ? 1.15f : 1f);
                _exportPressure[c.Good] = (_exportPressure.TryGetValue(c.Good, out var p) ? p : 0f) + c.Amount;
                var key = (c.OwnerIndex, c.Good);
                _yearExport[key] = (_yearExport.TryGetValue(key, out var y) ? y : 0f) + c.Amount;
            }
            // Высокая пошлина рождает контрабанду: часть товара проходит мимо таможни.
            // Пошлина берётся с торговой наценки (цена продажи − цена покупки), а не со всего оборота.
            float margin = Math.Max(0f, price - c.BuyPrice) * c.Amount;
            float full = margin * player.Tariff * (1f - player.Debasement) * (_game.IsBankrupt(player) ? 0f : 1f);
            float smuggled = full * GameState.SmugglingShare(player.Tariff, c.To != null && _game.HasGuard(c.To));
            int tariff = (int)Math.Round(full - smuggled);
            if (c.To != null && c.To.OwnerIndex != c.OwnerIndex)
                CollectImportDuty(c.To.OwnerIndex, (int)Math.Round(margin * ImportDuty(c.To.OwnerIndex, c.OwnerIndex)));
            player.Gold += tariff;
            player.TariffIncomeLastTurn += tariff;
            player.TradeIncomeTotal += tariff;
            player.SmuggledLastTurn += (int)Math.Round(smuggled);
            player.CaravansDeliveredLastTurn++;
            Finish(c, true, $"доставил {c.Amount:0} × {c.Good.displayName} по {price:0.0} — пошлина +{tariff}" +
                            (smuggled >= 0.5f ? $" (контрабанда −{smuggled:0})" : ""));
        }

        private readonly SortedDictionary<(int, GoodData), float> _yearExport = new SortedDictionary<(int, GoodData), float>(ContentOrder.OwnerGood);

        /// <summary>
        /// Раз в год: если больше 60% заморского вывоза игрока — один товар (и его много),
        /// заморская цена на него обваливается вдвое. Монокультура в торговле опасна.
        /// </summary>
        private void CheckPriceCrash(PlayerState player)
        {
            var mine = _yearExport.Where(kv => kv.Key.Item1 == player.Index).ToList();
            float total = mine.Sum(kv => kv.Value * kv.Key.Item2.basePrice);
            foreach (var kv in mine) _yearExport.Remove(kv.Key);
            if (total <= 0f) return;
            var top = mine.OrderByDescending(kv => kv.Value * kv.Key.Item2.basePrice).First();
            if (top.Value * top.Key.Item2.basePrice / total < 0.6f || top.Value < 20f) return;
            _exportPressure[top.Key.Item2] = (_exportPressure.TryGetValue(top.Key.Item2, out var p) ? p : 0f) + 30f;
            _game.Report($"Обвал цены: {player.Region.displayName} завалил заморский рынок товаром «{top.Key.Item2.displayName}» — цена упала вдвое");
        }

        private void Finish(Caravan c, bool delivered, string what)
        {
            Caravans.Remove(c);
            Finished?.Invoke(c, delivered);
            _game.Report($"Караван {c.From.Data.displayName} → {c.DestinationName}: {what}");
        }

        /// <summary>Сторона сошла со сцены: её караваны пропадают.</summary>
        public void RemoveOwner(int owner)
        {
            foreach (var c in Caravans.Where(c => c.OwnerIndex == owner).ToList())
            {
                Caravans.Remove(c);
                Finished?.Invoke(c, false);
            }
        }

        /// <summary>Сколько караванов этого игрока в пути.</summary>
        public int CountOf(PlayerState player) => Caravans.Count(c => c.OwnerIndex == player.Index);
    }
}

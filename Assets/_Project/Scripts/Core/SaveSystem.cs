using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Map;
using Runeterra.Tech;
using Runeterra.Units;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Сохранение и загрузка партии в JSON (один слот). Сохраняется всё состояние:
    /// клетки карты, туман, игроки (казна, налоги, долги, развитие), города (склады, постройки, мастера),
    /// юниты, караваны и план ИИ. Контент (юниты, товары, постройки, узлы) ищется по id.
    /// </summary>
    public static class SaveSystem
    {
        public static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");
        public static bool HasSave => File.Exists(SavePath);

        /// <summary>Сохранение, которое нужно загрузить при старте сцены партии (null — новая игра).</summary>
        public static SaveData PendingLoad { get; set; }

        // ---------- Формат ----------

        [Serializable] public class SaveData
        {
            public int version = 2;
            public int mapRadius, seed;
            public int turn, currentIndex;
            public int winner = -1;
            public string gameOverText;
            public List<TileSave> tiles = new List<TileSave>();
            public List<PlayerSave> players = new List<PlayerSave>();
            public List<CitySave> cities = new List<CitySave>();
            public List<CaravanSave> caravans = new List<CaravanSave>();
            public List<GoodAmount> exportPressure = new List<GoodAmount>();
            public List<YearExportSave> yearExport = new List<YearExportSave>();
            public List<int> aiAttacking = new List<int>();
            public List<RelationSave> relations = new List<RelationSave>();
            public List<ClaimSave> claims = new List<ClaimSave>();
            public List<ProposalSave> proposals = new List<ProposalSave>();
            public List<RelationSave> agreements = new List<RelationSave>();
            public List<RelationSave> embargoes = new List<RelationSave>();
            public List<RelationSave> importAccum = new List<RelationSave>();
        }

        [Serializable] public class RelationSave
        {
            public int a, b, value, stance, until, since;
        }

        [Serializable] public class ClaimSave
        {
            public int owner, ready;
            public string city;
            public bool historical;
        }

        [Serializable] public class ProposalSave
        {
            public int from, kind, turn;
        }

        [Serializable] public class TileSave
        {
            public HexCoord c;
            public int feature;
            public bool road;
            public string cityId;
        }

        [Serializable] public class GoodAmount
        {
            public string id;
            public float amount;
        }

        [Serializable] public class YearExportSave
        {
            public int owner;
            public string good;
            public float amount;
        }

        [Serializable] public class IntEntry
        {
            public string id;
            public int value;
        }

        [Serializable] public class MastersSave
        {
            public string building;
            public List<int> experience = new List<int>();
        }

        [Serializable] public class UnitSave
        {
            public int id;
            public string data;
            public HexCoord coord;
            public int moves, health, charges, experience, bonusStrength, attackBonus;
            public bool acted;
        }

        [Serializable] public class PlayerSave
        {
            public string region;
            public int gold, tariffIncome, importDuty, reserve, landIncome, peopleIncome, luxuryIncome, smuggled;
            public float tariff, landTax, peopleTax, luxuryTax, debasement;
            public int debt, missedPayments, interestLastTurn, bankruptUntil, stigmaUntil;
            public List<string> techs = new List<string>();
            public string researching;
            public List<GoodAmount> techProgress = new List<GoodAmount>();
            public List<string> revealed = new List<string>();
            public List<string> epochs = new List<string>();
            public int caravansDelivered, battlesThisTurn, battlesLastTurn, scienceLastTurn, armyUpkeepLastTurn, soilExhaustionYears;
            public List<IntEntry> branchWithoutCarrier = new List<IntEntry>();
            public List<HexCoord> explored = new List<HexCoord>();
            public List<UnitSave> units = new List<UnitSave>();
        }

        [Serializable] public class CitySave
        {
            public string dataId;
            // Для основанных в партии городов CityData создаётся заново.
            public bool founded;
            public string dataName, displayName;
            public int startingPopulation, food, production;

            public HexCoord coord;
            public int owner, founder;
            public List<HexCoord> territory = new List<HexCoord>();
            public bool hasMarket, hasPort;
            public HexCoord market, port;
            public int walls;
            public bool underSiege;
            public int population, foodStock, productionStock;
            public int buildKind; // 0 — ничего, 1 — юнит, 2 — район, 3 — постройка
            public string buildId;
            public int capacity;
            public float spoilage;
            public List<GoodAmount> stock = new List<GoodAmount>();
            public List<IntEntry> buildings = new List<IntEntry>();
            public List<MastersSave> masters = new List<MastersSave>();
            public float lastRawValue, lastLuxuryValue;
            public bool badHarvest;
            public int tier, tierFailTurns, draftCooldownUntil;
            public List<IntEntry> blights = new List<IntEntry>();
        }

        [Serializable] public class CaravanSave
        {
            public int owner, from, to; // индексы городов; to = -1 — заморский рынок
            public string good;
            public float amount, buyPrice;
            public bool bySea;
            public List<HexCoord> path = new List<HexCoord>();
            public int position, stalled;
        }

        /// <summary>Контент игры для поиска по id при загрузке.</summary>
        public class Content
        {
            public IEnumerable<UnitData> Units;
            public IEnumerable<GoodData> Goods;
            public IEnumerable<BuildingData> Buildings;
            public IEnumerable<TechData> Techs;
            public IEnumerable<DistrictData> Districts;
            public IEnumerable<CityData> Cities;
        }

        // ---------- Сохранение ----------

        private static List<HexCoord> Sorted(IEnumerable<HexCoord> coords) => coords.OrderBy(c => c.q).ThenBy(c => c.r).ToList();

        public static SaveData Capture(GameState s, AiPlayer ai)
        {
            var d = new SaveData
            {
                mapRadius = s.Grid.Radius,
                seed = s.Grid.Seed,
                turn = s.Turns.Turn,
                currentIndex = s.Turns.CurrentIndex,
                winner = s.Winner ?? -1,
                gameOverText = s.GameOverText,
            };
            foreach (var t in s.Grid.Tiles.OrderBy(t => t.Coord.q).ThenBy(t => t.Coord.r))
                d.tiles.Add(new TileSave { c = t.Coord, feature = (int)t.Feature, road = t.HasRoad, cityId = t.CityId });

            foreach (var p in s.Players)
            {
                var ps = new PlayerSave
                {
                    region = p.Region.id, gold = p.Gold, tariff = p.Tariff, tariffIncome = p.TariffIncomeLastTurn, importDuty = p.ImportDutyLastTurn,
                    landTax = p.LandTax, peopleTax = p.PeopleTax, luxuryTax = p.LuxuryTax, reserve = p.Reserve,
                    landIncome = p.LandIncome, peopleIncome = p.PeopleIncome, luxuryIncome = p.LuxuryIncome, smuggled = p.SmuggledLastTurn,
                    debasement = p.Debasement, debt = p.Debt, missedPayments = p.MissedPayments, interestLastTurn = p.InterestLastTurn,
                    bankruptUntil = p.BankruptUntil, stigmaUntil = p.StigmaUntil,
                    techs = p.Techs.OrderBy(x => x).ToList(),
                    researching = p.Researching != null ? p.Researching.id : null,
                    techProgress = p.TechProgress.OrderBy(kv => kv.Key).Select(kv => new GoodAmount { id = kv.Key, amount = kv.Value }).ToList(),
                    revealed = p.RevealedTechs.OrderBy(x => x).ToList(),
                    epochs = p.Epochs.OrderBy(x => x).ToList(),
                    caravansDelivered = p.CaravansDeliveredLastTurn, battlesThisTurn = p.BattlesThisTurn, battlesLastTurn = p.BattlesLastTurn,
                    scienceLastTurn = p.ScienceLastTurn, armyUpkeepLastTurn = p.ArmyUpkeepLastTurn, soilExhaustionYears = p.SoilExhaustionYears,
                    branchWithoutCarrier = p.BranchWithoutCarrier.OrderBy(kv => (int)kv.Key)
                        .Select(kv => new IntEntry { id = ((int)kv.Key).ToString(), value = kv.Value }).ToList(),
                    explored = Sorted(s.Vision.Explored(p.Index)),
                };
                foreach (var u in p.Units.Where(u => u.IsAlive))
                    ps.units.Add(new UnitSave
                    {
                        id = u.Id, data = u.Data.id, coord = u.Coord, moves = u.MovesLeft, health = u.Health, charges = u.BuildCharges,
                        experience = u.Experience, bonusStrength = u.BonusStrength, attackBonus = u.AttackBonus, acted = u.Acted,
                    });
                d.players.Add(ps);
            }

            foreach (var c in s.Cities)
            {
                bool founded = c.Data.name.StartsWith("Founded");
                var b = c.CurrentBuild;
                d.cities.Add(new CitySave
                {
                    dataId = c.Data.id, founded = founded, dataName = c.Data.name, displayName = c.Data.displayName,
                    startingPopulation = c.Data.startingPopulation, food = c.Data.food, production = c.Data.production,
                    coord = c.Coord, owner = c.OwnerIndex, founder = c.FounderIndex, territory = Sorted(c.Territory),
                    hasMarket = c.HasMarket, market = c.MarketCoord ?? default, hasPort = c.HasPort, port = c.PortCoord ?? default,
                    walls = c.Walls, underSiege = c.UnderSiege,
                    population = c.Population, foodStock = c.FoodStock, productionStock = c.ProductionStock,
                    buildKind = b == null ? 0 : b.Unit != null ? 1 : b.District != null ? 2 : 3,
                    buildId = b == null ? null : b.Unit != null ? b.Unit.id : b.District != null ? b.District.id : b.Building.id,
                    capacity = c.Warehouse.Capacity, spoilage = c.Warehouse.SpoilageMultiplier,
                    stock = c.Warehouse.Items.OrderBy(kv => kv.Key.id).Select(kv => new GoodAmount { id = kv.Key.id, amount = kv.Value }).ToList(),
                    buildings = c.Buildings.OrderBy(kv => kv.Key.id).Select(kv => new IntEntry { id = kv.Key.id, value = kv.Value }).ToList(),
                    masters = c.Masters.Where(kv => kv.Value.Count > 0).OrderBy(kv => kv.Key.id)
                        .Select(kv => new MastersSave { building = kv.Key.id, experience = kv.Value.ToList() }).ToList(),
                    lastRawValue = c.LastRawValue, lastLuxuryValue = c.LastLuxuryValue, badHarvest = c.BadHarvest,
                    tier = (int)c.Tier, tierFailTurns = c.TierFailTurns, draftCooldownUntil = c.DraftCooldownUntil,
                    blights = c.Blights.OrderBy(kv => kv.Key.id).Select(kv => new IntEntry { id = kv.Key.id, value = kv.Value }).ToList(),
                });
            }

            foreach (var cv in s.Trade.Caravans)
                d.caravans.Add(new CaravanSave
                {
                    owner = cv.OwnerIndex, from = s.Cities.IndexOf(cv.From), to = cv.To != null ? s.Cities.IndexOf(cv.To) : -1,
                    good = cv.Good.id, amount = cv.Amount, buyPrice = cv.BuyPrice, bySea = cv.BySea,
                    path = cv.Path.ToList(), position = cv.Position, stalled = cv.StalledTurns,
                });
            d.exportPressure = s.Trade.ExportPressure.OrderBy(kv => kv.Key.id)
                .Select(kv => new GoodAmount { id = kv.Key.id, amount = kv.Value }).ToList();
            d.yearExport = s.Trade.YearExport.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2.id)
                .Select(kv => new YearExportSave { owner = kv.Key.Item1, good = kv.Key.Item2.id, amount = kv.Value }).ToList();
            d.aiAttacking = ai.Attacking.OrderBy(x => x).ToList();
            d.relations = s.Diplomacy.All.OrderBy(x => x.key.Item1).ThenBy(x => x.key.Item2)
                .Select(x => new RelationSave { a = x.key.Item1, b = x.key.Item2, value = x.rel.Value, stance = (int)x.rel.Stance, until = x.rel.Until, since = x.rel.Since })
                .ToList();
            d.claims = s.Diplomacy.Claims.Select(c => new ClaimSave { owner = c.Owner, city = c.CityId, ready = c.ReadyTurn, historical = c.Historical }).ToList();
            d.agreements = s.Trade.Agreements.OrderBy(x => x).Select(x => new RelationSave { a = x.Item1, b = x.Item2 }).ToList();
            d.embargoes = s.Trade.EmbargoSet.OrderBy(x => x).Select(x => new RelationSave { a = x.Item1, b = x.Item2 }).ToList();
            d.importAccum = s.Trade.ImportAccum.OrderBy(x => x.Key).Select(x => new RelationSave { a = x.Key, value = x.Value }).ToList();
            d.proposals = s.Diplomacy.Proposals.Select(p => new ProposalSave { from = p.From, kind = (int)p.Kind, turn = p.Turn }).ToList();
            return d;
        }

        public static string ToJson(SaveData d) => JsonUtility.ToJson(d);

        public static void Save(GameState s, AiPlayer ai)
        {
            var json = ToJson(Capture(s, ai));
            var tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }

        /// <summary>Прочитать сохранение с диска (null — нет файла или он повреждён).</summary>
        public static SaveData Read()
        {
            if (!HasSave) return null;
            try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)); }
            catch (Exception e)
            {
                Debug.LogWarning($"Не удалось прочитать сохранение: {e.Message}");
                return null;
            }
        }

        // ---------- Загрузка ----------

        private static T Find<T>(IEnumerable<T> items, string id, Func<T, string> key) where T : class =>
            string.IsNullOrEmpty(id) ? null : items.FirstOrDefault(x => x != null && key(x) == id);

        /// <summary>
        /// Восстановить партию в свежесозданный GameState (игроки уже созданы по регионам карты,
        /// городов и юнитов ещё нет). Юниты появляются через событие UnitCreated.
        /// </summary>
        public static void Apply(SaveData d, GameState s, AiPlayer ai, Content content)
        {
            foreach (var t in d.tiles)
            {
                var tile = s.Grid.GetTile(t.c);
                if (tile == null) continue;
                tile.Feature = (TileFeature)t.feature;
                tile.HasRoad = t.road;
                tile.CityId = string.IsNullOrEmpty(t.cityId) ? null : t.cityId;
            }

            GoodData Good(string id) => Find(content.Goods, id, g => g.id);
            BuildingData Building(string id) => Find(content.Buildings, id, b => b.id);

            // Города — до юнитов, чтобы правила (владение клетками) работали сразу.
            foreach (var cs in d.cities)
            {
                CityData data;
                if (cs.founded)
                {
                    data = ScriptableObject.CreateInstance<CityData>();
                    data.name = cs.dataName;
                    data.id = cs.dataId;
                    data.displayName = cs.displayName;
                    data.startingPopulation = cs.startingPopulation;
                    data.food = cs.food;
                    data.production = cs.production;
                }
                else data = Find(content.Cities, cs.dataId, c => c.id);
                if (data == null) throw new InvalidDataException($"город {cs.dataId} не найден");

                var city = new City(data, cs.coord, cs.owner) { FounderIndex = cs.founder };
                city.Territory.UnionWith(cs.territory);
                city.MarketCoord = cs.hasMarket ? cs.market : (HexCoord?)null;
                city.PortCoord = cs.hasPort ? cs.port : (HexCoord?)null;
                city.RestoreWalls(cs.walls, cs.underSiege);
                city.Population = cs.population;
                city.FoodStock = cs.foodStock;
                city.ProductionStock = cs.productionStock;
                city.CurrentBuild = cs.buildKind switch
                {
                    1 when Find(content.Units, cs.buildId, u => u.id) is UnitData u => new BuildItem(u),
                    2 when Find(content.Districts, cs.buildId, x => x.id) is DistrictData x => new BuildItem(x),
                    3 when Building(cs.buildId) is BuildingData b => new BuildItem(b),
                    _ => null,
                };
                foreach (var e in cs.buildings)
                    if (Building(e.id) is BuildingData b) city.Buildings[b] = e.value;
                city.Warehouse.Capacity = cs.capacity;
                city.Warehouse.SpoilageMultiplier = cs.spoilage;
                foreach (var g in cs.stock)
                    if (Good(g.id) is GoodData good) city.Warehouse.Set(good, g.amount);
                foreach (var m in cs.masters)
                    if (Building(m.building) is BuildingData b) city.MastersOf(b).AddRange(m.experience);
                city.LastRawValue = cs.lastRawValue;
                city.LastLuxuryValue = cs.lastLuxuryValue;
                city.BadHarvest = cs.badHarvest;
                city.Tier = (SettlementTier)cs.tier;
                city.TierFailTurns = cs.tierFailTurns;
                city.DraftCooldownUntil = cs.draftCooldownUntil;
                foreach (var e in cs.blights)
                    if (Good(e.id) is GoodData good) city.Blights[good] = e.value;
                s.Cities.Add(city);
            }

            for (int i = 0; i < d.players.Count && i < s.Players.Count; i++)
            {
                var ps = d.players[i];
                var p = s.Players[i];
                if (p.Region.id != ps.region) throw new InvalidDataException($"регион {i}: ожидался {ps.region}, на карте {p.Region.id}");
                p.Gold = ps.gold; p.Tariff = ps.tariff; p.TariffIncomeLastTurn = ps.tariffIncome; p.ImportDutyLastTurn = ps.importDuty;
                p.LandTax = ps.landTax; p.PeopleTax = ps.peopleTax; p.LuxuryTax = ps.luxuryTax; p.Reserve = ps.reserve;
                p.LandIncome = ps.landIncome; p.PeopleIncome = ps.peopleIncome; p.LuxuryIncome = ps.luxuryIncome; p.SmuggledLastTurn = ps.smuggled;
                p.Debasement = ps.debasement; p.Debt = ps.debt; p.MissedPayments = ps.missedPayments; p.InterestLastTurn = ps.interestLastTurn;
                p.BankruptUntil = ps.bankruptUntil; p.StigmaUntil = ps.stigmaUntil;
                p.Techs.UnionWith(ps.techs);
                p.Researching = Find(content.Techs, ps.researching, t => t.id);
                foreach (var e in ps.techProgress) p.TechProgress[e.id] = e.amount;
                p.RevealedTechs.UnionWith(ps.revealed);
                p.Epochs.UnionWith(ps.epochs);
                p.CaravansDeliveredLastTurn = ps.caravansDelivered; p.BattlesThisTurn = ps.battlesThisTurn; p.BattlesLastTurn = ps.battlesLastTurn;
                p.ScienceLastTurn = ps.scienceLastTurn; p.ArmyUpkeepLastTurn = ps.armyUpkeepLastTurn; p.SoilExhaustionYears = ps.soilExhaustionYears;
                foreach (var e in ps.branchWithoutCarrier) p.BranchWithoutCarrier[(TechBranch)int.Parse(e.id)] = e.value;
                s.Vision.Explored(p.Index).UnionWith(ps.explored);

                foreach (var us in ps.units)
                {
                    var data = Find(content.Units, us.data, u => u.id);
                    if (data == null) throw new InvalidDataException($"юнит {us.data} не найден");
                    var unit = new Unit(data, p.Index, us.coord) { BonusStrength = us.bonusStrength, AttackBonus = us.attackBonus };
                    unit.Restore(us.id, us.coord, us.moves, us.health, us.charges, us.experience, us.acted);
                    s.AttachUnit(p, unit);
                }
            }

            foreach (var cv in d.caravans)
            {
                var good = Good(cv.good);
                if (good == null || cv.from < 0 || cv.from >= s.Cities.Count) continue;
                var to = cv.to >= 0 && cv.to < s.Cities.Count ? s.Cities[cv.to] : null;
                s.Trade.Caravans.Add(new Caravan(cv.owner, good, cv.amount, s.Cities[cv.from], to, cv.bySea, cv.path, cv.buyPrice)
                    { Position = cv.position, StalledTurns = cv.stalled });
            }
            foreach (var e in d.exportPressure)
                if (Good(e.id) is GoodData g) s.Trade.ExportPressure[g] = e.amount;
            foreach (var e in d.yearExport)
                if (Good(e.good) is GoodData g) s.Trade.YearExport[(e.owner, g)] = e.amount;
            ai.Attacking.UnionWith(d.aiAttacking);
            foreach (var r in d.relations)
                s.Diplomacy.Set(r.a, r.b, new Relation { Value = r.value, Stance = (Stance)r.stance, Until = r.until, Since = r.since });
            foreach (var c in d.claims)
                s.Diplomacy.Claims.Add(new Claim { Owner = c.owner, CityId = c.city, ReadyTurn = c.ready, Historical = c.historical });
            foreach (var x in d.agreements) s.Trade.Agreements.Add((x.a, x.b));
            foreach (var x in d.embargoes) s.Trade.EmbargoSet.Add((x.a, x.b));
            foreach (var x in d.importAccum) s.Trade.ImportAccum[x.a] = x.value;
            foreach (var p in d.proposals)
                s.Diplomacy.Proposals.Add(new Proposal { From = p.from, Kind = (ProposalKind)p.kind, Turn = p.turn });

            s.Turns.Restore(d.turn, d.currentIndex);
            s.Restore(d.winner >= 0 ? d.winner : (int?)null, d.gameOverText, d.turn);
            s.Vision.RefreshAll();
        }
    }
}

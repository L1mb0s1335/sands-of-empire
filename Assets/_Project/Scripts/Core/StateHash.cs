using System;
using System.Linq;
using Runeterra.Economy;
using Runeterra.Map;

namespace Runeterra.Core
{
    /// <summary>
    /// Хеш состояния партии по разделам: карта, юниты, города, экономика, развитие, дипломатия, торговля.
    /// Одинаковое состояние даёт одинаковый хеш на любой машине; по разделам видно, где разошлись.
    /// Числа с плавающей точкой входят побитово.
    /// </summary>
    public sealed class StateHash
    {
        public ulong Map, Units, Cities, Economy, Techs, Diplomacy, Trade, Total;

        public static readonly string[] SectionNames = { "карта", "юниты", "города", "экономика", "развитие", "дипломатия", "торговля" };

        public ulong[] Sections => new[] { Map, Units, Cities, Economy, Techs, Diplomacy, Trade };

        public override string ToString() =>
            $"{Total:x16} карта={Map:x8} юниты={Units:x8} города={Cities:x8} экономика={Economy:x8} развитие={Techs:x8} дипломатия={Diplomacy:x8} торговля={Trade:x8}";

        public bool Equals(StateHash other) => other != null && Total == other.Total;

        /// <summary>Разделы, в которых хеши различаются (через запятую).</summary>
        public string DiffSections(StateHash other)
        {
            var a = Sections;
            var b = other.Sections;
            return string.Join(", ", Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).Select(i => SectionNames[i]));
        }

        private sealed class H
        {
            public ulong V = 0x5EED5EED5EED5EEDUL;
            public void I(long v) => V = DetRandom.Combine(V, v);
            public void B(bool v) => I(v ? 1 : 0);
            public void F(float v) => I(BitConverter.SingleToInt32Bits(v));
            // null и пустая строка равны: JSON сохранения превращает null в "".
            public void S(string v) => I(string.IsNullOrEmpty(v) ? -1 : DetRandom.StringKey(v));
            public void C(HexCoord c) { I(c.q); I(c.r); }
            public void C(HexCoord? c) { B(c.HasValue); if (c.HasValue) C(c.Value); }
        }

        public static StateHash Of(GameState s, AiPlayer ai)
        {
            var r = new StateHash();

            var map = new H();
            foreach (var t in s.Grid.Tiles.OrderBy(t => t.Coord, ContentOrder.Coords))
            {
                map.C(t.Coord);
                map.I((int)t.Terrain);
                map.I((int)t.Feature);
                map.B(t.HasRoad);
                map.S(t.CityId);
                map.S(t.Resource);
            }
            foreach (var p in s.Players)
            {
                map.I(p.Index);
                foreach (var c in s.Vision.Explored(p.Index).OrderBy(c => c, ContentOrder.Coords)) map.C(c);
                map.I(-2);
                foreach (var c in s.Vision.Visible(p.Index).OrderBy(c => c, ContentOrder.Coords)) map.C(c);
                map.I(-3);
            }
            r.Map = map.V;

            var units = new H();
            units.I(s.NextUnitId);
            foreach (var p in s.Players)
            {
                units.I(p.Index);
                units.I(p.Units.Count);
                foreach (var u in p.Units)
                {
                    units.I(u.Id); units.S(u.Data.id); units.C(u.Coord); units.I(u.MovesLeft); units.I(u.Health);
                    units.I(u.BuildCharges); units.I(u.Experience); units.I(u.BonusStrength); units.I(u.AttackBonus);
                    units.I(u.MovementBonus); units.B(u.Acted); units.B(u.Consumed);
                }
            }
            r.Units = units.V;

            var cities = new H();
            foreach (var c in s.Cities)
            {
                cities.S(c.Data.id); cities.S(c.Data.displayName); cities.C(c.Coord); cities.I(c.OwnerIndex); cities.I(c.FounderIndex);
                foreach (var t in c.Territory.OrderBy(t => t, ContentOrder.Coords)) cities.C(t);
                cities.I(-2);
                cities.C(c.MarketCoord); cities.C(c.PortCoord);
                cities.I(c.Walls); cities.B(c.UnderSiege);
                cities.I(c.Population); cities.I(c.FoodStock); cities.I(c.ProductionStock);
                var b = c.CurrentBuild;
                cities.S(b == null ? null : b.Unit != null ? "u:" + b.Unit.id : b.District != null ? "d:" + b.District.id : "b:" + b.Building.id);
                cities.I(c.Warehouse.Capacity); cities.F(c.Warehouse.SpoilageMultiplier);
                foreach (var kv in c.Warehouse.Exact) { cities.S(kv.Key.id); cities.F(kv.Value); }
                cities.I(-3);
                foreach (var kv in c.Buildings) { cities.S(kv.Key.id); cities.I(kv.Value); }
                cities.I(-4);
                foreach (var kv in c.Masters.Where(kv => kv.Value.Count > 0))
                {
                    cities.S(kv.Key.id);
                    foreach (var e in kv.Value) cities.I(e);
                    cities.I(-5);
                }
                cities.F(c.LastRawValue); cities.F(c.LastLuxuryValue); cities.B(c.BadHarvest);
                cities.I((int)c.Tier); cities.I(c.TierFailTurns); cities.I(c.DraftCooldownUntil);
                foreach (var kv in c.Blights) { cities.S(kv.Key.id); cities.I(kv.Value); }
                cities.I(-6);
            }
            r.Cities = cities.V;

            var eco = new H();
            foreach (var p in s.Players)
            {
                eco.I(p.Index); eco.I(p.Gold); eco.F(p.Tariff); eco.I(p.TariffIncomeLastTurn); eco.I(p.ImportDutyLastTurn); eco.I(p.TradeIncomeTotal);
                eco.F(p.LandTax); eco.F(p.PeopleTax); eco.F(p.LuxuryTax); eco.I(p.Reserve);
                eco.I(p.LandIncome); eco.I(p.PeopleIncome); eco.I(p.LuxuryIncome); eco.I(p.SmuggledLastTurn);
                eco.F(p.Debasement); eco.I(p.Debt); eco.I(p.MissedPayments); eco.I(p.InterestLastTurn); eco.I(p.BankruptUntil); eco.I(p.StigmaUntil);
                eco.I(p.CaravansDeliveredLastTurn); eco.I(p.BattlesThisTurn); eco.I(p.BattlesLastTurn);
                eco.I(p.ArmyUpkeepLastTurn); eco.I(p.SoilExhaustionYears);
            }
            r.Economy = eco.V;

            var tech = new H();
            foreach (var p in s.Players)
            {
                tech.I(p.Index);
                foreach (var t in p.Techs) tech.S(t);
                tech.I(-2);
                tech.S(p.Researching?.id);
                foreach (var kv in p.TechProgress) { tech.S(kv.Key); tech.F(kv.Value); }
                tech.I(-3);
                foreach (var t in p.RevealedTechs) tech.S(t);
                tech.I(-4);
                foreach (var t in p.Epochs) tech.S(t);
                tech.I(-5);
                foreach (var kv in p.BranchWithoutCarrier) { tech.I((int)kv.Key); tech.I(kv.Value); }
                tech.I(p.ScienceLastTurn);
            }
            r.Techs = tech.V;

            var dip = new H();
            var d = s.Diplomacy;
            dip.B(d.WarsDisabled);
            foreach (var (key, rel) in d.All) { dip.I(key.Item1); dip.I(key.Item2); dip.I(rel.Value); dip.I((int)rel.Stance); dip.I(rel.Until); dip.I(rel.Since); }
            dip.I(-2);
            foreach (var c in d.Claims) { dip.I(c.Owner); dip.S(c.CityId); dip.I(c.ReadyTurn); dip.B(c.Historical); }
            dip.I(-3);
            foreach (var p in d.Proposals) { dip.I(p.From); dip.I((int)p.Kind); dip.I(p.Turn); }
            dip.I(-4);
            dip.I(d.CoalitionTarget); dip.I(d.CoalitionCooldown);
            foreach (var m in d.Coalition) dip.I(m);
            dip.I(-5);
            if (ai != null) foreach (var a in ai.Attacking) dip.I(a);
            r.Diplomacy = dip.V;

            var trade = new H();
            var tr = s.Trade;
            trade.I(tr.NextCaravanId);
            foreach (var c in tr.Caravans)
            {
                trade.I(c.Id); trade.I(c.OwnerIndex); trade.S(c.Good.id); trade.F(c.Amount);
                trade.I(s.Cities.IndexOf(c.From)); trade.I(c.To != null ? s.Cities.IndexOf(c.To) : -1);
                trade.B(c.BySea); trade.I(c.Path.Count);
                foreach (var p in c.Path) trade.C(p);
                trade.I(c.Position); trade.I(c.StalledTurns); trade.F(c.BuyPrice);
            }
            trade.I(-2);
            foreach (var kv in tr.ExportPressure) { trade.S(kv.Key.id); trade.F(kv.Value); }
            trade.I(-3);
            foreach (var kv in tr.YearExport) { trade.I(kv.Key.Item1); trade.S(kv.Key.Item2.id); trade.F(kv.Value); }
            trade.I(-4);
            foreach (var (a, b) in tr.Agreements) { trade.I(a); trade.I(b); }
            trade.I(-5);
            foreach (var (a, b) in tr.EmbargoSet) { trade.I(a); trade.I(b); }
            trade.I(-6);
            foreach (var kv in tr.ImportAccum) { trade.I(kv.Key); trade.I(kv.Value); }
            r.Trade = trade.V;

            var total = new H();
            total.I(s.GameSeed); total.I(s.Turns.Turn); total.I(s.Turns.CurrentIndex);
            total.I(s.Winner ?? -1); total.S(s.GameOverText);
            total.I(s.Scenario == Scenario.PeacefulDevelopment ? 1 : 0); total.I(s.TurnLimit);
            foreach (var v in r.Sections) total.I((long)v);
            r.Total = total.V;
            return r;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Economy;
using Runeterra.Tech;

namespace Runeterra.Core
{
    /// <summary>
    /// Дерево развития. Знания копятся в городах; цена узла = база × (1 + 10% за каждый изученный узел).
    /// Эффекты узлов учитываются правилами игры через PlayerState.Has(id).
    /// </summary>
    public class TechSystem
    {
        public const float BreadthCostStep = 0.1f;
        public const float ArmyUpkeepBase = 0.5f;
        public const float ArmyUpkeepPerWarTech = 0.25f;

        private readonly GameState _game;
        public IReadOnlyList<TechData> All { get; }

        public event Action<PlayerState, TechData> Researched;

        public TechSystem(GameState game, IReadOnlyList<TechData> techs)
        {
            _game = game;
            All = techs ?? new List<TechData>();
        }

        /// <summary>Знания за ход: в каждом городе 1 + население/3 (+1 со Школами, +2 с Астролябией в порту); Библиотеки ×1.25.</summary>
        public int Science(PlayerState p)
        {
            float s = _game.Cities.Where(c => c.OwnerIndex == p.Index)
                .Sum(c => 1f + c.Population / 3f + (p.Has("schools") ? 1f : 0f) + (p.Has("astrolabe") && c.HasPort ? 2f : 0f));
            if (p.Has("libraries")) s *= 1.25f;
            if (p.Epochs.Contains(EpochKnowledge)) s *= 1.2f;
            if (p.Region.leaderAbility == LeaderAbility.HouseOfWisdom) s *= 1.25f;
            return (int)Math.Round(s);
        }

        /// <summary>Узел уже знает сосед — перенимаем (торговля, пленные, шпионы): −25%.</summary>
        public bool NeighborKnows(PlayerState p, TechData t) => _game.Players.Any(o => o != p && o.Has(t.id));

        public int Cost(PlayerState p, TechData t)
        {
            float cost = t.baseCost * (1f + BreadthCostStep * p.Techs.Count);
            if (p.Has("translation")) cost *= 0.9f;
            if (NeighborKnows(p, t)) cost *= 0.75f;
            return (int)Math.Round(cost);
        }

        public float Progress(PlayerState p, TechData t) => p.TechProgress.TryGetValue(t.id, out var v) ? v : 0f;

        public bool IsKnown(PlayerState p, TechData t) => p.Has(t.id);

        public bool IsVisible(PlayerState p, TechData t) => !t.hidden || p.RevealedTechs.Contains(t.id) || IsKnown(p, t);

        public bool IsLockedByDoctrine(PlayerState p, TechData t) => t.exclusiveWith != null && p.Has(t.exclusiveWith.id);

        public bool IsAvailable(PlayerState p, TechData t) =>
            !IsKnown(p, t) && IsVisible(p, t) && !IsLockedByDoctrine(p, t) &&
            t.prerequisites.All(pre => pre == null || p.Has(pre.id));

        public int TurnsLeft(PlayerState p, TechData t)
        {
            float left = Cost(p, t) - Progress(p, t);
            return Math.Max(1, (int)Math.Ceiling(left / Math.Max(1, Science(p))));
        }

        public void Choose(PlayerState p, TechData t)
        {
            if (t == null || !IsAvailable(p, t) || p.Researching == t) return;
            p.Researching = t; // прогресс по каждому узлу копится отдельно и не пропадает при смене темы
        }

        /// <summary>Узлы войны: каждый поднимает содержание армии на 25%.</summary>
        public int WarTechs(PlayerState p) => All.Count(t => t.branch == TechBranch.War && p.Has(t.id));

        /// <summary>Содержание армии: 0.5 золота за боевой юнит × (1 + 0.25 × узлы войны).</summary>
        public int ArmyUpkeep(PlayerState p)
        {
            int army = p.Units.Count(u => u.IsAlive && GameState.CanFight(u));
            float k = 1f + ArmyUpkeepPerWarTech * WarTechs(p);
            if (p.Has("iqta")) k *= 0.5f;
            if (p.Has("salary")) k += 0.25f;
            return (int)Math.Ceiling(army * ArmyUpkeepBase * k);
        }

        /// <summary>Начало хода: знания в текущее исследование, содержание армии, побочные эффекты узлов.</summary>
        public void PlayTurn(PlayerState p)
        {
            p.BattlesLastTurn = p.BattlesThisTurn;
            p.BattlesThisTurn = 0;
            RevealHidden(p);
            p.ScienceLastTurn = Science(p);
            if (p.Researching != null) p.ResearchProgress += p.ScienceLastTurn;
            AddPractice(p);
            foreach (var t in All.Where(t => IsAvailable(p, t) && Progress(p, t) >= Cost(p, t)).ToList()) Complete(p, t);
            CheckCarriers(p);
            CheckEpochs(p);

            p.Gold -= _game.BuildingUpkeep(p);
            int upkeep = ArmyUpkeep(p);
            p.Gold -= upkeep;
            p.ArmyUpkeepLastTurn = upkeep;
            if (p.Gold < 0 && p.Has("salary")) p.Gold = 0; // войско на жалованье не разбегается
            if (p.Gold < 0)
            {
                // Нечем платить армии — дезертирство: уходит самый слабый боевой юнит.
                var deserter = p.Units.Where(u => u.IsAlive && GameState.CanFight(u)).OrderBy(u => u.Health).FirstOrDefault();
                p.Gold = 0;
                if (deserter != null)
                {
                    deserter.TakeDamage(deserter.Health);
                    _game.Turns.RemoveDead();
                    _game.Report($"{p.Region.displayName}: армии нечем платить — {deserter.Data.displayName} дезертирует");
                }
            }

            if (p.Has("intensive") && _game.Season == Season.Sowing) p.SoilExhaustionYears++;

            // Тяжёлая конница ест зерно со склада столицы.
            if (p.Has("heavycav") && _game.Grain != null)
            {
                var capital = _game.CapitalOf(p);
                int riders = p.Units.Count(u => u.IsAlive && u.Data.role == Runeterra.Units.UnitRole.Melee);
                if (capital != null && riders > 0)
                {
                    float need = riders * 0.5f;
                    float got = capital.Warehouse.Take(_game.Grain, need);
                    if (got + 0.01f < need)
                        foreach (var u in p.Units.Where(u => u.IsAlive && u.Data.role == Runeterra.Units.UnitRole.Melee).ToList())
                            u.TakeDamage(10);
                }
            }
        }

        private void Complete(PlayerState p, TechData t)
        {
            p.Techs.Add(t.id);
            p.TechProgress.Remove(t.id);
            if (p.Researching == t) p.Researching = null;
            ApplyOnResearch(p, t, +1);
            Researched?.Invoke(p, t);
            _game.Report($"{p.Region.displayName} изучает «{t.displayName}»: {t.effect}" + (NeighborKnows(p, t) ? " (перенято у соседа)" : ""));
        }

        /// <summary>Разовые эффекты при изучении (sign = +1) или утрате (sign = −1) узла.</summary>
        private void ApplyOnResearch(PlayerState p, TechData t, int sign)
        {
            foreach (var u in p.Units) u.BonusStrength += sign * UnitBonus(t.id, u);
            if (t.id == "warehouses")
                foreach (var c in _game.Cities.Where(c => c.OwnerIndex == p.Index)) c.Warehouse.Capacity += sign * 15;
        }

        // ---------- Живое дерево ----------

        /// <summary>
        /// Практика ведёт науку сама: доступные узлы ветки получают очки от дела —
        /// поля (Земля), мастера (Ремесло), доставленные караваны (Торговля), города (Управление),
        /// жители (Знание), бои (Война).
        /// </summary>
        public float Practice(PlayerState p, TechBranch branch)
        {
            var cities = _game.Cities.Where(c => c.OwnerIndex == p.Index).ToList();
            return branch switch
            {
                TechBranch.LandAndWater => cities.Sum(c => c.Territory.Count(t => _game.Grid.GetTile(t) is var tile && tile != null &&
                    (tile.Terrain == Runeterra.Map.TerrainType.Grassland || tile.Terrain == Runeterra.Map.TerrainType.Plains))) / 6f,
                TechBranch.Craft => cities.Sum(c => c.Employed) / 3f,
                TechBranch.Trade => p.CaravansDeliveredLastTurn * 0.5f,
                TechBranch.Governance => cities.Count / 2f,
                TechBranch.Knowledge => cities.Sum(c => c.Population) / 10f,
                _ => p.BattlesLastTurn * 1f,
            };
        }

        private void AddPractice(PlayerState p)
        {
            foreach (var t in All.Where(t => IsAvailable(p, t)))
            {
                float gain = Practice(p, t.branch);
                if (gain > 0f) p.TechProgress[t.id] = Progress(p, t) + gain;
            }
        }

        /// <summary>Условия появления скрытых узлов.</summary>
        public bool RevealCondition(PlayerState p, TechData t)
        {
            var mine = _game.Cities.Where(c => c.OwnerIndex == p.Index).ToList();
            var weapons = _game.Goods.FirstOrDefault(g => g.id == "weapons");
            return t.id switch
            {
                "damascus" => weapons != null && mine.Any(c => c.Warehouse.Get(weapons) >= 10),
                "maritime" => mine.Count(c => c.HasPort) >= 2,
                "astrolabe" => p.Has("libraries") && mine.Any(c => c.HasPort),
                _ => false,
            };
        }

        private void RevealHidden(PlayerState p)
        {
            foreach (var t in All.Where(t => t.hidden && !p.RevealedTechs.Contains(t.id) && RevealCondition(p, t)))
            {
                p.RevealedTechs.Add(t.id);
                _game.Report($"{p.Region.displayName}: открыт скрытый узел «{t.displayName}» — {t.effect}");
            }
        }

        /// <summary>Носители знаний ветки: мастера, жители Деревень и крупнее, рынки, 2 города, армия, земледельцы.</summary>
        public bool HasCarrier(PlayerState p, TechBranch branch)
        {
            var mine = _game.Cities.Where(c => c.OwnerIndex == p.Index).ToList();
            return branch switch
            {
                TechBranch.LandAndWater => mine.Any(c => c.Population >= 2),
                TechBranch.Craft => mine.Any(c => c.Employed > 0),
                TechBranch.Trade => mine.Any(c => c.HasMarket),
                TechBranch.Governance => mine.Count >= 2,
                TechBranch.Knowledge => mine.Any(c => c.Tier >= Runeterra.Cities.SettlementTier.Village),
                _ => p.Units.Any(u => u.IsAlive && GameState.CanFight(u)),
            };
        }

        public string CarrierName(TechBranch branch) => branch switch
        {
            TechBranch.LandAndWater => "город с 2+ жителями",
            TechBranch.Craft => "мастера в мастерских",
            TechBranch.Trade => "рынок",
            TechBranch.Governance => "2 города",
            TechBranch.Knowledge => "учёные (Деревня и крупнее)",
            _ => "армия",
        };

        public const int CarrierGraceTurns = Seasons.TurnsPerYear;

        /// <summary>Ветка без носителей целый год теряет самый поздний узел (половина прогресса остаётся).</summary>
        private void CheckCarriers(PlayerState p)
        {
            foreach (TechBranch branch in Enum.GetValues(typeof(TechBranch)))
            {
                var known = All.Where(t => t.branch == branch && p.Has(t.id)).ToList();
                if (known.Count == 0 || HasCarrier(p, branch)) { p.BranchWithoutCarrier[branch] = 0; continue; }
                int turns = (p.BranchWithoutCarrier.TryGetValue(branch, out var n) ? n : 0) + 1;
                p.BranchWithoutCarrier[branch] = turns;
                if (turns < CarrierGraceTurns) continue;
                p.BranchWithoutCarrier[branch] = 0;
                // Теряем узел, от которого ничего не зависит, — самый поздний.
                var lost = known.Where(t => !known.Any(o => o.prerequisites.Contains(t))).OrderByDescending(t => t.order).First();
                p.Techs.Remove(lost.id);
                p.TechProgress[lost.id] = Cost(p, lost) * 0.5f;
                ApplyOnResearch(p, lost, -1);
                _game.Report($"{p.Region.displayName}: знание «{lost.displayName}» утеряно — нет носителей ({CarrierName(branch)})");
            }
        }

        // ---------- Эпохи ----------

        public const string EpochTrade = "trade";
        public const string EpochCraft = "craft";
        public const string EpochKnowledge = "knowledge";

        public static string EpochName(string id) => id switch
        {
            EpochTrade => "Эпоха торговли",
            EpochCraft => "Эпоха ремесла",
            _ => "Эпоха знаний",
        };

        public static string EpochEffect(string id) => id switch
        {
            EpochTrade => "золото городов +10%",
            EpochCraft => "выпуск мастерских +10%",
            _ => "знания +20%",
        };

        private int Count(PlayerState p, TechBranch b) => All.Count(t => t.branch == b && p.Has(t.id));

        /// <summary>Что нужно для эпохи (пустой список — эпоха наступила или наступит).</summary>
        public List<string> EpochMissing(PlayerState p, string epoch)
        {
            var miss = new List<string>();
            var mine = _game.Cities.Where(c => c.OwnerIndex == p.Index).ToList();
            void Need(int have, int need, string what) { if (have < need) miss.Add($"{what} {have}/{need}"); }
            switch (epoch)
            {
                case EpochTrade:
                    Need(Count(p, TechBranch.Trade), 3, "узлы торговли");
                    Need(Count(p, TechBranch.Governance), 2, "узлы управления");
                    if (!mine.Any(c => c.Tier >= Runeterra.Cities.SettlementTier.City)) miss.Add("поселение уровня «Город»");
                    break;
                case EpochCraft:
                    Need(Count(p, TechBranch.Craft), 3, "узлы ремесла");
                    Need(Count(p, TechBranch.Trade), 2, "узлы торговли");
                    if (!mine.Any(c => c.Buildings.Any(kv => kv.Key.IsWorkshop && kv.Value >= 2))) miss.Add("кластер (2 одинаковые мастерские)");
                    break;
                default:
                    Need(Count(p, TechBranch.Knowledge), 3, "узлы знания");
                    if (!mine.Any(c => c.Population >= 6)) miss.Add("город с 6 жителями");
                    break;
            }
            return miss;
        }

        public static readonly string[] AllEpochs = { EpochTrade, EpochCraft, EpochKnowledge };

        private void CheckEpochs(PlayerState p)
        {
            foreach (var e in AllEpochs)
            {
                if (p.Epochs.Contains(e) || EpochMissing(p, e).Count > 0) continue;
                p.Epochs.Add(e);
                _game.Report($"{p.Region.displayName}: наступила «{EpochName(e)}» — {EpochEffect(e)}");
            }
        }

        /// <summary>Бонус к силе юнита от узла.</summary>
        public static int UnitBonus(string techId, Runeterra.Units.Unit u)
        {
            if (!GameState.CanFight(u)) return 0;
            if (techId == "elite") return 2;
            if (techId == "heavycav" && u.Data.role == Runeterra.Units.UnitRole.Melee) return 4;
            if (techId == "salary") return 1;
            return 0;
        }

        /// <summary>Сумма бонусов изученных узлов для нового юнита.</summary>
        public int NewUnitBonus(PlayerState p, Runeterra.Units.Unit u) => p.Techs.Sum(id => UnitBonus(id, u));
    }
}

using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Units;

namespace Runeterra.Core
{
    /// <summary>
    /// Дипломатия ИИ: войну объявляет только по созревшей претензии и при перевесе сил,
    /// претензии создаёт на города слабых соседей, затяжную или проигрышную войну заканчивает миром,
    /// с соседями, к которым расположен, заключает пакты и союзы.
    /// </summary>
    public partial class AiPlayer
    {
        private System.Random _diploRandom;

        private System.Random Rng => _diploRandom ??= new System.Random(_game.Grid.Seed * 17 + 3);

        /// <summary>Военная сила стороны: сумма сил боевых юнитов и по 10 за город.</summary>
        public int Strength(int player) =>
            _game.Players[player].Units.Where(u => u.IsAlive && GameState.CanFight(u)).Sum(u => u.Data.IsRanged ? u.RangedStrength : u.MeleeStrength) +
            10 * _game.Cities.Count(c => c.OwnerIndex == player);

        /// <summary>Сила стороны вместе с союзниками.</summary>
        private int BlocStrength(int player) => Strength(player) + _game.Diplomacy.AlliesOf(player).Sum(Strength);

        /// <summary>Сколько боевых юнитов держать: в мире — гарнизоны, при войне или её подготовке — больше.</summary>
        private int ArmyTarget(PlayerState player)
        {
            int cities = _game.Cities.Count(c => c.OwnerIndex == player.Index);
            if (_game.Diplomacy.EnemiesOf(player.Index).Any()) return cities * 4 + 4;
            if (_game.Diplomacy.ClaimsOf(player.Index).Any()) return cities * 3 + 2;
            return (int)(cities * 1.5f) + 2;
        }

        private bool WantsArmy(PlayerState player) =>
            player.Units.Count(u => u.IsAlive && GameState.CanFight(u)) < ArmyTarget(player);

        private void PlayDiplomacy(PlayerState player)
        {
            var d = _game.Diplomacy;
            int me = player.Index;
            if (d.WarsDisabled)
            {
                SeekTreaties(player);
                return;
            }

            // 1. Мир: затяжная или проигрышная война.
            foreach (int enemy in d.EnemiesOf(me).ToList())
            {
                if (d.CanMakePeace(me, enemy) != null) continue;
                int turns = d.WarTurns(me, enemy);
                bool losing = BlocStrength(me) * 1.1f < BlocStrength(enemy);
                bool tired = turns >= 20;
                if (!(losing && turns >= 6) && !tired) continue;
                OfferPeace(me, enemy);
            }

            // 2. Война по созревшей претензии, если перевес явный и воевать больше не с кем.
            if (!d.EnemiesOf(me).Any())
            {
                foreach (var claim in d.ClaimsOf(me).Where(d.IsRipe).ToList())
                {
                    var city = d.CityById(claim.CityId);
                    int target = city.OwnerIndex;
                    if (d.CanDeclareWar(me, target) != null) continue;
                    if (d.Opinion(me, target) > 30) continue;
                    if (Strength(me) < BlocStrength(target) * 1.3f) continue;
                    d.DeclareWar(me, target);
                    break;
                }
            }

            // 3. Новая претензия: на город слабого нелюбимого соседа, если своих претензий нет.
            if (_game.Turns.Turn > 8 && !d.ClaimsOf(me).Any() && !d.EnemiesOf(me).Any() && Rng.NextDouble() < 0.12)
            {
                var mine = _game.Cities.Where(c => c.OwnerIndex == me).ToList();
                var pick = _game.Cities.Where(c => c.OwnerIndex != me && d.CanFabricate(me, c) == null &&
                                                   d.StanceOf(me, c.OwnerIndex) != Stance.Alliance && d.Opinion(me, c.OwnerIndex) < 10)
                    .OrderBy(c => Strength(c.OwnerIndex) + d.Opinion(me, c.OwnerIndex))
                    .ThenBy(c => mine.Min(m => m.Coord.DistanceTo(c.Coord)))
                    .FirstOrDefault();
                if (pick != null && player.Gold >= Diplomacy.ClaimCost + 40) d.Fabricate(me, pick);
            }

            SeekTreaties(player);
        }

        // ---------- Торговля с другими странами ----------

        /// <summary>
        /// Торговая политика ИИ: соглашения с теми, к кому не враждебен; эмбарго против заклятых врагов;
        /// покупка у соседей недостающих стратегических товаров (кони, соль, стекло).
        /// </summary>
        private void PlayForeignTrade(PlayerState player)
        {
            var t = _game.Trade;
            var d = _game.Diplomacy;
            int me = player.Index;
            foreach (var other in _game.Players.Where(p => p.Index != me && !_game.IsEliminated(p)))
            {
                int o = other.Index;
                int opinion = d.Opinion(me, o);
                if (!t.Embargoes(me, o) && opinion <= -60 && !d.AtWar(me, o)) t.SetEmbargo(me, o, true);
                else if (t.Embargoes(me, o) && opinion > -30) t.SetEmbargo(me, o, false);
                if (t.CanSignAgreement(me, o) == null && opinion >= 0 && Rng.NextDouble() < 0.25)
                {
                    if (other.IsHuman) d.Propose(me, o, ProposalKind.TradeAgreement);
                    else if (Accepts(o, me, ProposalKind.TradeAgreement)) t.SignAgreement(me, o);
                }
            }

            var capital = _game.CapitalOf(player);
            if (capital == null) return;
            foreach (var good in NeededGoods(player))
            {
                if (capital.Warehouse.Get(good) >= 4) continue;
                var seller = _game.Players.Where(p => p.Index != me && !p.IsHuman && t.CanBuy(me, p.Index, good) == null && t.SellerAgrees(p.Index, me, good))
                    .OrderBy(p => t.DealBuyCost(p.Index, good)).FirstOrDefault();
                if (seller != null && player.Gold >= t.DealBuyCost(seller.Index, good) + 40) t.Buy(me, seller.Index, good);
            }
        }

        /// <summary>Стратегические товары, без которых стоят найм или постройки.</summary>
        private IEnumerable<GoodData> NeededGoods(PlayerState player)
        {
            var needed = new HashSet<GoodData>();
            if (WantsArmy(player))
                foreach (var u in _shop.Where(u => u.role != UnitRole.Civilian))
                    foreach (var g in u.goodsCost) if (g.good != null) needed.Add(g.good);
            foreach (var b in _buildings.Where(b => b.epidemicReduction > 0))
                foreach (var g in b.goodsCost) if (g.good != null && g.good.fromDeposit) needed.Add(g.good);
            return needed;
        }

        private void OfferPeace(int me, int enemy)
        {
            var d = _game.Diplomacy;
            if (_game.Players[enemy].IsHuman) d.Propose(me, enemy, ProposalKind.Peace);
            else if (Accepts(enemy, me, ProposalKind.Peace)) d.MakePeace(me, enemy);
        }

        /// <summary>Пакты с соседями, к которым расположен; союз при высоком мнении.</summary>
        private void SeekTreaties(PlayerState player)
        {
            var d = _game.Diplomacy;
            int me = player.Index;
            if (Rng.NextDouble() > 0.2) return;
            foreach (var other in _game.Players.Where(p => p.Index != me && !_game.IsEliminated(p)).OrderByDescending(p => d.Opinion(me, p.Index)))
            {
                int o = other.Index;
                if (d.ClaimsOf(me).Any(c => d.CityById(c.CityId).OwnerIndex == o)) continue;
                ProposalKind? kind = d.CanAlly(me, o) == null && d.Opinion(me, o) >= Diplomacy.AllianceOpinion + 10 ? ProposalKind.Alliance
                    : d.StanceOf(me, o) == Stance.Peace && d.CanSignPact(me, o) == null && d.Opinion(me, o) >= 0 ? ProposalKind.NonAggression
                    : (ProposalKind?)null;
                if (kind == null) continue;
                if (other.IsHuman) d.Propose(me, o, kind.Value);
                else if (Accepts(o, me, kind.Value))
                {
                    if (kind == ProposalKind.Alliance) d.Ally(me, o);
                    else d.SignPact(me, o);
                }
                return;
            }
        }

        /// <summary>Согласится ли ИИ-сторона who на предложение стороны from.</summary>
        public bool Accepts(int who, int from, ProposalKind kind)
        {
            var d = _game.Diplomacy;
            switch (kind)
            {
                case ProposalKind.Peace:
                    if (d.CanMakePeace(who, from) != null) return false;
                    return BlocStrength(who) < BlocStrength(from) * 1.5f || d.WarTurns(who, from) >= 20;
                case ProposalKind.TradeAgreement:
                    return _game.Trade.CanSignAgreement(who, from) == null && d.Opinion(who, from) >= -10;
                case ProposalKind.NonAggression:
                    if (d.CanSignPact(who, from) != null) return false;
                    // Не связываем себе руки, если сами готовим войну против них.
                    return !d.ClaimsOf(who).Any(c => d.CityById(c.CityId).OwnerIndex == from);
                default:
                    return d.CanAlly(who, from) == null && d.Opinion(who, from) >= Diplomacy.AllianceOpinion;
            }
        }

        /// <summary>Игрок предлагает ИИ договор: принять сразу или отказать.</summary>
        public bool AnswerHuman(int human, int ai, ProposalKind kind)
        {
            var d = _game.Diplomacy;
            if (!Accepts(ai, human, kind))
            {
                _game.Report($"{_game.Players[ai].Region.displayName} отклоняет предложение: {Diplomacy.KindName(kind)}");
                return false;
            }
            switch (kind)
            {
                case ProposalKind.Peace: d.MakePeace(human, ai); break;
                case ProposalKind.NonAggression: d.SignPact(human, ai); break;
                case ProposalKind.TradeAgreement: _game.Trade.SignAgreement(human, ai); break;
                default: d.Ally(human, ai); break;
            }
            return true;
        }
    }
}

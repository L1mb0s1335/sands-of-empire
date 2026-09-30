using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Units;

namespace Runeterra.Core
{
    /// <summary>
    /// Дипломатия ИИ по характеру страны (<see cref="AiPersonality"/>): войну объявляет только по созревшей претензии,
    /// выждав и набрав нужный характеру перевес; претензии заявляет прежде всего на город своей цели и на сильнейшего
    /// (коалиция); затяжную или проигрышную войну заканчивает миром; с соседями заключает пакты и союзы;
    /// правитель с низким доверием рвёт пакт, чтобы напасть.
    /// </summary>
    public partial class AiPlayer
    {
        private System.Random _diploRandom;

        private System.Random Rng => _diploRandom ??= new System.Random(_game.Grid.Seed * 17 + 3);

        private readonly Dictionary<int, AiPersonality> _personalities = new Dictionary<int, AiPersonality>();

        /// <summary>Характер ИИ стороны.</summary>
        public AiPersonality Personality(int player)
        {
            if (!_personalities.TryGetValue(player, out var p))
                _personalities[player] = p = AiPersonality.For(_game.Players[player].Region.id);
            return p;
        }

        public int Strength(int player) => _game.MilitaryStrength(player);

        /// <summary>Сила стороны вместе с союзниками.</summary>
        private int BlocStrength(int player) => Strength(player) + _game.Diplomacy.AlliesOf(player).Sum(Strength);

        /// <summary>Сколько боевых юнитов держать: зависит от войны, её подготовки и характера.</summary>
        private int ArmyTarget(PlayerState player)
        {
            var ch = Personality(player.Index);
            int cities = _game.Cities.Count(c => c.OwnerIndex == player.Index);
            if (_game.Diplomacy.WarsDisabled) return cities; // мирный сценарий: только стража городов
            if (_game.Diplomacy.EnemiesOf(player.Index).Any()) return (int)(cities * (3f + 2f * ch.Aggression)) + 3;
            bool preparing = _game.Diplomacy.ClaimsOf(player.Index).Any() || _game.Diplomacy.CoalitionTarget == player.Index;
            if (preparing) return (int)(cities * (1.8f + ch.Aggression + ch.Caution * 0.5f)) + 2;
            return (int)(cities * (0.8f + ch.Aggression * 0.6f + ch.Caution * 0.6f - ch.Development * 0.3f)) + 2;
        }

        private bool WantsArmy(PlayerState player) =>
            player.Units.Count(u => u.IsAlive && GameState.CanFight(u)) < ArmyTarget(player);

        private void PlayDiplomacy(PlayerState player)
        {
            var d = _game.Diplomacy;
            int me = player.Index;
            var ch = Personality(me);
            int turn = _game.Turns.Turn;

            // Набожность: раз в 5 ходов теплеет к единоверцам и холодеет к иноверцам.
            if (ch.Piety >= 0.7f && turn % 5 == me % 5)
                foreach (var other in _game.Players.Where(p => p.Index != me))
                    d.AddOpinion(me, other.Index, d.SameFaith(me, other.Index) ? 1 : -1);

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
                bool losing = BlocStrength(me) * (1f + 0.3f * ch.Caution) < BlocStrength(enemy);
                bool tired = turns >= 10 + (int)(25 * ch.Aggression);
                bool lostCities = _game.Cities.Any(c => c.FounderIndex == me && c.OwnerIndex == enemy);
                if (!(losing && turns >= 5) && !tired && !(lostCities && ch.Caution > 0.6f && turns >= 5)) continue;
                OfferPeace(me, enemy);
            }

            // 2. Война по созревшей претензии: не раньше срока характера, при нужном перевесе.
            if (!d.EnemiesOf(me).Any() && turn >= ch.EarliestWar)
            {
                foreach (var claim in d.ClaimsOf(me).Where(d.IsRipe).OrderBy(c => c.CityId == ch.GoalCity ? 0 : 1).ToList())
                {
                    var city = d.CityById(claim.CityId);
                    int target = city.OwnerIndex;
                    bool coalition = d.InCoalition(me) && d.CoalitionTarget == target;
                    float ratio = ch.WarRatio - (coalition ? 0.3f : 0f) - (claim.CityId == ch.GoalCity ? 0.1f : 0f);
                    if (d.Opinion(me, target) > 20 + (int)(30 * ch.Aggression)) continue;
                    if (BlocStrength(me) < BlocStrength(target) * ratio) continue;
                    // Вероломный правитель рвёт пакт, чтобы через ход напасть.
                    if (d.StanceOf(me, target) == Stance.NonAggression && ch.Trust < 0.45f && BlocStrength(me) >= BlocStrength(target) * (ratio + 0.3f))
                    {
                        d.BreakTreaty(me, target);
                        break;
                    }
                    if (d.CanDeclareWar(me, target) != null) continue;
                    d.DeclareWar(me, target);
                    break;
                }
            }

            // 3. Новая претензия: город цели, иначе город сильнейшего (коалиция), иначе слабого нелюбимого соседа.
            if (turn > 8 && !d.EnemiesOf(me).Any() && d.ClaimsOf(me).Count() < 2 && Rng.NextDouble() < ch.ClaimChance &&
                player.Gold >= Diplomacy.ClaimCost + 40)
            {
                var mine = _game.Cities.Where(c => c.OwnerIndex == me).ToList();
                var goal = ch.GoalCity != null ? d.CityById(ch.GoalCity) : null;
                City pick = goal != null && d.CanFabricate(me, goal) == null && d.StanceOf(me, goal.OwnerIndex) != Stance.Alliance ? goal : null;
                pick ??= _game.Cities.Where(c => c.OwnerIndex != me && d.CanFabricate(me, c) == null &&
                                                 d.StanceOf(me, c.OwnerIndex) != Stance.Alliance &&
                                                 (d.Opinion(me, c.OwnerIndex) < 10 || (d.InCoalition(me) && d.CoalitionTarget == c.OwnerIndex)))
                    .OrderBy(c => d.InCoalition(me) && d.CoalitionTarget == c.OwnerIndex ? 0 : 1)
                    .ThenBy(c => Strength(c.OwnerIndex) + d.Opinion(me, c.OwnerIndex))
                    .ThenBy(c => mine.Min(m => m.Coord.DistanceTo(c.Coord)))
                    .FirstOrDefault();
                if (pick != null) d.Fabricate(me, pick);
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
                var ch = Personality(me);
                int embargoAt = -80 + (int)(40 * (1f - ch.Trade));
                if (!t.Embargoes(me, o) && opinion <= embargoAt && !d.AtWar(me, o)) t.SetEmbargo(me, o, true);
                else if (t.Embargoes(me, o) && opinion > embargoAt + 30) t.SetEmbargo(me, o, false);
                if (t.CanSignAgreement(me, o) == null && opinion >= 10 - (int)(30 * ch.Trade) && Rng.NextDouble() < 0.1 + 0.3 * ch.Trade)
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

        /// <summary>Пакты с соседями, к которым расположен; союз при высоком мнении (в коалиции — легче).</summary>
        private void SeekTreaties(PlayerState player)
        {
            var d = _game.Diplomacy;
            int me = player.Index;
            var ch = Personality(me);
            if (Rng.NextDouble() > 0.1 + 0.25 * ch.Caution) return;
            foreach (var other in _game.Players.Where(p => p.Index != me && !_game.IsEliminated(p)).OrderByDescending(p => d.Opinion(me, p.Index)))
            {
                int o = other.Index;
                if (d.ClaimsOf(me).Any(c => d.CityById(c.CityId).OwnerIndex == o)) continue;
                if (d.CoalitionTarget == o && d.InCoalition(me)) continue;
                if (ch.GoalCity != null && d.CityById(ch.GoalCity)?.OwnerIndex == o && ch.Aggression >= 0.5f) continue;
                int allyAt = d.AllianceOpinionFor(me, o) + (int)(20 * (0.5f - ch.Trust));
                ProposalKind? kind = d.CanAlly(me, o) == null && d.Opinion(me, o) >= allyAt ? ProposalKind.Alliance
                    : d.StanceOf(me, o) == Stance.Peace && d.CanSignPact(me, o) == null && d.Opinion(me, o) >= 10 - (int)(20 * ch.Caution) ? ProposalKind.NonAggression
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

        /// <summary>Согласится ли ИИ-сторона who на предложение стороны from (по своему характеру).</summary>
        public bool Accepts(int who, int from, ProposalKind kind)
        {
            var d = _game.Diplomacy;
            var ch = Personality(who);
            switch (kind)
            {
                case ProposalKind.Peace:
                    if (d.CanMakePeace(who, from) != null) return false;
                    return BlocStrength(who) < BlocStrength(from) * (1.2f + 0.6f * (1f - ch.Aggression)) ||
                           d.WarTurns(who, from) >= 10 + (int)(25 * ch.Aggression);
                case ProposalKind.TradeAgreement:
                    return _game.Trade.CanSignAgreement(who, from) == null && d.Opinion(who, from) >= -(int)(30 * ch.Trade);
                case ProposalKind.NonAggression:
                    if (d.CanSignPact(who, from) != null) return false;
                    // Не связываем себе руки, если сами готовим войну против них или это цель коалиции.
                    if (d.ClaimsOf(who).Any(c => d.CityById(c.CityId).OwnerIndex == from)) return false;
                    if (d.InCoalition(who) && d.CoalitionTarget == from) return false;
                    return d.Opinion(who, from) >= -10 - (int)(20 * ch.Caution);
                default:
                    return d.CanAlly(who, from) == null && d.Opinion(who, from) >= d.AllianceOpinionFor(who, from) + (int)(20 * (0.5f - ch.Trust));
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

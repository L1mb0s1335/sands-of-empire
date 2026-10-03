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
    /// Действие стороны над партией. Все изменения состояния игроком и ИИ идут через команды
    /// (<see cref="CommandBus"/>): команда — только данные (id юнитов, городов, контента, координаты),
    /// её можно записать в журнал, переслать по сети и применить заново с тем же результатом.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Сторона, от имени которой действие (должна ходить сейчас).</summary>
        int Player { get; }

        /// <summary>Причина, по которой команду нельзя применить (null — можно).</summary>
        string Validate(GameState s);

        /// <summary>Применить к состоянию. Вызывается только после успешной проверки.</summary>
        bool Apply(GameState s);
    }

    /// <summary>Общая часть команд: сторона и поиск сущностей по id.</summary>
    [Serializable]
    public abstract class Command : ICommand
    {
        public int player;
        public int Player => player;

        protected Command(int player) => this.player = player;

        public abstract string Validate(GameState s);
        public abstract bool Apply(GameState s);

        protected PlayerState Me(GameState s) => s.Players[player];

        protected Unit OwnUnit(GameState s, int id) =>
            player >= 0 && player < s.Players.Count ? s.Players[player].Units.FirstOrDefault(u => u.Id == id && u.IsAlive) : null;

        protected static City CityOf(GameState s, string id) => s.Cities.FirstOrDefault(c => c.Data.id == id);

        protected City OwnCity(GameState s, string id) => CityOf(s, id) is City c && c.OwnerIndex == player ? c : null;

        protected static bool ValidPlayer(GameState s, int index) => index >= 0 && index < s.Players.Count;

        protected static GoodData GoodOf(GameState s, string id) => s.Goods.FirstOrDefault(g => g.id == id);
    }

    // ---------- Ход ----------

    /// <summary>Конец хода стороны: правила следующего хода применяются внутри.</summary>
    [Serializable]
    public sealed class EndTurnCommand : Command
    {
        public EndTurnCommand(int player) : base(player) { }
        public override string Validate(GameState s) => null;
        public override bool Apply(GameState s) { s.Turns.EndTurn(); return true; }
    }

    // ---------- Юниты ----------

    /// <summary>Движение по пути; можно остановиться, дойдя на stopDist клеток до stopNear.</summary>
    [Serializable]
    public sealed class MoveUnitCommand : Command
    {
        public int unit;
        public List<HexCoord> path;
        public bool hasStop;
        public HexCoord stopNear;
        public int stopDist;

        public MoveUnitCommand(int player, int unit, List<HexCoord> path, HexCoord? stopNear = null, int stopDist = 0) : base(player)
        {
            this.unit = unit;
            this.path = path?.ToList();
            hasStop = stopNear != null;
            this.stopNear = stopNear ?? default;
            this.stopDist = stopDist;
        }

        public override string Validate(GameState s)
        {
            // Пустой путь допустим: движения нет, но обзор пересчитывается (как и раньше при любом приказе).
            return OwnUnit(s, unit) == null ? "нет такого юнита" : null;
        }

        public override bool Apply(GameState s)
        {
            var u = OwnUnit(s, unit);
            var from = u.Coord;
            Func<HexCoord, bool> stop = null;
            if (hasStop)
            {
                var near = stopNear;
                int dist = stopDist;
                stop = c => c.DistanceTo(near) <= dist;
            }
            s.MoveUnit(u, path ?? new List<HexCoord>(), stop);
            return u.Coord != from;
        }
    }

    [Serializable]
    public sealed class AttackCommand : Command
    {
        public int unit;
        public HexCoord target;

        public AttackCommand(int player, int unit, HexCoord target) : base(player) { this.unit = unit; this.target = target; }

        public override string Validate(GameState s)
        {
            var u = OwnUnit(s, unit);
            if (u == null) return "нет такого юнита";
            if (!GameState.CanFight(u)) return "юнит не воюет";
            return s.IsTarget(u, target) ? null : "не цель";
        }

        public override bool Apply(GameState s) => s.AttackTarget(OwnUnit(s, unit), target);
    }

    [Serializable]
    public sealed class FoundCityCommand : Command
    {
        public int unit;

        public FoundCityCommand(int player, int unit) : base(player) => this.unit = unit;

        public override string Validate(GameState s)
        {
            var u = OwnUnit(s, unit);
            if (u == null) return "нет такого юнита";
            if (u.MovesLeft <= 0) return "нет очков движения";
            return s.CanFoundCity(u, u.Coord);
        }

        public override bool Apply(GameState s) => s.FoundCity(OwnUnit(s, unit)) != null;
    }

    /// <summary>Строитель ставит район (рынок или порт) у соседнего своего города.</summary>
    [Serializable]
    public sealed class BuildDistrictCommand : Command
    {
        public int unit;
        public string district;

        public BuildDistrictCommand(int player, int unit, string district) : base(player) { this.unit = unit; this.district = district; }

        private DistrictData District(GameState s) =>
            s.Market != null && s.Market.id == district ? s.Market : s.Port != null && s.Port.id == district ? s.Port : null;

        public override string Validate(GameState s)
        {
            var u = OwnUnit(s, unit);
            if (u == null) return "нет такого юнита";
            var d = District(s);
            if (d == null) return "нет такого района";
            return s.DistrictCityFor(u, d, out var reason) != null ? null : reason;
        }

        public override bool Apply(GameState s) => s.BuildDistrict(OwnUnit(s, unit), District(s));
    }

    [Serializable]
    public sealed class BuildRoadCommand : Command
    {
        public int unit;

        public BuildRoadCommand(int player, int unit) : base(player) => this.unit = unit;

        public override string Validate(GameState s) => OwnUnit(s, unit) is Unit u ? s.CanBuildRoad(u) : "нет такого юнита";
        public override bool Apply(GameState s) => s.BuildRoad(OwnUnit(s, unit));
    }

    // ---------- Города ----------

    /// <summary>Что строит город: kind 0 — ничего, 1 — юнит, 2 — район, 3 — постройка; id — id контента.</summary>
    [Serializable]
    public sealed class SetBuildCommand : Command
    {
        public string city;
        public int kind;
        public string item;

        public SetBuildCommand(int player, City city, BuildItem build) : base(player)
        {
            this.city = city.Data.id;
            kind = build == null ? 0 : build.Unit != null ? 1 : build.District != null ? 2 : 3;
            item = build == null ? null : build.Unit != null ? build.Unit.id : build.District != null ? build.District.id : build.Building.id;
        }

        private BuildItem Item(GameState s)
        {
            switch (kind)
            {
                case 1: return s.FindUnitData(item) is UnitData u ? new BuildItem(u) : null;
                case 2:
                    var d = s.Market != null && s.Market.id == item ? s.Market : s.Port != null && s.Port.id == item ? s.Port : null;
                    return d != null ? new BuildItem(d) : null;
                case 3: return s.BuildingTypes.FirstOrDefault(b => b.id == item) is BuildingData b ? new BuildItem(b) : null;
                default: return null;
            }
        }

        public override string Validate(GameState s)
        {
            if (OwnCity(s, city) == null) return "нет такого города";
            if (kind != 0 && Item(s) == null) return "нет такого заказа";
            return null;
        }

        public override bool Apply(GameState s)
        {
            var c = OwnCity(s, city);
            var before = c.CurrentBuild;
            s.SetBuild(c, Item(s));
            return c.CurrentBuild != before;
        }
    }

    [Serializable]
    public sealed class RushCommand : Command
    {
        public string city;

        public RushCommand(int player, City city) : base(player) => this.city = city.Data.id;

        public override string Validate(GameState s) => OwnCity(s, city) is City c ? s.CanRush(c, Me(s)) : "нет такого города";
        public override bool Apply(GameState s) => s.Rush(OwnCity(s, city), Me(s));
    }

    [Serializable]
    public sealed class DraftCommand : Command
    {
        public string city;

        public DraftCommand(int player, City city) : base(player) => this.city = city.Data.id;

        public override string Validate(GameState s) => OwnCity(s, city) is City c ? s.CanDraft(c, Me(s)) : "нет такого города";
        public override bool Apply(GameState s) => s.Draft(OwnCity(s, city), Me(s)) != null;
    }

    /// <summary>Покупка юнита за золото в своём городе.</summary>
    [Serializable]
    public sealed class BuyUnitCommand : Command
    {
        public string city;
        public string unit;

        public BuyUnitCommand(int player, City city, UnitData unit) : base(player) { this.city = city.Data.id; this.unit = unit.id; }

        public override string Validate(GameState s)
        {
            var c = OwnCity(s, city);
            if (c == null) return "нет такого города";
            var data = s.FindUnitData(unit);
            if (data == null) return "нет такого юнита";
            return s.CanBuy(Me(s), c, data, out var reason) ? null : reason;
        }

        public override bool Apply(GameState s) => s.Buy(Me(s), OwnCity(s, city), s.FindUnitData(unit)) != null;
    }

    // ---------- Казна ----------

    public enum Rate { LandTax, PeopleTax, LuxuryTax, Tariff }

    /// <summary>Ставка налога или пошлины (0–30%).</summary>
    [Serializable]
    public sealed class SetRateCommand : Command
    {
        public Rate rate;
        public float value;

        public SetRateCommand(int player, Rate rate, float value) : base(player) { this.rate = rate; this.value = value; }

        public override string Validate(GameState s) => value >= 0f && value <= 0.3001f ? null : "ставка вне 0–30%";

        public override bool Apply(GameState s)
        {
            var p = Me(s);
            switch (rate)
            {
                case Rate.LandTax: p.LandTax = value; break;
                case Rate.PeopleTax: p.PeopleTax = value; break;
                case Rate.LuxuryTax: p.LuxuryTax = value; break;
                default: p.Tariff = value; break;
            }
            return true;
        }
    }

    /// <summary>Перевод золота в резерв (amount &gt; 0) или из резерва в казну (amount &lt; 0).</summary>
    [Serializable]
    public sealed class ReserveCommand : Command
    {
        public int amount;

        public ReserveCommand(int player, int amount) : base(player) => this.amount = amount;

        public override string Validate(GameState s)
        {
            var p = Me(s);
            if (amount == 0) return "ноль";
            if (amount > 0 && p.Gold < amount) return "не хватает золота";
            if (amount < 0 && p.Reserve < -amount) return "резерв меньше";
            return null;
        }

        public override bool Apply(GameState s)
        {
            var p = Me(s);
            p.Gold -= amount;
            p.Reserve += amount;
            return true;
        }
    }

    public enum TreasuryAction { Debase, Recoin, Borrow, Repay }

    /// <summary>Порча и перечеканка монеты, заём и возврат долга.</summary>
    [Serializable]
    public sealed class TreasuryCommand : Command
    {
        public TreasuryAction action;

        public TreasuryCommand(int player, TreasuryAction action) : base(player) => this.action = action;

        public override string Validate(GameState s) => null;

        public override bool Apply(GameState s)
        {
            var p = Me(s);
            switch (action)
            {
                case TreasuryAction.Debase: return s.Debase(p);
                case TreasuryAction.Recoin: return s.Recoin(p);
                case TreasuryAction.Borrow: return s.Borrow(p);
                default: return s.Repay(p);
            }
        }
    }

    // ---------- Развитие ----------

    [Serializable]
    public sealed class ResearchCommand : Command
    {
        public string tech;

        public ResearchCommand(int player, string tech) : base(player) => this.tech = tech;

        public override string Validate(GameState s)
        {
            var t = s.Tech.All.FirstOrDefault(x => x.id == tech);
            if (t == null) return "нет такого узла";
            return s.Tech.IsAvailable(Me(s), t) ? null : "узел недоступен";
        }

        public override bool Apply(GameState s)
        {
            var p = Me(s);
            var before = p.Researching;
            s.Tech.Choose(p, s.Tech.All.First(x => x.id == tech));
            return p.Researching != before;
        }
    }

    // ---------- Дипломатия ----------

    [Serializable]
    public sealed class DeclareWarCommand : Command
    {
        public int target;

        public DeclareWarCommand(int player, int target) : base(player) => this.target = target;

        public override string Validate(GameState s) => ValidPlayer(s, target) ? s.Diplomacy.CanDeclareWar(player, target) : "нет такой стороны";
        public override bool Apply(GameState s) => s.Diplomacy.DeclareWar(player, target);
    }

    /// <summary>Претензия на чужой город.</summary>
    [Serializable]
    public sealed class ClaimCommand : Command
    {
        public string city;

        public ClaimCommand(int player, City city) : base(player) => this.city = city.Data.id;

        public override string Validate(GameState s) => CityOf(s, city) is City c ? s.Diplomacy.CanFabricate(player, c) : "нет такого города";
        public override bool Apply(GameState s) => s.Diplomacy.Fabricate(player, CityOf(s, city));
    }

    [Serializable]
    public sealed class BreakTreatyCommand : Command
    {
        public int other;

        public BreakTreatyCommand(int player, int other) : base(player) => this.other = other;

        public override string Validate(GameState s)
        {
            if (!ValidPlayer(s, other) || other == player) return "нет такой стороны";
            var st = s.Diplomacy.StanceOf(player, other);
            return st == Stance.NonAggression || st == Stance.Alliance ? null : "договора нет";
        }

        public override bool Apply(GameState s) { s.Diplomacy.BreakTreaty(player, other); return true; }
    }

    /// <summary>
    /// Договор двух сторон, на который вторая сторона уже согласилась (ИИ решил за неё,
    /// либо это ответ на её предложение): мир, пакт, союз или торговое соглашение.
    /// </summary>
    [Serializable]
    public sealed class TreatyCommand : Command
    {
        public int other;
        public ProposalKind kind;

        public TreatyCommand(int player, int other, ProposalKind kind) : base(player) { this.other = other; this.kind = kind; }

        public override string Validate(GameState s)
        {
            if (!ValidPlayer(s, other) || other == player) return "нет такой стороны";
            var d = s.Diplomacy;
            switch (kind)
            {
                case ProposalKind.Peace: return d.CanMakePeace(player, other);
                case ProposalKind.NonAggression: return d.CanSignPact(player, other);
                case ProposalKind.TradeAgreement: return s.Trade.CanSignAgreement(player, other);
                default: return d.CanAlly(player, other);
            }
        }

        public override bool Apply(GameState s)
        {
            var d = s.Diplomacy;
            switch (kind)
            {
                case ProposalKind.Peace: d.MakePeace(player, other); break;
                case ProposalKind.NonAggression: d.SignPact(player, other); break;
                case ProposalKind.TradeAgreement: s.Trade.SignAgreement(player, other); break;
                default: d.Ally(player, other); break;
            }
            return true;
        }
    }

    /// <summary>Предложение договора стороне-человеку (ответ — в окне «Дипломатия»).</summary>
    [Serializable]
    public sealed class ProposeCommand : Command
    {
        public int to;
        public ProposalKind kind;

        public ProposeCommand(int player, int to, ProposalKind kind) : base(player) { this.to = to; this.kind = kind; }

        public override string Validate(GameState s) => ValidPlayer(s, to) && to != player ? null : "нет такой стороны";
        public override bool Apply(GameState s) { s.Diplomacy.Propose(player, to, kind); return true; }
    }

    /// <summary>Ответ на предложение стороны from.</summary>
    [Serializable]
    public sealed class AnswerProposalCommand : Command
    {
        public int from;
        public ProposalKind kind;
        public bool accept;

        public AnswerProposalCommand(int player, int from, ProposalKind kind, bool accept) : base(player)
        {
            this.from = from;
            this.kind = kind;
            this.accept = accept;
        }

        private Proposal Find(GameState s) => s.Diplomacy.Proposals.FirstOrDefault(p => p.From == from && p.Kind == kind);

        public override string Validate(GameState s) => Find(s) != null ? null : "нет такого предложения";
        public override bool Apply(GameState s) { s.Diplomacy.Answer(Find(s), player, accept); return true; }
    }

    /// <summary>Набожный правитель раз в 5 ходов теплеет к единоверцам и холодеет к иноверцам.</summary>
    [Serializable]
    public sealed class PietyCommand : Command
    {
        public PietyCommand(int player) : base(player) { }

        public override string Validate(GameState s) =>
            s.Turns.Turn % 5 == player % 5 && AiPersonality.For(Me(s).Region.id).Piety >= 0.7f ? null : "не время";

        public override bool Apply(GameState s)
        {
            var d = s.Diplomacy;
            foreach (var other in s.Players.Where(p => p.Index != player))
                d.AddOpinion(player, other.Index, d.SameFaith(player, other.Index) ? 1 : -1);
            return true;
        }
    }

    // ---------- Торговля между странами ----------

    [Serializable]
    public sealed class EmbargoCommand : Command
    {
        public int against;
        public bool on;

        public EmbargoCommand(int player, int against, bool on) : base(player) { this.against = against; this.on = on; }

        public override string Validate(GameState s) => ValidPlayer(s, against) && against != player ? null : "нет такой стороны";
        public override bool Apply(GameState s) { s.Trade.SetEmbargo(player, against, on); return true; }
    }

    [Serializable]
    public sealed class CancelAgreementCommand : Command
    {
        public int other;
        public string reason;

        public CancelAgreementCommand(int player, int other, string reason) : base(player) { this.other = other; this.reason = reason; }

        public override string Validate(GameState s) => ValidPlayer(s, other) && s.Trade.HasAgreement(player, other) ? null : "соглашения нет";
        public override bool Apply(GameState s) { s.Trade.CancelAgreement(player, other, reason); return true; }
    }

    /// <summary>Сделка со столицей другой стороны: купить (buy) или продать партию товара.</summary>
    [Serializable]
    public sealed class DealCommand : Command
    {
        public int other;
        public string good;
        public bool buy;

        public DealCommand(int player, int other, GoodData good, bool buy) : base(player) { this.other = other; this.good = good.id; this.buy = buy; }

        public override string Validate(GameState s) => ValidPlayer(s, other) && GoodOf(s, good) != null ? null : "нет такой сделки";

        public override bool Apply(GameState s) =>
            buy ? s.Trade.Buy(player, other, GoodOf(s, good)) : s.Trade.Sell(player, other, GoodOf(s, good));
    }

    // ---------- Память ИИ ----------

    /// <summary>ИИ стороны решает наступать (армия собрана) или копить силы.</summary>
    [Serializable]
    public sealed class AiOffensiveCommand : Command
    {
        public bool on;

        public AiOffensiveCommand(int player, bool on) : base(player) => this.on = on;

        public override string Validate(GameState s) => s.AiOffensive.Contains(player) != on ? null : "без изменений";

        public override bool Apply(GameState s)
        {
            if (on) s.AiOffensive.Add(player);
            else s.AiOffensive.Remove(player);
            return true;
        }
    }
}

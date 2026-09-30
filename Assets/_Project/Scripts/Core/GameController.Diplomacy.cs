using System.Linq;
using Runeterra.Cities;
using UnityEngine;
using static Runeterra.Core.HudSkin;

namespace Runeterra.Core
{
    /// <summary>
    /// Окно «Дипломатия» (G): предложения от соседей, отношения со всеми сторонами, претензии,
    /// объявление войны, мир, пакты и союзы. Только отрисовка и вызовы правил из <see cref="Diplomacy"/>.
    /// </summary>
    public partial class GameController
    {
        private bool _showDiplomacy;
        private int _diploTab;
        private int _dealGood;

        public void SetDiplomacyTab(int tab) => _diploTab = tab;

        public void ShowDiplomacy(bool show) => _showDiplomacy = show;

        private Rect DiplomacyRect()
        {
            float w = Mathf.Min(W - 24, 980), h = Mathf.Min(H - 100, 640);
            return new Rect((W - w) / 2f, 84, w, h);
        }

        private static string OpinionText(int v)
        {
            var c = v >= 40 ? CGood : v >= 0 ? CGold : v > -40 ? CWarn : CBad;
            var word = v >= 60 ? "дружба" : v >= 20 ? "тёплые" : v > -20 ? "ровные" : v > -60 ? "холодные" : "вражда";
            return $"<color={c}><b>{(v > 0 ? "+" : "")}{v}</b> {word}</color>";
        }

        private string StanceText(Relation r)
        {
            var name = Diplomacy.StanceName(r.Stance);
            return r.Stance switch
            {
                Stance.War => $"<color={CBad}><b>Война</b></color> {Turns.Turn - r.Since} х.",
                Stance.Truce or Stance.NonAggression => $"<color={CCyan}>{char.ToUpper(name[0]) + name.Substring(1)}</color> до хода {r.Until}",
                Stance.Alliance => $"<color={CGood}><b>Союз</b></color>",
                _ => "Мир без договоров",
            };
        }

        private void DrawDiplomacy(PlayerState human)
        {
            var d = State.Diplomacy;
            var r = Hud(DiplomacyRect());
            Window(r);
            float pad = 22, y = r.y + 12;
            Label(new Rect(r.x + pad, y, 220, 30), "Дипломатия", Heading);
            if (CloseButton(r)) _showDiplomacy = false;
            if (Toggle(new Rect(r.x + 240, y + 2, 140, 30), _diploTab == 0, "Отношения")) _diploTab = 0;
            if (Toggle(new Rect(r.x + 386, y + 2, 140, 30), _diploTab == 1, "Торговля")) _diploTab = 1;
            y += 36;
            if (_diploTab == 1)
            {
                DrawTradeTab(human, r, y);
                return;
            }
            string rules = d.WarsDisabled
                ? "Сценарий «Мирное развитие»: войны отключены, соперничество — в хозяйстве, торговле и знаниях."
                : $"Войну можно объявить только по созревшей претензии на город соперника (своя — {Diplomacy.ClaimCost} золота, зреет {Diplomacy.ClaimMaturity} х.). " +
                  (Turns.Turn <= Diplomacy.StartTruceTurns ? $"<b>Стартовое перемирие до хода {Diplomacy.StartTruceTurns}.</b> " : "") +
                  "Союзники вступают в оборонительную войну. Мир — перемирие на 10 ходов.";
            if (d.CoalitionTarget >= 0)
                rules += $"\n<color={CBad}><b>Коалиция против {Players[d.CoalitionTarget].Region.displayName}</b></color>: " +
                         string.Join(", ", d.Coalition.Select(i => Players[i].Region.displayName));
            Label(new Rect(r.x + pad, y, r.width - pad * 2, 40), rules, BodySmall);
            y += 44;

            // Предложения соседей.
            foreach (var p in d.Proposals.ToList())
            {
                var from = Players[p.From];
                var row = new Rect(r.x + pad, y, r.width - pad * 2, 34);
                Fill(row, new Color(0.95f, 0.85f, 0.55f, 0.35f));
                Label(new Rect(row.x + 10, row.y + 6, row.width - 260, 24),
                    $"<b>{from.Region.displayName}</b> ({from.Region.leaderName}) предлагает: <b>{Diplomacy.KindName(p.Kind)}</b>", Body);
                if (Button(new Rect(row.xMax - 244, row.y + 3, 118, 28), "Принять")) d.Answer(p, human.Index, true);
                if (Button(new Rect(row.xMax - 120, row.y + 3, 118, 28), "Отклонить")) d.Answer(p, human.Index, false);
                y += 38;
            }
            Fill(new Rect(r.x + pad, y + 2, r.width - pad * 2, 1), GoldLine);
            y += 8;

            float rowH = 76;
            foreach (var other in Players.Where(p => p != human))
            {
                var row = new Rect(r.x + pad, y, r.width - pad * 2, rowH - 6);
                bool gone = State.IsEliminated(other);
                var rel = d.Get(human.Index, other.Index);
                Fill(new Rect(row.x, row.y + 4, 8, row.height - 8), other.Region.primaryColor);
                Fill(new Rect(row.x + 8, row.y + 4, 4, row.height - 8), other.Region.secondaryColor);

                int cities = Cities.Count(c => c.OwnerIndex == other.Index);
                Label(new Rect(row.x + 22, row.y + 2, 300, 24), $"<b>{other.Region.displayName}</b>", InkMid);
                Label(new Rect(row.x + 22, row.y + 26, 300, 20),
                    gone ? $"<color={CMuted}>сошла со сцены</color>" : $"{other.Region.leaderName} · городов {cities} · сила {_ai.Strength(other.Index)} · очки {State.Score(other)} · цель: {AiPersonality.GoalName(_ai.Personality(other.Index).Goal)}", CaptionInk);
                var ch = _ai.Personality(other.Index);
                var goalCity = ch.GoalCity != null ? State.Diplomacy.CityById(ch.GoalCity) : null;
                Tip(new Rect(row.x, row.y, 320, row.height),
                    $"{other.Region.description}\nЛидер: {other.Region.leaderName} — {other.Region.leaderAbilityName}: {other.Region.leaderAbilityText}\n" +
                    $"Характер: {ch.Summary}.\nЦель: {AiPersonality.GoalName(ch.Goal)}{(ch.Goal == AiGoal.Conquest && goalCity != null ? $" ({goalCity.Data.displayName})" : "")}\n" +
                    $"Агрессия {ch.Aggression:0.0} · торговля {ch.Trade:0.0} · развитие {ch.Development:0.0} · набожность {ch.Piety:0.0} · доверие {ch.Trust:0.0} · осторожность {ch.Caution:0.0}");

                if (!gone)
                {
                    Label(new Rect(row.x + 330, row.y + 2, 200, 24), OpinionText(rel.Value), Body);
                    Label(new Rect(row.x + 330, row.y + 26, 200, 20), StanceText(rel), BodySmall);

                    // Претензии: наши на их города и их на наши.
                    var ours = d.ClaimsOf(human.Index).Where(c => d.CityById(c.CityId).OwnerIndex == other.Index)
                        .Select(c => d.IsRipe(c) ? $"<color={CBad}>{d.CityById(c.CityId).Data.displayName}</color>"
                            : $"{d.CityById(c.CityId).Data.displayName} (ход {c.ReadyTurn})").ToList();
                    var theirs = d.ClaimsOf(other.Index).Where(c => d.CityById(c.CityId).OwnerIndex == human.Index)
                        .Select(c => d.CityById(c.CityId).Data.displayName).ToList();
                    var claims = (ours.Count > 0 ? $"Наши претензии: {string.Join(", ", ours)}" : "") +
                                 (theirs.Count > 0 ? $"{(ours.Count > 0 ? " · " : "")}<color={CWarn}>Их претензии: {string.Join(", ", theirs)}</color>" : "");
                    Label(new Rect(row.x + 22, row.y + 46, 540, 24), claims, BodySmall);

                    DrawDiplomacyButtons(human, other, row);
                }
                y += rowH;
                Fill(new Rect(r.x + pad, y - 4, r.width - pad * 2, 1), new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.35f));
            }
        }

        private void DrawDiplomacyButtons(PlayerState human, PlayerState other, Rect row)
        {
            var d = State.Diplomacy;
            int me = human.Index, o = other.Index;
            float bw = 128, bh = 30, x = row.xMax - bw * 3 - 8, y1 = row.y + 4, y2 = row.y + 38;

            // Ряд 1: война / мир, претензия.
            if (d.AtWar(me, o))
            {
                var reason = d.CanMakePeace(me, o);
                var b = new Rect(x, y1, bw * 2 + 4, bh);
                GUI.enabled = reason == null;
                if (Button(b, "Предложить мир")) _ai.AnswerHuman(me, o, ProposalKind.Peace);
                GUI.enabled = true;
                Tip(b, reason ?? "ИИ согласится, если война ему невыгодна или затянулась");
            }
            else
            {
                var reason = d.CanDeclareWar(me, o);
                var b = new Rect(x, y1, bw * 2 + 4, bh);
                GUI.enabled = reason == null;
                if (Button(b, reason == null ? "<color=#a3321e><b>Объявить войну</b></color>" : "Объявить войну")) d.DeclareWar(me, o);
                GUI.enabled = true;
                Tip(b, reason ?? $"Повод — претензия. Их союзники вступят в войну: {string.Join(", ", d.AlliesOf(o).Select(a => Players[a].Region.displayName).DefaultIfEmpty("нет"))}");
            }
            var target = ClaimTarget(me, o);
            var cr = new Rect(x + bw * 2 + 8, y1, bw, bh);
            string claimReason = target == null ? "нет города в пределах досягаемости" : d.CanFabricate(me, target);
            GUI.enabled = claimReason == null;
            if (Button(cr, "Претензия")) d.Fabricate(me, target);
            GUI.enabled = true;
            Tip(cr, target == null ? claimReason
                : claimReason ?? $"Заявить права на {target.Data.displayName} за {Diplomacy.ClaimCost} золота; созреет через {Diplomacy.ClaimMaturity} х. Их мнение −15");

            // Ряд 2: пакт, союз, разрыв.
            var stance = d.StanceOf(me, o);
            var pr = new Rect(x, y2, bw, bh);
            string pactReason = d.CanSignPact(me, o);
            GUI.enabled = pactReason == null;
            if (Button(pr, "Пакт")) _ai.AnswerHuman(me, o, ProposalKind.NonAggression);
            GUI.enabled = true;
            Tip(pr, pactReason ?? $"Пакт о ненападении на {Diplomacy.PactTurns} ходов: войну объявить нельзя, мнение растёт");

            var ar = new Rect(x + bw + 4, y2, bw, bh);
            string allyReason = d.CanAlly(me, o);
            GUI.enabled = allyReason == null;
            if (Button(ar, "Союз")) _ai.AnswerHuman(me, o, ProposalKind.Alliance);
            GUI.enabled = true;
            Tip(ar, allyReason ?? "Союзники вступают в войну, если на кого-то из вас нападут");

            var br = new Rect(x + bw * 2 + 8, y2, bw, bh);
            bool treaty = stance == Stance.NonAggression || stance == Stance.Alliance;
            GUI.enabled = treaty;
            if (Button(br, "Разорвать")) d.BreakTreaty(me, o);
            GUI.enabled = true;
            Tip(br, treaty ? "Разорвать договор: мнение −30" : "договора нет");
        }

        // ---------- Торговля ----------

        private void DrawTradeTab(PlayerState human, Rect r, float y)
        {
            var t = State.Trade;
            float pad = 22;
            Label(new Rect(r.x + pad, y, r.width - pad * 2, 40),
                "Торговое соглашение открывает границу караванам (ввозная пошлина вдвое меньше). Эмбарго закрывает её, война рвёт торговлю и конфискует караваны. " +
                $"Ваша пошлина {human.Tariff:0%} (меняется в «Казне»), ввозные пошлины за круг: <color={CGold}><b>+{human.ImportDutyLastTurn}</b></color>.", BodySmall);
            y += 44;

            // Товар для прямых сделок (со склада столицы).
            var dealGoods = goods.Where(g => g.IsRaw || g.tier > 0).ToList();
            if (dealGoods.Count == 0) return;
            _dealGood = (_dealGood % dealGoods.Count + dealGoods.Count) % dealGoods.Count;
            var good = dealGoods[_dealGood];
            var capital = State.CapitalOf(human);
            Label(new Rect(r.x + pad, y + 4, 250, 26), $"<b>Сделки партиями по {TradeSystem.DealAmount}:</b>", InkMid);
            if (Button(new Rect(r.x + pad + 250, y, 36, 30), "◀")) _dealGood--;
            Label(new Rect(r.x + pad + 290, y + 4, 220, 26), $"<b>{good.displayName}</b>", Center);
            if (Button(new Rect(r.x + pad + 514, y, 36, 30), "▶")) _dealGood++;
            Label(new Rect(r.x + pad + 560, y + 4, r.width - pad * 2 - 560, 26),
                $"в столице: {(capital != null ? capital.Warehouse.Get(good) : 0):0}, цена {(capital != null ? State.Trade.Price(capital, good) : 0):0.0}", CaptionInk);
            y += 38;
            Fill(new Rect(r.x + pad, y, r.width - pad * 2, 1), GoldLine);
            y += 6;

            float rowH = 76;
            foreach (var other in Players.Where(p => p != human))
            {
                int me = human.Index, o = other.Index;
                var row = new Rect(r.x + pad, y, r.width - pad * 2, rowH - 6);
                Fill(new Rect(row.x, row.y + 4, 8, row.height - 8), other.Region.primaryColor);
                Fill(new Rect(row.x + 8, row.y + 4, 4, row.height - 8), other.Region.secondaryColor);
                Label(new Rect(row.x + 22, row.y + 2, 300, 24), $"<b>{other.Region.displayName}</b>", InkMid);
                if (State.IsEliminated(other))
                {
                    Label(new Rect(row.x + 22, row.y + 26, 300, 20), $"<color={CMuted}>сошла со сцены</color>", CaptionInk);
                    y += rowH;
                    continue;
                }
                string status = State.AtWar(me, o) ? $"<color={CBad}>война — торговли нет</color>"
                    : t.HasEmbargo(me, o) ? $"<color={CBad}>эмбарго{(t.Embargoes(me, o) ? " (ваше)" : " (их)")}</color>"
                    : t.HasAgreement(me, o) ? $"<color={CGood}>торговое соглашение</color>" : "границы закрыты для караванов";
                Label(new Rect(row.x + 22, row.y + 26, 320, 20), status, BodySmall);
                var specialties = string.Join(", ", other.Region.specialties.Where(g => g != null).Select(g => g.displayName));
                Label(new Rect(row.x + 22, row.y + 46, 330, 20),
                    $"пошлина {t.ImportDuty(o, me):0%} · караванов {t.CaravansBetween(me, o)}{(specialties.Length > 0 ? $" · {specialties}" : "")}", CaptionInk);

                float bw = 128, bh = 30, x = row.xMax - bw * 4 - 12, y1 = row.y + 4, y2 = row.y + 38;
                // Соглашение / эмбарго.
                var ar = new Rect(x, y1, bw * 2 + 4, bh);
                if (t.HasAgreement(me, o))
                {
                    if (Button(ar, "Расторгнуть соглашение")) t.CancelAgreement(me, o, "решение правителя");
                    Tip(ar, "Караваны между вами будут конфискованы на границе");
                }
                else
                {
                    var reason = t.CanSignAgreement(me, o);
                    GUI.enabled = reason == null;
                    if (Button(ar, "Торговое соглашение")) _ai.AnswerHuman(me, o, ProposalKind.TradeAgreement);
                    GUI.enabled = true;
                    Tip(ar, reason ?? "Предложить открыть границы для караванов; ИИ согласен, если мнение не ниже −10");
                }
                var er = new Rect(x, y2, bw * 2 + 4, bh);
                bool mine = t.Embargoes(me, o);
                GUI.enabled = !State.AtWar(me, o) || mine;
                if (Button(er, mine ? "Снять эмбарго" : "Эмбарго")) t.SetEmbargo(me, o, !mine);
                GUI.enabled = true;
                Tip(er, mine ? "Снова пропускать их товары" : "Закрыть границу для их товаров: соглашение рвётся, мнение −15");

                // Прямые сделки.
                var br = new Rect(x + bw * 2 + 8, y1, bw * 2 + 4, bh);
                var buyReason = capital == null ? "нет столицы" : t.CanBuy(me, o, good) ?? (t.SellerAgrees(o, me, good) ? null : "не продают: им самим нужно");
                GUI.enabled = buyReason == null;
                if (Button(br, $"Купить за {t.DealBuyCost(o, good)}")) t.Buy(me, o, good);
                GUI.enabled = true;
                Tip(br, buyReason ?? $"Купить {TradeSystem.DealAmount} × {good.displayName} с их столичного склада");
                var sr = new Rect(x + bw * 2 + 8, y2, bw * 2 + 4, bh);
                var sellReason = capital == null ? "нет столицы" : t.CanSell(me, o, good) ?? (t.BuyerAgrees(o, me, good) ? null : "им не нужно: у них дёшево");
                GUI.enabled = sellReason == null;
                if (Button(sr, $"Продать за {t.DealSellGain(o, good)}")) t.Sell(me, o, good);
                GUI.enabled = true;
                Tip(sr, sellReason ?? $"Продать {TradeSystem.DealAmount} × {good.displayName} из своей столицы");

                y += rowH;
                Fill(new Rect(r.x + pad, y - 4, r.width - pad * 2, 1), new Color(GoldLine.r, GoldLine.g, GoldLine.b, 0.35f));
            }
        }

        /// <summary>Ближайший к нашим землям город стороны, на который можно заявить претензию.</summary>
        private City ClaimTarget(int me, int other)
        {
            var mine = Cities.Where(c => c.OwnerIndex == me).ToList();
            if (mine.Count == 0) return null;
            return Cities.Where(c => c.OwnerIndex == other && !State.Diplomacy.Claims.Any(x => x.Owner == me && x.CityId == c.Data.id))
                .OrderBy(c => mine.Min(m => m.Coord.DistanceTo(c.Coord)))
                .FirstOrDefault(c => mine.Min(m => m.Coord.DistanceTo(c.Coord)) <= Diplomacy.ClaimRange);
        }
    }
}

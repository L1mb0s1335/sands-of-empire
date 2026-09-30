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
            Label(new Rect(r.x + pad, y, r.width - pad * 2, 30), "Дипломатия", Heading);
            if (CloseButton(r)) _showDiplomacy = false;
            y += 36;
            string rules = d.WarsDisabled
                ? "Сценарий «Мирное развитие»: войны отключены, соперничество — в хозяйстве, торговле и знаниях."
                : $"Войну можно объявить только по созревшей претензии на город соперника (своя — {Diplomacy.ClaimCost} золота, зреет {Diplomacy.ClaimMaturity} х.). " +
                  (Turns.Turn <= Diplomacy.StartTruceTurns ? $"<b>Стартовое перемирие до хода {Diplomacy.StartTruceTurns}.</b> " : "") +
                  "Союзники вступают в оборонительную войну. Мир — перемирие на 10 ходов.";
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
                    gone ? $"<color={CMuted}>сошла со сцены</color>" : $"{other.Region.leaderName} · городов {cities} · сила {_ai.Strength(other.Index)}", CaptionInk);
                Tip(new Rect(row.x, row.y, 320, row.height), $"{other.Region.description}\nЛидер: {other.Region.leaderName} — {other.Region.leaderAbilityName}: {other.Region.leaderAbilityText}");

                if (!gone)
                {
                    Label(new Rect(row.x + 330, row.y + 2, 230, 24), OpinionText(rel.Value), Body);
                    Label(new Rect(row.x + 330, row.y + 26, 230, 20), StanceText(rel), BodySmall);

                    // Претензии: наши на их города и их на наши.
                    var ours = d.ClaimsOf(human.Index).Where(c => d.CityById(c.CityId).OwnerIndex == other.Index)
                        .Select(c => d.IsRipe(c) ? $"<color={CBad}>{d.CityById(c.CityId).Data.displayName}</color>"
                            : $"{d.CityById(c.CityId).Data.displayName} (ход {c.ReadyTurn})").ToList();
                    var theirs = d.ClaimsOf(other.Index).Where(c => d.CityById(c.CityId).OwnerIndex == human.Index)
                        .Select(c => d.CityById(c.CityId).Data.displayName).ToList();
                    var claims = (ours.Count > 0 ? $"Наши претензии: {string.Join(", ", ours)}" : "") +
                                 (theirs.Count > 0 ? $"{(ours.Count > 0 ? "\n" : "")}<color={CWarn}>Их претензии: {string.Join(", ", theirs)}</color>" : "");
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

using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;
using Runeterra.Map;
using Runeterra.Units;
using UnityEngine;
using static Runeterra.Core.HudSkin;

namespace Runeterra.Core
{
    /// <summary>
    /// HUD партии: верхняя полоса ресурсов, карточка хода и печать «Конец хода», столбик кнопок
    /// «Развитие / Казна / Юниты», медальон лидера, панель города или юнита, круглая мини-карта,
    /// окна на пергаменте и подсказки. Только отрисовка и вызовы существующих действий.
    /// Координаты — виртуальные (экран 1600×900), см. <see cref="HudSkin"/>.
    /// </summary>
    public partial class GameController
    {
        // Области HUD прошлого кадра: над ними клики не уходят в карту.
        private List<Rect> _hudRects = new List<Rect>(), _hudRectsNext = new List<Rect>();
        private string _tip;
        private Vector2 _cityScroll, _buildScroll;
        private int _cityTab;

        private bool MouseOverGui()
        {
            var p = InputMouse;
            foreach (var r in _hudRects)
                if (r.Contains(p)) return true;
            return false;
        }

        /// <summary>Отметить прямоугольник как часть HUD.</summary>
        private Rect Hud(Rect r)
        {
            if (Event.current.type == EventType.Repaint) _hudRectsNext.Add(r);
            return r;
        }

        private void Tip(Rect r, string text)
        {
            if (!string.IsNullOrEmpty(text) && r.Contains(Mouse)) _tip = text;
        }

        /// <summary>Кнопка «×» в правом верхнем углу окна.</summary>
        private bool CloseButton(Rect window)
        {
            var r = new Rect(window.xMax - 46, window.y + 12, 32, 32);
            Tip(r, "Закрыть");
            return Button(r, "×", BtnBig);
        }

        /// <summary>Закрыть верхнее открытое окно (порядок как при отрисовке). false — окон нет.</summary>
        private bool CloseTopWindow()
        {
            if (_showMenu && _menuConfirm != 0) _menuConfirm = 0;
            else if (_showMenu) _showMenu = false;
            else if (_showTech) _showTech = false;
            else if (_showUnits) _showUnits = false;
            else if (_showTreasury) _showTreasury = false;
            else if (_selectedCity != null && _cityTab != 0) _cityTab = 0;
            else return false;
            return true;
        }

        private static bool Toggle(Rect r, bool on, string text) => GUI.Toggle(R(r), on, text, Btn) != on;

        private void OnGUI()
        {
            if (State == null) return;
            Ensure();
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) _hudRectsNext.Clear();
            _tip = null;
            try
            {
                DrawHud();
            }
            finally
            {
                if (repaint) (_hudRects, _hudRectsNext) = (_hudRectsNext, _hudRects);
            }
        }

        private void DrawHud()
        {
            var human = Players.First(p => p.IsHuman);
            var player = Turns.Current;
            DrawTopBar(human);
            DrawTurnControls(player);

            if (Winner != null)
            {
                DrawGameOver();
                return;
            }

            DrawLeftColumn(human);
            DrawLeader(human);
            DrawMinimap();
            DrawMessage();

            Rect? panel = null;
            if (_selectedCity != null) panel = DrawCityPanel(human);
            else if (_selected != null) panel = DrawUnitPanel();
            else DrawControlsHint();
            if (_selectedCity != null && panel != null)
            {
                if (_cityTab == 1) DrawBuildWindow(human, _selectedCity, panel.Value);
                else if (_cityTab == 2) DrawStoreWindow(_selectedCity, panel.Value);
            }

            if (_showTreasury) DrawTreasury(human);
            if (_showUnits) DrawUnitList(human);
            if (_showTech) DrawTechTree(human);
            if (_showMenu) DrawMenu();
            DrawTooltip();
        }

        private int Taxes(PlayerState p) => p.LandIncome + p.PeopleIncome + p.LuxuryIncome + p.TariffIncomeLastTurn;

        private static string Signed(int v) => v > 0 ? $"+{v}" : v < 0 ? $"−{-v}" : "0";

        // ---------- Верхняя полоса ----------

        private void DrawTopBar(PlayerState human)
        {
            var bar = Hud(new Rect(0, 0, W, 68));
            Draw(bar, BarTex);
            Fill(new Rect(0, 68, W, 3), BronzeLine);
            Fill(new Rect(0, 71, W, 1), new Color(0f, 0f, 0f, 0.35f));
            Hud(new Rect(0, 68, W, 4));

            float x = 22;
            var name = human.Region.displayName;
            float nameW = Mathf.Min(TextWidth(name, Title), 360);
            Label(new Rect(x, 0, nameW + 4, 68), name, Title);
            x += nameW + 22;
            Fill(new Rect(x, 14, 1, 40), new Color(BronzeLine.r, BronzeLine.g, BronzeLine.b, 0.6f));
            x += 20;

            int income = State.IncomeOf(human);
            var research = human.Researching != null
                ? $"{human.Researching.displayName}: {human.ResearchProgress:0}/{State.Tech.Cost(human, human.Researching)}, ещё {State.Tech.TurnsLeft(human, human.Researching)} х."
                : "узел не выбран — откройте «Развитие» (Y)";
            int grain = State.Grain == null ? 0
                : Mathf.FloorToInt(Cities.Where(c => c.OwnerIndex == human.Index).Sum(c => c.Warehouse.Get(State.Grain)));

            x = Resource(x, "gold", Hex("f0c870"), $"{human.Gold} <color={(income >= 0 ? LGood : LBad)}>{Signed(income)}</color>",
                $"золото · армия −{State.Tech.ArmyUpkeep(human)}",
                $"Казна {human.Gold}, доход {Signed(income)} за ход, содержание армии −{State.Tech.ArmyUpkeep(human)}.\n" +
                $"Налоги {Signed(Taxes(human))} за ход · караванов в пути: {State.Trade.CountOf(human)}");
            x = Resource(x, "science", Hex("8fd8f0"), $"+{State.Tech.Science(human)}", "знания за ход", $"Знания +{State.Tech.Science(human)} за ход.\n{research}");
            x = Resource(x, "grain", Hex("e8cf7a"), $"{grain}", "зерно на складах", "Зерно на складах ваших городов. В засуху города проедают его.");
            x = Resource(x, "reserve", Hex("d9b07a"), $"{human.Reserve}", "резерв",
                "Резервная казна: закупает зерно в засуху и гасит пожары. Пополнение — «Казна» (T).");

            char rating = State.CreditRating(human);
            var rc = rating == 'A' ? LGood : rating == 'B' ? "#d8e89f" : rating == 'C' ? "#ffcf80" : LBad;
            var credit = $"Кредит <color={rc}>{rating}</color> · долг {human.Debt}";
            float cw = TextWidth(credit, CenterLight) + 30;
            var cr = new Rect(x, 19, cw, 30);
            Box(cr, PlaqueTex);
            Label(cr, credit, CenterLight);
            Tip(cr, $"Рейтинг {rating} — {GameState.RatingText(rating)}. Ставка {State.InterestRate(human):0%}/год, лимит займа {State.CreditLimit(human)}." +
                    (State.IsBankrupt(human) ? $"\nБанкрот ещё {human.BankruptUntil - Turns.Turn} х.: нет торговли" : ""));
        }

        private float Resource(float x, string icon, Color tint, string value, string caption, string tip)
        {
            Draw(new Rect(x, 12, 44, 44), Ring(44));
            Icon(new Rect(x + 11, 23, 22, 22), icon, tint);
            float w = Mathf.Max(TextWidth(value, Num), TextWidth(caption, Caption));
            Label(new Rect(x + 52, 9, w + 6, 28), value, Num);
            Label(new Rect(x + 52, 36, w + 6, 18), caption, Caption);
            Tip(new Rect(x, 8, 52 + w, 52), tip);
            return x + 52 + w + 24;
        }

        // ---------- Ход, печать, сохранение ----------

        private Rect SealRect() => new Rect(W - 16 - 112, 6, 112, 112);

        private void DrawTurnControls(PlayerState player)
        {
            var seal = SealRect();
            var card = Hud(new Rect(seal.x - 12 - 196, 7, 196, 62));
            Box(card, CardTex);
            Label(new Rect(card.x, card.y + 5, card.width, 28), $"Ход {Turns.Turn}", TurnTitle);
            Label(new Rect(card.x, card.y + 33, card.width, 20), $"Год {State.Year} · {Seasons.Name(State.Season)}", Center);
            Tip(card, $"{Seasons.Name(State.Season)}: {Seasons.Hint(State.Season)}" + (player.IsHuman ? "" : $"\nСейчас ходит: {player.Region.displayName}"));
            if (Winner != null) return;

            float bw = (card.width - 6) / 2f;
            GUI.enabled = CanSave;
            if (Button(Hud(new Rect(card.x, card.yMax + 7, bw, 26)), "Сохранить")) SaveGame();
            GUI.enabled = SaveSystem.HasSave && !Busy;
            if (Button(Hud(new Rect(card.x + bw + 6, card.yMax + 7, bw, 26)), "Загрузить")) LoadGame();
            GUI.enabled = true;
            var menuRect = Hud(new Rect(card.x, card.yMax + 39, card.width, 26));
            if (Button(menuRect, "Меню")) _showMenu = !_showMenu;
            Tip(menuRect, "Продолжить, выйти в главное меню или из игры (Esc)");

            Hud(seal);
            bool ready = !player.Units.Any(u => u.MovesLeft > 0);
            if (ready)
            {
                var glow = Glow(160);
                float pulse = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
                Draw(new Rect(seal.center.x - 80, seal.center.y - 80, 160, 160), glow, new Color(1f, 1f, 1f, pulse));
            }
            Draw(seal, Seal(112, seal.Contains(Mouse)));
            if (GUI.Button(R(seal), GUIContent.none, Plain)) EndTurn();
            var plate = Hud(new Rect(seal.center.x - 64, seal.yMax + 2, 128, 26));
            Box(plate, PlaqueTex);
            Label(plate, ready ? $"<color={LGold}>Конец хода »</color>" : "Конец хода", CenterLight);
            Tip(seal, ready ? "Все юниты сходили. Пробел / Enter — конец хода" : "Есть юниты с ходами (Tab — следующий). Пробел / Enter — конец хода");
        }

        // ---------- Левый столбик ----------

        private void DrawLeftColumn(PlayerState human)
        {
            var t = human.Researching;
            string research = t != null
                ? $"{t.displayName} · {human.ResearchProgress:0}/{State.Tech.Cost(human, t)} · ещё {State.Tech.TurnsLeft(human, t)} х."
                : $"<color={LBad}>узел не выбран</color>";
            float progress = t != null ? Mathf.Clamp01(human.ResearchProgress / Mathf.Max(1, State.Tech.Cost(human, t))) : 0f;
            if (SideButton(new Vector2(62, 134), "tech", "Развитие (Y)", research, _showTech, progress)) _showTech = !_showTech;
            if (SideButton(new Vector2(62, 232), "treasury", "Казна (T)", $"<color={LGold}>{Signed(Taxes(human))}/ход</color> · резерв {human.Reserve}", _showTreasury, -1f))
                _showTreasury = !_showTreasury;
            int ready = human.Units.Count(u => u.MovesLeft > 0);
            if (SideButton(new Vector2(62, 322), "units", "Юниты (U)", $"{human.Units.Count} · ходят {ready}", _showUnits, -1f)) _showUnits = !_showUnits;
        }

        /// <summary>Шестиугольная бронзовая кнопка с табличкой. progress ≥ 0 — кольцо прогресса.</summary>
        private bool SideButton(Vector2 c, string icon, string title, string caption, bool on, float progress)
        {
            const float size = 68;
            var r = Hud(new Rect(c.x - size / 2, c.y - size / 2, size, size));
            float tw = Mathf.Max(TextWidth(title, TextLight), TextWidth(caption, TextLightSmall)) + 28;
            var plate = Hud(new Rect(c.x + 46, c.y - 24, tw, 48));
            Box(plate, PlaqueTex);
            Label(new Rect(plate.x + 14, plate.y + 4, tw, 22), $"<b>{title}</b>", TextLight);
            Label(new Rect(plate.x + 14, plate.y + 25, tw, 20), caption, TextLightSmall);
            if (progress >= 0f) Draw(new Rect(c.x - 45, c.y - 45, 90, 90), ProgressRing(90, progress));
            bool hover = r.Contains(Mouse) || plate.Contains(Mouse);
            Draw(r, HexButton(size, hover, on));
            Icon(new Rect(c.x - 17, c.y - 17, 34, 34), icon, Hex("f0d9a0"));
            bool a = GUI.Button(R(r), GUIContent.none, Plain);
            bool b = GUI.Button(R(plate), GUIContent.none, Plain);
            return a || b;
        }

        // ---------- Лидер ----------

        private void DrawLeader(PlayerState human)
        {
            var region = human.Region;
            var med = Hud(new Rect(16, H - 16 - 138, 138, 138));
            var plate = Hud(new Rect(med.xMax - 16, med.y + 22, 290, 100));
            Box(plate, PlaqueTex);
            Draw(med, Ring(138, Color.Lerp(region.primaryColor, Color.black, 0.45f)));
            var who = string.IsNullOrEmpty(region.leaderName) ? region.displayName : region.leaderName;
            Label(new Rect(med.x, med.y - 2, med.width, med.height), who.Length > 0 ? who.Substring(0, 1).ToUpper() : "", Monogram);

            float x = plate.x + 30, w = plate.width - 42;
            Label(new Rect(x, plate.y + 6, w, 26), who, HeadingLight);
            var ability = region.leaderAbility != LeaderAbility.None
                ? $"<color={LGold}>{region.leaderAbilityName}</color>: {region.leaderAbilityText}"
                : region.displayName;
            Label(new Rect(x, plate.y + 32, w, 38), ability, TextLightSmall);
            var epoch = TechSystem.AllEpochs.LastOrDefault(e => human.Epochs.Contains(e));
            Label(new Rect(x, plate.y + 72, w, 20), epoch != null ? TechSystem.EpochName(epoch) : "Эпоха ещё не наступила", Caption);
            Tip(plate, string.Join("\n", TechSystem.AllEpochs.Select(e => human.Epochs.Contains(e)
                ? $"{TechSystem.EpochName(e)} — наступила ({TechSystem.EpochEffect(e)})"
                : $"{TechSystem.EpochName(e)}: нужно {string.Join(", ", State.Tech.EpochMissing(human, e))}")));
        }

        // ---------- Мини-карта ----------

        private void FocusOn(HexCoord c)
        {
            var cam = map.mapCamera != null ? map.mapCamera.GetComponent<HexCameraController>() : null;
            if (cam != null) cam.FocusOn(map.SurfacePosition(c));
        }

        private void DrawMinimap()
        {
            if (_minimap == null) return;
            var outer = Hud(new Rect(W - 16 - 230, H - 16 - 230, 230, 230));
            var inner = new Rect(outer.x + 13, outer.y + 13, outer.width - 26, outer.height - 26);
            Draw(inner, _minimap.Texture);
            Draw(outer, BrassRing(230, (115f - 13f) / 115f));
            Tip(inner, "Мини-карта: клик переносит камеру");
            var e = Event.current;
            var m = Mouse;
            if (e.type == EventType.MouseDown && e.button == 0 && (m - inner.center).magnitude <= inner.width / 2f)
            {
                var uv = new Vector2((m.x - inner.x) / inner.width, (m.y - inner.y) / inner.height);
                var cam = map.mapCamera != null ? map.mapCamera.GetComponent<HexCameraController>() : null;
                if (cam != null) cam.FocusOn(_minimap.ToWorld(uv));
                e.Use();
            }
        }

        // ---------- Сообщение и подсказка управления ----------

        private void DrawMessage()
        {
            if (string.IsNullOrEmpty(_lastMessage)) return;
            float w = Mathf.Min(TextWidth(_lastMessage, CenterLight) + 44, W - 760);
            var r = Hud(new Rect((W - w) / 2f, 82, w, 30));
            Box(r, PlaqueTex);
            Label(r, _lastMessage, CenterLight);
        }

        private void DrawControlsHint()
        {
            float left = 16 + 138 + 290, right = W - 16 - 230 - 12;
            ShadowLabel(new Rect(left, H - 58, right - left, 48),
                "ЛКМ — юнит / город · ПКМ — идти / атаковать · B рынок · P порт · R дорога · F город · Tab — следующий юнит · " +
                "Пробел — конец хода · WASD / колесо / Q / E — камера", Hint);
        }

        // ---------- Нижняя панель ----------

        private Rect BottomPanel(float h)
        {
            float left = 16 + 138 + 290 + 10, right = W - 16 - 230 - 14;
            float w = Mathf.Min(720, right - left);
            float x = Mathf.Clamp(W / 2f - w / 2f, left, right - w);
            return new Rect(x, H - 10 - h, w, h);
        }

        /// <summary>Карточка показателя: иконка, число, подпись и строка пояснения.</summary>
        private void StatCard(Rect r, string icon, string value, string title, string detail, string tip = null)
        {
            Box(r, TileTex);
            Icon(new Rect(r.x + 10, r.y + 10, 22, 22), icon, Hex("6e4a1f"));
            Label(new Rect(r.x + 38, r.y + 6, r.width - 42, 28), value, NumInk);
            Label(new Rect(r.x + 10, r.y + 35, r.width - 16, 18), $"<b>{title}</b>", CaptionInk);
            Label(new Rect(r.x + 10, r.y + 53, r.width - 14, 20), detail, CaptionInk);
            Tip(r, tip);
        }

        private Rect DrawCityPanel(PlayerState human)
        {
            var city = _selectedCity;
            var p = Hud(BottomPanel(340));
            Box(p, PanelTex);
            float pad = 28, x = p.x + pad, w = p.width - pad * 2, y = p.y + 22;

            Label(new Rect(x, y, w * 0.62f, 34), city.Data.displayName + (string.IsNullOrEmpty(city.Data.altName) ? "" : $" <size={Sz(15)}>({city.Data.altName})</size>"), Heading);
            Label(new Rect(x + w * 0.5f, y, w * 0.5f, 34), $"<b>{SettlementTiers.Name(city.Tier)}</b>{(city.IsCapital ? " · столица" : "")} · население {city.Population}", InkRight);
            var tier = city.Tier < SettlementTier.TradeCapital
                ? $"Ступень {SettlementTiers.Name(city.Tier)} → {SettlementTiers.Name(city.Tier + 1)}: нужно {string.Join(", ", State.MissingForTier(city, city.Tier + 1))}"
                : "Высшая ступень поселения";
            var tierRect = new Rect(x, y + 34, w, 20);
            Label(tierRect, tier, new GUIStyle(CaptionInk) { wordWrap = false });
            Tip(tierRect, tier);

            // Пять карточек.
            float cy = y + 60, gap = 8, cw = (w - gap * 4) / 5f, ch = 78;
            float use = State.WaterUse(city);
            int cap = State.WaterCapacity(city);
            StatCard(new Rect(x, cy, cw, ch), "people", $"{city.Population}", "жители", $"мастеров {city.Employed} · своб. {city.FreeWorkers}",
                $"Жители {city.Population}: мастеров {city.Employed}, свободных {city.FreeWorkers}, на земле 1");
            StatCard(new Rect(x + (cw + gap), cy, cw, ch), "water", $"{use:0.#}/{cap}", "вода",
                use > cap ? $"<color={CBad}>нехватка {use - cap:0.#}</color>" : $"<color={CGood}>хватает</color>",
                use > cap ? $"Нехватка воды {use - cap:0.#}: еда −{Mathf.CeilToInt(use - cap)}, мастерские ×{State.WaterFactor(city):0.00}" : "Воды хватает");
            int grow = State.TurnsToGrow(city);
            StatCard(new Rect(x + (cw + gap) * 2, cy, cw, ch), "food", Signed(State.CityFood(city)), "еда",
                grow > 0 ? $"{city.FoodStock}/{State.GrowthThreshold(city)} · рост {grow} х." : $"<color={CBad}>рост остановлен</color>",
                $"Запас еды {city.FoodStock}/{State.GrowthThreshold(city)}" + (grow > 0 ? $", рост через {grow} х." : ", рост остановлен"));
            StatCard(new Rect(x + (cw + gap) * 3, cy, cw, ch), "prod", $"+{State.CityProduction(city)}", "производство", $"золото +{State.CityGold(city)}");
            bool blockade = State.IsBlockaded(city);
            StatCard(new Rect(x + (cw + gap) * 4, cy, cw, ch), "walls", $"{city.Walls}/{City.MaxWalls}", "стены",
                blockade ? $"<color={CBad}>блокада</color>" : $"сила {State.CityStrength(city)}",
                blockade ? "Блокада: враг рядом, стены не чинятся" : $"Сила города {State.CityStrength(city)}");

            // Текущая постройка.
            var bar = new Rect(x, cy + ch + 10, w, 30);
            Box(bar, TileTex);
            var build = city.CurrentBuild;
            if (build != null)
            {
                float f = Mathf.Clamp01((float)city.ProductionStock / Mathf.Max(1, build.Cost));
                Fill(new Rect(bar.x + 3, bar.y + 3, (bar.width - 6) * f, bar.height - 6), new Color(ProgressColor.r, ProgressColor.g, ProgressColor.b, 0.55f));
                Label(new Rect(bar.x + 12, bar.y, bar.width * 0.6f, bar.height), $"<b>Строится:</b> {build.Name}", InkMid);
                Label(new Rect(bar.x + bar.width * 0.4f, bar.y, bar.width * 0.6f - 12, bar.height),
                    $"{city.ProductionStock}/{build.Cost} · ещё {State.TurnsToBuild(city, build)} х.", InkRight);
            }
            else Label(new Rect(bar.x + 12, bar.y, bar.width - 24, bar.height), $"<color={CBad}>Ничего не строится — кнопка «Строить»</color>", InkMid);

            // Подробности (прокрутка).
            float dy = bar.yMax + 8, by = p.yMax - 22 - 34;
            var area = new Rect(x, dy, w, by - 8 - dy);
            var detail = CityDetails(city);
            float innerW = w - 18;
            float textH = BodySmall.CalcHeight(new GUIContent(detail), Sz(innerW)) / S;
            _cityScroll = GUI.BeginScrollView(R(area), _cityScroll, R(new Rect(0, 0, innerW, Mathf.Max(textH, area.height - 2))));
            GUI.Label(R(new Rect(0, 0, innerW, textH)), detail, BodySmall);
            GUI.EndScrollView();

            // Кнопки.
            float bw = (w - gap * 3) / 4f;
            if (Toggle(new Rect(x, by, bw, 34), _cityTab == 1, "Строить")) _cityTab = _cityTab == 1 ? 0 : 1;
            if (Toggle(new Rect(x + bw + gap, by, bw, 34), _cityTab == 2, "Склад")) _cityTab = _cityTab == 2 ? 0 : 2;
            var rushRect = new Rect(x + (bw + gap) * 2, by, bw, 34);
            bool rushable = build != null && build.Unit == null;
            var rush = rushable ? State.CanRush(city, human) : build == null ? "ничего не строится" : "юнита не ускорить — его можно купить в «Строить»";
            GUI.enabled = rushable && rush == null;
            if (Button(rushRect, rushable ? $"Ускорить · {State.RushCost(city)} зол." : "Ускорить")) State.Rush(city, human);
            GUI.enabled = true;
            Tip(rushRect, rush == null ? $"Ускорить «{build.Name}» за {State.RushCost(city)} золота" : $"Ускорить: {rush}" + (rushable ? $" ({State.RushCost(city)} зол.)" : ""));
            var draftRect = new Rect(x + (bw + gap) * 3, by, bw, 34);
            var draft = State.CanDraft(city, human);
            GUI.enabled = draft == null;
            if (Button(draftRect, "Ополчение")) State.Draft(city, human);
            GUI.enabled = true;
            Tip(draftRect, draft == null ? "Призвать ополчение: Воин даром, −1 житель" : $"Призыв: {draft}");
            return p;
        }

        private string CityDetails(City city)
        {
            var t = $"<b>Районы:</b> {(city.HasMarket ? "рынок " : "")}{(city.HasPort ? "порт" : "")}{(!city.HasMarket && !city.HasPort ? "нет" : "")}";
            var other = city.Buildings.Where(kv => !kv.Key.IsWorkshop).Select(kv => kv.Key.displayName).ToList();
            t += $"   <b>Постройки:</b> {(other.Count > 0 ? string.Join(", ", other) : "нет")}";
            var risks = new List<string>();
            if (State.FireChance(city) > 0) risks.Add($"пожар {State.FireChance(city):0.#%}/ход");
            if (State.CrimeRate(city) > 0) risks.Add($"кражи −{State.CrimeLoss(city)} зол.");
            if (State.EpidemicChance(city) > 0) risks.Add($"эпидемия {State.EpidemicChance(city):0.#%}/ход");
            if (risks.Count > 0) t += $"\n<color={CWarn}>Беды: {string.Join(", ", risks)}</color>";
            if (State.IsBlockaded(city)) t += $"\n<color={CBad}>Блокада: враг рядом, стены не чинятся</color>";
            var shops = city.Buildings.Where(kv => kv.Key.IsWorkshop).ToList();
            t += shops.Count == 0 ? "\n<b>Мастерские:</b> нет — сырьё можно перерабатывать в дорогие товары" : "\n<b>Мастерские</b>";
            foreach (var kv in shops)
            {
                float mult = State.ClusterMultiplier(city, kv.Key), batches = State.WorkshopBatches(city, kv.Key);
                var masters = city.MastersOf(kv.Key);
                float skill = State.WorkshopSkill(city, kv.Key);
                t += $"\n{kv.Key.displayName} ×{kv.Value}: {kv.Key.RecipeText()}" +
                     (mult > 1f ? $" <color={CGood}>кластер +{mult - 1f:0%}</color>" : "") +
                     (batches > 0 ? $" ({batches:0}×/ход)" : masters.Count == 0 ? $" <color={CBad}>нет мастеров</color>" : $" <color={CBad}>нет сырья</color>") +
                     $" · мастера {masters.Count}/{kv.Value}" +
                     (masters.Count > 0 ? $", стаж ~{masters.Average():0} х., мастерство {skill - 1f:+0%;-0%;+0%}" : "");
            }
            var (mono, share) = State.Monoculture(city);
            if (mono != null && share >= GameState.MonocultureShare)
                t += $"\n<color={CWarn}>Монокультура: {mono.displayName} {share:0%} — риск болезни урожая</color>";
            foreach (var b in city.Blights) t += $"\n<color={CBad}>Болезнь урожая: {b.Key.displayName} ×0.5 ещё {b.Value} х.</color>";
            return t;
        }

        private List<BuildItem> CityBuildItems()
        {
            var items = shop.Select(u => new BuildItem(u)).ToList();
            if (market != null) items.Add(new BuildItem(market));
            if (port != null) items.Add(new BuildItem(port));
            items.AddRange(buildings.Select(b => new BuildItem(b)));
            return items;
        }

        /// <summary>Окно над панелью города; высота ограничена местом под верхней полосой.</summary>
        private Rect AbovePanel(Rect panel, float wanted) =>
            new Rect(panel.x, panel.y - 10 - Mathf.Min(wanted, panel.y - 10 - 132), panel.width, Mathf.Min(wanted, panel.y - 10 - 132));

        private void DrawBuildWindow(PlayerState human, City city, Rect panel)
        {
            var items = CityBuildItems();
            const float rowH = 34, head = 64;
            var r = Hud(AbovePanel(panel, head + rowH * items.Count + 16));
            Window(r);
            float pad = 20;
            Label(new Rect(r.x + pad, r.y + 10, r.width - pad * 2, 30), $"Строительство — {city.Data.displayName}", Heading);
            if (CloseButton(r)) _cityTab = 0;
            Label(new Rect(r.x + pad, r.y + 40, r.width - pad * 2, 18), $"Производство +{State.CityProduction(city)} · казна {human.Gold} · справа — покупка за золото", CaptionInk);
            var area = new Rect(r.x + pad, r.y + head, r.width - pad * 2, r.height - head - 12);
            float innerW = area.width - 18, colW = (innerW - 8) * 0.62f, buyW = innerW - 8 - colW;
            _buildScroll = GUI.BeginScrollView(R(area), _buildScroll, R(new Rect(0, 0, innerW, rowH * items.Count)));
            float y = 0;
            foreach (var item in items)
            {
                bool canBuild = State.CanBuild(city, item, out var reason);
                GUI.enabled = canBuild;
                var count = item.Building != null && city.Count(item.Building) > 0 ? $" ×{city.Count(item.Building)}" : "";
                var label = (item.Is(city.CurrentBuild) ? $"<color={CGold}>» </color>" : "") + $"<b>{item.Name}</b>{count} · {State.TurnsToBuild(city, item)} х." +
                            (canBuild ? "" : $" <size={Sz(11)}>({reason})</size>");
                if (GUI.Button(R(new Rect(0, y, colW, rowH - 4)), label, Row)) State.SetBuild(city, item);
                if (item.Unit != null)
                {
                    bool ok = State.CanBuy(human, city, item.Unit, out var why);
                    GUI.enabled = ok;
                    var buy = $"Купить · {State.BuyCost(human, item.Unit)} зол." + (ok || why == "не хватает золота" ? "" : $" <size={Sz(10)}>({why})</size>");
                    if (GUI.Button(R(new Rect(colW + 8, y, buyW, rowH - 4)), buy, Btn)) State.Buy(human, city, item.Unit);
                }
                else if (item.Building != null && item.Building.IsWorkshop)
                {
                    GUI.enabled = true;
                    GUI.Label(R(new Rect(colW + 8, y, buyW, rowH - 4)), $"<size={Sz(11)}>{item.Building.RecipeText()}</size>", CaptionInk);
                }
                GUI.enabled = true;
                y += rowH;
            }
            GUI.EndScrollView();
        }

        private void DrawStoreWindow(City city, Rect panel)
        {
            const float rowH = 22, head = 92;
            bool drought = State.Season == Season.Drought || State.Season == Season.Harvest;
            var r = Hud(AbovePanel(panel, head + rowH * goods.Count + (drought ? 34 : 14)));
            Window(r);
            float pad = 22, x = r.x + pad, w = r.width - pad * 2;
            Label(new Rect(x, r.y + 10, w, 30), $"Склад — {city.Data.displayName}", Heading);
            if (CloseButton(r)) _cityTab = 0;
            Label(new Rect(x, r.y + 40, w, 18), $"Вместимость {city.Warehouse.Capacity}: запас, приход за ход, цена, порча", CaptionInk);
            float[] cols = { 0f, 0.38f, 0.55f, 0.72f, 0.88f };
            string[] heads = { "Товар", "Запас", "Приход", "Цена", "Порча" };
            for (int i = 0; i < heads.Length; i++) Label(new Rect(x + w * cols[i], r.y + 64, w * 0.2f, 20), $"<b>{heads[i]}</b>", InkMid);
            Fill(new Rect(x, r.y + head - 6, w, 1), GoldLine);
            float y = r.y + head;
            foreach (var g in goods)
            {
                if (y + rowH > r.yMax - 8) break;
                float output = State.GoodOutput(city, g, State.Season);
                Label(new Rect(x + w * cols[0], y, w * 0.38f, rowH), g.displayName, InkMid);
                Label(new Rect(x + w * cols[1], y, w * 0.17f, rowH), $"{city.Warehouse.Get(g):0}", InkMid);
                Label(new Rect(x + w * cols[2], y, w * 0.17f, rowH), output > 0 ? $"<color={CGood}>+{output:0.#}</color>" : "—", InkMid);
                Label(new Rect(x + w * cols[3], y, w * 0.17f, rowH), $"<color={CGold}>{State.Trade.Price(city, g):0.0}</color>", InkMid);
                Label(new Rect(x + w * cols[4], y, w * 0.12f, rowH), g.spoilagePerTurn > 0 ? $"−{g.spoilagePerTurn * city.Warehouse.SpoilageMultiplier:0%}" : "—", InkMid);
                y += rowH;
            }
            if (drought) Label(new Rect(x, r.yMax - 30, w, 22), $"<color={CWarn}>В засуху город съест {State.DroughtGrainNeed(city)} зерна</color>", InkMid);
        }

        // ---------- Панель юнита ----------

        private Rect DrawUnitPanel()
        {
            var u = _selected;
            bool builder = u.Data.buildCharges > 0, settler = u.Data.canFoundCity;
            var p = Hud(BottomPanel(builder || settler ? 250 : 206));
            Box(p, PanelTex);
            float pad = 28, x = p.x + pad, w = p.width - pad * 2, y = p.y + 22;

            Label(new Rect(x, y, w * 0.6f, 34), u.Data.displayName, Heading);
            if (u.Data.strength > 0) Label(new Rect(x + w * 0.5f, y, w * 0.5f, 34), $"<b>{u.LevelName}</b>", InkRight);
            string sub;
            if (_previewPath != null) sub = $"Путь: {_previewPath.Count} кл., ходов: {_previewTurns}";
            else if (_hovered != null && _hovered.Value != u.Coord && !VisibleTarget(u, _hovered.Value)) sub = $"<color={CBad}>Пути нет</color>";
            else sub = "ПКМ — идти / атаковать · Tab — следующий юнит · Esc — снять выбор";
            Label(new Rect(x, y + 34, w, 20), sub, CaptionInk);

            var cards = new List<(string icon, string value, string title, string detail, string tip)>
            {
                ("move", $"{u.MovesLeft}/{u.Data.movement}", "движение", u.MovesLeft > 0 ? "может ходить" : $"<color={CMuted}>сходил</color>", null),
                ("hp", $"{u.Health}/{u.Data.maxHealth}", "здоровье",
                    u.Health < u.Data.maxHealth ? $"<color={CGood}>+{State.HealAmount(u)} за отдых</color>" : "полное",
                    u.Health < u.Data.maxHealth ? $"Без действий вылечится на +{State.HealAmount(u)}" : null),
            };
            var atk = u.AttackBonus > 0 ? $"в атаке +{u.AttackBonus}" : "";
            if (u.Data.IsRanged)
                cards.Add(("sword", $"{u.RangedStrength}", "выстрел", $"дальн. {u.Data.range} · вблизи {u.MeleeStrength}",
                    $"Выстрел {u.RangedStrength}{(atk != "" ? $" ({atk})" : "")}, дальность {u.Data.range}, вблизи {u.MeleeStrength}"));
            else if (u.Data.strength > 0) cards.Add(("sword", $"{u.MeleeStrength}", "сила", atk, null));
            if (u.Data.strength > 0)
            {
                int next = u.Level < Unit.LevelThresholds.Length ? Unit.LevelThresholds[u.Level] : 0;
                cards.Add(("xp", next > 0 ? $"{u.Experience}/{next}" : $"{u.Experience}", "опыт", u.LevelName, null));
            }
            if (builder) cards.Add(("build", $"{u.BuildCharges}", "постройки", "осталось", null));

            float gap = 8, cw = Mathf.Min(150, (w - gap * 4) / 5f), cy = y + 60;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                StatCard(new Rect(x + (cw + gap) * i, cy, cw, 78), c.icon, c.value, c.title, c.detail, c.tip);
            }

            float by = cy + 78 + 12;
            if (builder)
            {
                float bw = (w - gap * 2) / 3f;
                int i = 0;
                foreach (var d in new[] { market, port })
                {
                    if (d == null) continue;
                    var city = State.DistrictCityFor(_selected, d, out var reason);
                    var key = d == market ? "B" : "P";
                    var br = new Rect(x + (bw + gap) * i, by, bw, 40);
                    GUI.enabled = city != null;
                    if (Button(br, city != null ? $"{d.displayName} ({key})" : $"{d.displayName}\n<size={Sz(11)}>{reason}</size>")) TryBuildDistrict(d);
                    GUI.enabled = true;
                    Tip(br, city != null ? $"Построить: {d.displayName} у города {city.Data.displayName}" : reason);
                    i++;
                }
                var road = State.CanBuildRoad(_selected);
                var rr = new Rect(x + (bw + gap) * i, by, bw, 40);
                GUI.enabled = road == null;
                if (Button(rr, road == null ? "Дорога (R)" : $"Дорога\n<size={Sz(11)}>{road}</size>") && State.BuildRoad(_selected)) AfterAction();
                GUI.enabled = true;
                Tip(rr, road);
            }
            else if (settler)
            {
                var reason = _selected.MovesLeft <= 0 ? "нет очков движения" : State.CanFoundCity(_selected, _selected.Coord);
                var fr = new Rect(x, by, w, 40);
                GUI.enabled = reason == null;
                if (Button(fr, reason == null ? "Основать город (F)" : $"Город: {reason}")) TryFoundCity();
                GUI.enabled = true;
            }
            return p;
        }

        // ---------- Подсказки ----------

        private void DrawTooltip()
        {
            var text = _tip;
            if (text == null && _hovered != null && !MouseOverGui() && !_hudRects.Any(r => r.Contains(Mouse))) text = HoverText(_hovered.Value).Trim('\n');
            if (string.IsNullOrEmpty(text)) return;
            float w = Mathf.Min(340, TextWidth(text.Split('\n').OrderByDescending(l => l.Length).First(), Body) + 36);
            w = Mathf.Max(w, 160);
            float h = Body.CalcHeight(new GUIContent(text), Sz(w - 32)) / S + 24;
            var m = Mouse;
            float x = Mathf.Min(m.x + 20, W - w - 8), y = m.y + 22;
            if (y + h > H - 8) y = m.y - h - 10;
            var r = new Rect(x, Mathf.Max(8, y), w, h);
            Window(r);
            Label(new Rect(r.x + 16, r.y + 12, w - 32, h - 20), text, Body);
        }

        private string HoverText(HexCoord c)
        {
            if (!map.Grid.TryGetTile(c, out var tile)) return "";
            int fog = FogState(c);
            if (fog == 0) return "\nНеизведанная земля";
            var text = $"\n<b>{TerrainName(tile.Terrain)}</b>";
            if (tile.Feature != TileFeature.None) text += $", {FeatureName(tile.Feature)}";
            int cost = tile.MoveCost();
            text += cost == TerrainRules.Impassable ? " — непроходимо" : $" — стоимость хода {cost}";
            var owner = State.OwnerOfTile(c);
            if (owner != null) text += $"\nТерритория: {Players[owner.Value].Region.displayName}";
            var city = State.CityAt(c);
            if (city != null)
            {
                text += $"\nГород: <b>{city.Data.displayName}</b>";
                if (!string.IsNullOrEmpty(city.Data.altName)) text += $" ({city.Data.altName})";
                if (city.IsCapital) text += " — столица";
                text += $", население {city.Population}, +{State.CityGold(city)} золота";
                text += $"\nСтены: {city.Walls}/{City.MaxWalls}, сила {State.CityStrength(city)}";
            }
            int defense = State.DefenseBonus(c);
            if (defense > 0) text += $"\nЗащита местности: +{defense}";
            if (Cities.Any(ci => ci.MarketCoord == c)) text += $"\n{market.displayName} (+{market.goldPerTurn} золота)";
            if (port != null && Cities.Any(ci => ci.PortCoord == c)) text += $"\n{port.displayName}: морские пути и заморская торговля";
            if (tile.HasRoad) text += "\nДорога: ход 1, караваны быстрее и безопаснее";
            var caravansHere = State.Trade.Caravans.Where(cv => cv.Coord == c && (fog == 2 || cv.OwnerIndex == _human)).ToList();
            foreach (var cv in caravansHere) text += $"\nКараван: {cv.Amount:0} × {cv.Good.displayName} → {cv.DestinationName}";
            var unit = fog == 2 ? State.UnitAt(c) : null;
            if (fog == 1) text += $"\n<color={CMuted}>Вне обзора — юнитов не видно</color>";
            if (unit != null && unit != _selected)
            {
                text += $"\nЮнит: {unit.Data.displayName} ({Players[unit.OwnerIndex].Region.displayName}), HP {unit.Health}/{unit.Data.maxHealth}, сила {unit.MeleeStrength}";
                if (unit.Level > 0) text += $", {unit.LevelName}";
            }
            if (_selected != null && GameState.CanFight(_selected) && VisibleTarget(_selected, c))
            {
                var (dealt, taken, what) = State.PreviewAttack(_selected, c);
                text += _selected.Data.IsRanged
                    ? $"\n<color={CBad}>Выстрел по цели «{what}»: урон {dealt}, без ответа</color>"
                    : $"\n<color={CBad}>Атака на «{what}»: урон {dealt}, ответ {taken}</color>";
            }
            return text;
        }

        // ---------- Казна ----------

        private bool _showTreasury;

        private Rect TreasuryRect() => new Rect(410, 88, 580, 486);

        private void TaxRow(ref float y, Rect r, string name, float rate, System.Action<float> set, int income, string downside)
        {
            float pad = 22;
            Label(new Rect(r.x + pad, y, 360, 26), $"<b>{name}</b> {rate:0%}  <color={CGold}>+{income}/ход</color>", InkMid);
            if (Button(new Rect(r.xMax - pad - 88, y, 40, 26), "−")) set(Mathf.Max(0f, rate - 0.05f));
            if (Button(new Rect(r.xMax - pad - 42, y, 40, 26), "+")) set(Mathf.Min(0.3f, rate + 0.05f));
            y += 26;
            Label(new Rect(r.x + pad, y, r.width - pad * 2, 36), downside, CaptionInk);
            y += 36;
        }

        private void DrawTreasury(PlayerState human)
        {
            var r = Hud(TreasuryRect());
            Window(r);
            float pad = 22, y = r.y + 12;
            Label(new Rect(r.x + pad, y, r.width - pad * 2, 30), "Казна и налоги", Heading);
            if (CloseButton(r)) _showTreasury = false;
            y += 38;
            TaxRow(ref y, r, "Налог на землю", human.LandTax, v => human.LandTax = v, human.LandIncome,
                human.LandTax >= GameState.HeavyTax
                    ? $"<color={CBad}>Тяжёлый: при неурожае крестьяне бегут (−1 житель, 35%)</color>"
                    : "Ставка × половина стоимости добытого сырья. От 15% при неурожае крестьяне бегут");
            TaxRow(ref y, r, "Подушный налог", human.PeopleTax, v => human.PeopleTax = v, human.PeopleIncome,
                $"2 зол. × ставка с каждого жителя. Рост городов медленнее: порог ×{1f + 2f * human.PeopleTax:0.0#}");
            TaxRow(ref y, r, "Налог на роскошь", human.LuxuryTax, v => human.LuxuryTax = v, human.LuxuryIncome,
                human.LuxuryTax >= GameState.HeavyTax
                    ? $"<color={CBad}>Знать недовольна: производство Городков и крупнее −{human.LuxuryTax:0%}</color>"
                    : "С потреблённых ткани и сластей. От 15% злит знать (−производство)");
            float smuggle = GameState.SmugglingShare(human.Tariff, false);
            TaxRow(ref y, r, "Торговая пошлина", human.Tariff, v => human.Tariff = v, human.TariffIncomeLastTurn,
                $"С каждой доставленной партии. Контрабанда: {smuggle:0%} пошлины (−{human.SmuggledLastTurn} за ход). " +
                "Лечится снижением пошлины (легализация) или стражей в городе (−20%)");

            Fill(new Rect(r.x + pad, y + 2, r.width - pad * 2, 1), GoldLine);
            y += 8;
            Label(new Rect(r.x + pad, y, 300, 28), $"<b>Резервная казна</b> {human.Reserve}", InkMid);
            GUI.enabled = human.Gold >= 10;
            if (Button(new Rect(r.xMax - pad - 132, y, 62, 28), "+10")) { human.Gold -= 10; human.Reserve += 10; }
            GUI.enabled = human.Reserve >= 10;
            if (Button(new Rect(r.xMax - pad - 64, y, 62, 28), "−10")) { human.Gold += 10; human.Reserve -= 10; }
            GUI.enabled = true;
            y += 34;

            // Монета и долги.
            float bw = 150;
            Label(new Rect(r.x + pad, y, r.width - pad * 2 - bw * 2, 42),
                $"<b>Монета</b>: порча {human.Debasement:0%}, инфляция {State.Inflation(human):0%}\n" +
                $"<size={Sz(12)}>Купцов и пошлин −{human.Debasement:0%}</size>", Body);
            GUI.enabled = human.Debasement < GameState.MaxDebasement - 0.001f;
            if (Button(new Rect(r.xMax - pad - bw * 2 - 6, y, bw, 30), $"Испортить +{State.DebaseGain(human)}")) State.Debase(human);
            GUI.enabled = human.Debasement > 0.001f && human.Gold >= State.RecoinCost(human);
            if (Button(new Rect(r.xMax - pad - bw, y, bw, 30), $"Перечеканить −{State.RecoinCost(human)}")) State.Recoin(human);
            GUI.enabled = true;
            y += 46;

            char rating = State.CreditRating(human);
            var ratingColor = rating == 'A' ? CGood : rating == 'B' ? "#5a7a1f" : rating == 'C' ? CWarn : CBad;
            Label(new Rect(r.x + pad, y, r.width - pad * 2 - bw * 2, 44),
                $"<b>Долг</b> {human.Debt}, ставка {State.InterestRate(human):0%}/год (−{State.InterestPerTurn(human)}/ход)\n" +
                $"<size={Sz(12)}>Рейтинг <color={ratingColor}><b>{rating}</b> — {GameState.RatingText(rating)}</color>, лимит {State.CreditLimit(human)}" +
                (State.IsBankrupt(human) ? $", <color={CBad}>банкрот ещё {human.BankruptUntil - Turns.Turn} х.: нет торговли</color>" : "") +
                (human.MissedPayments > 0 ? $", просрочек {human.MissedPayments}" : "") + "</size>", Body);
            GUI.enabled = State.CanBorrow(human);
            if (Button(new Rect(r.xMax - pad - bw * 2 - 6, y, bw, 30), $"Заём +{GameState.LoanStep}")) State.Borrow(human);
            GUI.enabled = human.Debt > 0 && human.Gold >= Mathf.Min(GameState.LoanStep, human.Debt);
            if (Button(new Rect(r.xMax - pad - bw, y, bw, 30), $"Вернуть {Mathf.Min(GameState.LoanStep, Mathf.Max(human.Debt, 0))}")) State.Repay(human);
            GUI.enabled = true;
            y += 48;

            var mine = Cities.Where(c => c.OwnerIndex == human.Index).ToList();
            int granaries = mine.Count(c => c.Buildings.Keys.Any(b => b.storageBonus > 0));
            int rawKinds = goods.Count(g => g.IsRaw && mine.Any(c => State.GoodOutput(c, g, Season.Harvest) + State.GoodOutput(c, g, Season.Sowing) > 0));
            int mono = mine.Count(c => State.Monoculture(c).share >= GameState.MonocultureShare);
            int thirsty = mine.Count(c => State.WaterDeficit(c) > 0);
            Label(new Rect(r.x + pad, y, r.width - pad * 2, r.yMax - 10 - y),
                $"Страховка от шоков: резерв закупает зерно в засуху (по {GameState.ReserveGrainPrice}) и гасит пожары ({GameState.ReserveFireCost}).\n" +
                $"Амбары: {granaries}/{mine.Count} городов · видов сырья: {rawKinds} · монокультура: {mono} гор. · нехватка воды: {thirsty} гор.", CaptionInk);
        }

        // ---------- Дерево развития ----------

        private bool _showTech;
        private Runeterra.Tech.TechData _hoverTech;

        public void ShowTech(bool show) => _showTech = show;

        private Rect TechRect()
        {
            float w = Mathf.Min(W - 24, 1240), h = Mathf.Min(740, H - 96);
            return new Rect((W - w) / 2f, 84, w, h);
        }

        private void DrawTechTree(PlayerState human)
        {
            var r = Hud(TechRect());
            Window(r);
            float pad = 14, colW = (r.width - pad * 7) / 6f, rowH = 60, top = r.y + 112;
            Label(new Rect(r.x + pad + 8, r.y + 10, r.width - pad * 2, 30), "Развитие", Heading);
            if (CloseButton(r)) _showTech = false;
            var epochs = string.Join("   ", TechSystem.AllEpochs.Select(e => human.Epochs.Contains(e)
                ? $"<color={CGood}>+ {TechSystem.EpochName(e)} ({TechSystem.EpochEffect(e)})</color>"
                : $"{TechSystem.EpochName(e)}: {string.Join(", ", State.Tech.EpochMissing(human, e))}"));
            Label(new Rect(r.x + pad + 8, r.y + 42, r.width - pad * 2 - 16, 68),
                $"Знания +{State.Tech.Science(human)} за ход, изучено {human.Techs.Count}. Цена +10% за каждый изученный, −25% если узел знает сосед. " +
                "Практика (поля, мастера, караваны, города, жители, бои) сама двигает узлы своей ветки. Без носителей год — узел теряется.\n" +
                $"<size={Sz(12)}>{epochs}</size>", BodySmall);

            _hoverTech = null;
            var mouse = Mouse;
            foreach (Runeterra.Tech.TechBranch branch in System.Enum.GetValues(typeof(Runeterra.Tech.TechBranch)))
            {
                int col = (int)branch;
                float x = r.x + pad + col * (colW + pad);
                bool carrier = State.Tech.HasCarrier(human, branch);
                float practice = State.Tech.Practice(human, branch);
                Label(new Rect(x, top, colW, 38),
                    $"<b>{Runeterra.Tech.TechBranches.Name(branch)}</b>\n<size={Sz(11)}>" +
                    (carrier ? $"<color={CGood}>носители есть</color>" : $"<color={CBad}>нет носителей!</color>") +
                    (practice > 0.05f ? $" · практика +{practice:0.#}" : "") + "</size>", BodySmall);
                var used = new HashSet<int>();
                foreach (var t in techs.Where(t => t.branch == branch).OrderBy(t => t.order))
                {
                    int row = t.order;
                    while (used.Contains(row)) row++;
                    used.Add(row);
                    var b = new Rect(x, top + 42 + row * (rowH + 6), colW, rowH);
                    bool known = State.Tech.IsKnown(human, t), available = State.Tech.IsAvailable(human, t), current = human.Researching == t;
                    bool visible = State.Tech.IsVisible(human, t), doctrine = State.Tech.IsLockedByDoctrine(human, t);
                    float progress = State.Tech.Progress(human, t);
                    string status = known ? $"<color={CGood}>изучено</color>"
                        : doctrine ? $"<color={CBad}>закрыто доктриной</color>"
                        : $"{(current ? $"<color={CGold}>" : "")}{(progress > 0 ? $"{progress:0}/" : "")}{State.Tech.Cost(human, t)} зн. · {State.Tech.TurnsLeft(human, t)} х.{(current ? "</color>" : "")}" +
                          (State.Tech.NeighborKnows(human, t) ? $" <color={CCyan}>сосед</color>" : "");
                    var label = !visible ? $"<b>???</b>\n<size={Sz(11)}>скрытый узел</size>"
                        : $"{(current ? "» " : "")}<b>{t.displayName}</b>{(string.IsNullOrEmpty(t.downside) ? "" : $" <color={CWarn}>(!)</color>")}" +
                          $"\n<size={Sz(11)}>{status}{(t.exclusiveWith != null && !known && !doctrine ? $" <color={CViolet}>развилка</color>" : "")}</size>";
                    GUI.enabled = available || known || current;
                    var style = known ? NodeKnown : current ? NodeCurrent : Node;
                    if (GUI.Button(R(b), label, style) && available) State.Tech.Choose(human, t);
                    GUI.enabled = true;
                    if (b.Contains(mouse)) _hoverTech = t;
                }
            }

            var info = _hoverTech ?? human.Researching;
            var infoRect = new Rect(r.x + pad + 8, r.yMax - 76, r.width - pad * 2 - 16, 66);
            Fill(new Rect(infoRect.x, infoRect.y - 6, infoRect.width, 1), GoldLine);
            if (info != null && !State.Tech.IsVisible(human, info))
                Label(infoRect, $"<b>Скрытый узел</b> ({Runeterra.Tech.TechBranches.Name(info.branch)}). {info.revealHint}", Body);
            else if (info != null)
            {
                var pre = info.prerequisites.Where(p => p != null).Select(p => p.displayName).ToList();
                Label(infoRect, $"<b>{info.displayName}</b> ({Runeterra.Tech.TechBranches.Name(info.branch)}): {info.effect}" +
                                (info.exclusiveWith != null ? $"\n<color={CViolet}>Доктрина: взяв этот узел, «{info.exclusiveWith.displayName}» станет недоступен</color>" : "") +
                                (string.IsNullOrEmpty(info.downside) ? "" : $"\n<color={CWarn}>Минус: {info.downside}</color>") +
                                (pre.Count > 0 ? $"\nНужно: {string.Join(", ", pre)}" : ""), Body);
            }
        }

        // ---------- Список юнитов ----------

        public void ShowUnitList(bool show) => _showUnits = show;

        private void DrawUnitList(PlayerState human)
        {
            var r = Hud(new Rect(410, 240, 460, 380));
            Window(r);
            float pad = 20;
            int ready = human.Units.Count(u => u.MovesLeft > 0);
            Label(new Rect(r.x + pad, r.y + 10, r.width - pad * 2, 30), "Юниты", Heading);
            if (CloseButton(r)) _showUnits = false;
            Label(new Rect(r.x + pad, r.y + 40, r.width - pad * 2, 18), $"Всего {human.Units.Count}, могут ходить {ready}. Клик — выбрать и показать на карте", CaptionInk);
            var units = human.Units.OrderByDescending(u => u.MovesLeft > 0).ThenBy(u => u.Data.displayName).ToList();
            const float rowH = 32;
            var area = new Rect(r.x + pad, r.y + 64, r.width - pad * 2, r.height - 76);
            var view = new Rect(0, 0, area.width - 18, units.Count * rowH);
            _unitScroll = GUI.BeginScrollView(R(area), _unitScroll, R(view));
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var near = Cities.Where(c => c.OwnerIndex == human.Index).OrderBy(c => c.Coord.DistanceTo(u.Coord)).FirstOrDefault();
                var where = near == null ? "" : near.Coord == u.Coord ? $"в {near.Data.displayName}" : $"{near.Coord.DistanceTo(u.Coord)} кл. от {near.Data.displayName}";
                var label = $"{(u == _selected ? "» " : "")}<b>{u.Data.displayName}</b>{(u.Level > 0 ? $" ({u.LevelName})" : "")}  HP {u.Health}  " +
                            (u.MovesLeft > 0 ? $"<color={CGood}>ход {u.MovesLeft}/{u.Data.movement}</color>" : $"<color={CMuted}>сходил</color>") +
                            $"  <size={Sz(11)}>{where}</size>";
                if (GUI.Button(R(new Rect(0, i * rowH, view.width, rowH - 4)), label, Row))
                {
                    SelectUnit(u);
                    FocusOn(u.Coord);
                }
            }
            GUI.EndScrollView();
        }

        // ---------- Меню ----------

        private bool _showMenu;
        private int _menuConfirm; // 0 — выбор, 1 — выход в главное меню, 2 — выход из игры

        public void ShowMenu(bool show)
        {
            _showMenu = show;
            _menuConfirm = 0;
        }

        private void DrawMenu()
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.35f));
            var r = Hud(new Rect(W / 2f - 200, H / 2f - 150, 400, 300));
            Window(r);
            if (CloseButton(r)) { _showMenu = false; _menuConfirm = 0; }
            float x = r.x + 40, w = r.width - 80;
            if (_menuConfirm == 0)
            {
                Label(new Rect(r.x, r.y + 18, r.width, 36), "Меню", new GUIStyle(Heading) { alignment = TextAnchor.MiddleCenter });
                if (Button(new Rect(x, r.y + 80, w, 44), "Продолжить", BtnBig)) _showMenu = false;
                if (Button(new Rect(x, r.y + 136, w, 44), "Выйти в главное меню", BtnBig)) _menuConfirm = 1;
                if (Button(new Rect(x, r.y + 192, w, 44), "Выйти из игры", BtnBig)) _menuConfirm = 2;
                return;
            }
            Label(new Rect(r.x, r.y + 18, r.width, 36), _menuConfirm == 1 ? "Выйти в главное меню?" : "Выйти из игры?",
                new GUIStyle(Heading) { alignment = TextAnchor.MiddleCenter });
            Label(new Rect(x, r.y + 70, w, 60), $"<color={CBad}>Несохранённый прогресс будет потерян.</color>", Center);
            float bw = (w - 12) / 2f;
            if (Button(new Rect(x, r.y + 160, bw, 44), "Выйти", BtnBig))
            {
                if (_menuConfirm == 1) UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
                else Application.Quit();
            }
            if (Button(new Rect(x + bw + 12, r.y + 160, bw, 44), "Отмена", BtnBig)) _menuConfirm = 0;
        }

        // ---------- Итог партии ----------

        private void DrawGameOver()
        {
            Fill(Hud(new Rect(0, 0, W, H)), new Color(0f, 0f, 0f, 0.55f));
            bool won = Players[Winner.Value].IsHuman;
            var r = new Rect(W / 2f - 330, H / 2f - 160, 660, 320);
            Window(r);
            Label(new Rect(r.x, r.y + 30, r.width, 80), won ? $"<color={CGold}>Победа!</color>" : $"<color={CBad}>Поражение</color>", BigTitle);
            Label(new Rect(r.x + 30, r.y + 118, r.width - 60, 60), $"{State.GameOverText}. Ход {Turns.Turn}.", Center);
            if (Button(new Rect(W / 2f - 120, r.yMax - 100, 240, 62), "Заново", BtnBig)) Restart();
        }

        private static string TerrainName(TerrainType t) => t switch
        {
            TerrainType.Ocean => "Океан",
            TerrainType.Coast => "Прибрежные воды",
            TerrainType.Plains => "Равнина",
            TerrainType.Grassland => "Луга",
            TerrainType.Desert => "Пустыня",
            TerrainType.Hills => "Холмы",
            _ => "Горы",
        };

        private static string FeatureName(TileFeature f) => f switch
        {
            TileFeature.Forest => "лес",
            TileFeature.Oasis => "оазис",
            _ => "",
        };
    }
}

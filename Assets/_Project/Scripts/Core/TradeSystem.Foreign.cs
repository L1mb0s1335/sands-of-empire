using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Economy;

namespace Runeterra.Core
{
    /// <summary>
    /// Торговля между странами.
    /// • Торговое соглашение открывает границу: купцы везут караваны в города партнёра с рынком.
    /// • Пошлина: продавец берёт свою пошлину с наценки, получатель — ввозную (по соглашению вдвое меньше).
    /// • Эмбарго закрывает границу и рвёт соглашение; война рвёт торговлю, караваны в пути конфискуются.
    /// • Прямые сделки: купить или продать партию товара со склада столицы за золото.
    /// </summary>
    public partial class TradeSystem
    {
        public const int DealAmount = 5;
        public const float DealBuyMarkup = 1.2f;
        public const float DealSellDiscount = 0.8f;

        private readonly HashSet<(int, int)> _agreements = new HashSet<(int, int)>();
        /// <summary>Эмбарго: (кто объявил, против кого).</summary>
        private readonly HashSet<(int, int)> _embargoes = new HashSet<(int, int)>();

        private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

        internal HashSet<(int, int)> Agreements => _agreements;
        internal HashSet<(int, int)> EmbargoSet => _embargoes;

        public bool HasAgreement(int a, int b) => _agreements.Contains(Pair(a, b));

        public bool HasEmbargo(int a, int b) => _embargoes.Contains((a, b)) || _embargoes.Contains((b, a));

        public bool Embargoes(int by, int against) => _embargoes.Contains((by, against));

        /// <summary>Открыта ли граница для караванов между сторонами.</summary>
        public bool CanTradeWith(int a, int b) =>
            a == b || (HasAgreement(a, b) && !_game.AtWar(a, b) && !HasEmbargo(a, b) &&
                       !_game.IsBankrupt(_game.Players[a]) && !_game.IsBankrupt(_game.Players[b]));

        /// <summary>Ввозная пошлина получателя для товаров стороны sender.</summary>
        public float ImportDuty(int receiver, int sender) =>
            receiver == sender ? 0f : _game.Players[receiver].Tariff * (HasAgreement(receiver, sender) ? 0.5f : 1f);

        private string Name(int i) => _game.Players[i].Region.displayName;

        // ---------- Соглашения и эмбарго ----------

        public string CanSignAgreement(int a, int b)
        {
            if (a == b) return "это вы";
            if (HasAgreement(a, b)) return "соглашение уже есть";
            if (_game.AtWar(a, b)) return "идёт война";
            if (HasEmbargo(a, b)) return "действует эмбарго";
            if (_game.Diplomacy.Opinion(a, b) < -30) return "мнение ниже −30";
            return null;
        }

        public void SignAgreement(int a, int b)
        {
            if (CanSignAgreement(a, b) != null) return;
            _agreements.Add(Pair(a, b));
            _game.Diplomacy.AddOpinion(a, b, 5);
            _game.Report($"Торговое соглашение: {Name(a)} и {Name(b)} открывают границы для караванов");
        }

        public void CancelAgreement(int a, int b, string why)
        {
            if (!_agreements.Remove(Pair(a, b))) return;
            ConfiscateBetween(a, b, why);
            _game.Report($"{Name(a)} и {Name(b)}: торговое соглашение расторгнуто ({why})");
        }

        public void SetEmbargo(int by, int against, bool on)
        {
            if (on == Embargoes(by, against) || by == against) return;
            if (on)
            {
                _embargoes.Add((by, against));
                _game.Diplomacy.AddOpinion(by, against, -15);
                _game.Report($"{Name(by)} объявляет эмбарго: {Name(against)}");
                CancelAgreement(by, against, "эмбарго");
                ConfiscateBetween(by, against, "эмбарго");
            }
            else
            {
                _embargoes.Remove((by, against));
                _game.Report($"{Name(by)} снимает эмбарго: {Name(against)}");
            }
        }

        /// <summary>Караваны между сторонами пропадают (конфискация на границе).</summary>
        private void ConfiscateBetween(int a, int b, string why)
        {
            foreach (var c in Caravans.Where(c => c.To != null &&
                         ((c.OwnerIndex == a && c.To.OwnerIndex == b) || (c.OwnerIndex == b && c.To.OwnerIndex == a))).ToList())
                Finish(c, false, $"конфискован: {why}");
        }

        /// <summary>Война рвёт торговлю: соглашение расторгнуто, караваны конфискованы.</summary>
        public void OnWar(int a, int b)
        {
            ClearRouteCache();
            CancelAgreement(a, b, "война");
            ConfiscateBetween(a, b, "война");
        }

        /// <summary>Соглашение улучшает мнение (+1 раз в 2 хода), эмбарго портит (−1).</summary>
        public int OpinionBonus(int a, int b)
        {
            if (HasEmbargo(a, b)) return -1;
            return HasAgreement(a, b) && _game.Turns.Turn % 2 == 0 ? 1 : 0;
        }

        /// <summary>Караванов в пути между сторонами (в обе стороны).</summary>
        public int CaravansBetween(int a, int b) =>
            Caravans.Count(c => c.To != null && ((c.OwnerIndex == a && c.To.OwnerIndex == b) || (c.OwnerIndex == b && c.To.OwnerIndex == a)));

        // ---------- Ввозная пошлина ----------

        private readonly Dictionary<int, int> _importAccum = new Dictionary<int, int>();
        internal Dictionary<int, int> ImportAccum => _importAccum;

        /// <summary>Получатель заработал ввозную пошлину (зачисляется сразу, в отчёт — к его ходу).</summary>
        private void CollectImportDuty(int receiver, int amount)
        {
            if (amount <= 0) return;
            _game.Players[receiver].Gold += amount;
            _importAccum[receiver] = (_importAccum.TryGetValue(receiver, out var v) ? v : 0) + amount;
        }

        /// <summary>Начало хода: ввозные пошлины, собранные за круг, — в отчёт.</summary>
        private void CloseImportReport(PlayerState player)
        {
            player.ImportDutyLastTurn = _importAccum.TryGetValue(player.Index, out var v) ? v : 0;
            _importAccum[player.Index] = 0;
        }

        // ---------- Прямые сделки ----------

        private City Capital(int p) => _game.CapitalOf(_game.Players[p]);

        /// <summary>Цена покупки партии у стороны seller (по цене её столицы с наценкой).</summary>
        public int DealBuyCost(int seller, GoodData good, int amount = DealAmount) =>
            Capital(seller) is City c ? (int)Math.Ceiling(Price(c, good) * amount * DealBuyMarkup) : 0;

        /// <summary>Выручка за партию, проданную стороне buyer (по цене её столицы со скидкой).</summary>
        public int DealSellGain(int buyer, GoodData good, int amount = DealAmount) =>
            Capital(buyer) is City c ? (int)Math.Floor(Price(c, good) * amount * DealSellDiscount) : 0;

        private string DealBlocked(int a, int b)
        {
            if (a == b) return "это вы";
            if (_game.AtWar(a, b)) return "идёт война";
            if (HasEmbargo(a, b)) return "эмбарго";
            if (Capital(a) == null || Capital(b) == null) return "нет столицы";
            return null;
        }

        /// <summary>Может ли buyer купить партию товара у seller (со склада столицы продавца).</summary>
        public string CanBuy(int buyer, int seller, GoodData good, int amount = DealAmount)
        {
            if (DealBlocked(buyer, seller) is string r) return r;
            if (!Capital(seller).Warehouse.Has(good, amount + 2)) return $"у них мало: {Capital(seller).Warehouse.Get(good):0}";
            if (_game.Players[buyer].Gold < DealBuyCost(seller, good, amount)) return "не хватает золота";
            return null;
        }

        public string CanSell(int seller, int buyer, GoodData good, int amount = DealAmount)
        {
            if (DealBlocked(seller, buyer) is string r) return r;
            if (!Capital(seller).Warehouse.Has(good, amount)) return $"в столице мало: {Capital(seller).Warehouse.Get(good):0}";
            if (_game.Players[buyer].Gold < DealSellGain(buyer, good, amount) + 20) return "у них нет золота";
            return null;
        }

        /// <summary>Согласен ли ИИ продать (не продаёт себе в убыток и тем, кого не терпит).</summary>
        public bool SellerAgrees(int seller, int buyer, GoodData good, int amount = DealAmount) =>
            _game.Diplomacy.Opinion(seller, buyer) >= -30 &&
            Capital(seller).Warehouse.Get(good) >= amount + TargetStock(Capital(seller)) * 0.25f;

        /// <summary>Согласен ли ИИ купить (товар ему нужен — цена в его столице не ниже базы).</summary>
        public bool BuyerAgrees(int buyer, int seller, GoodData good) =>
            _game.Diplomacy.Opinion(buyer, seller) >= -30 && Price(Capital(buyer), good) >= good.basePrice * 0.9f;

        public bool Buy(int buyer, int seller, GoodData good, int amount = DealAmount)
        {
            if (CanBuy(buyer, seller, good, amount) != null) return false;
            int cost = DealBuyCost(seller, good, amount);
            _game.Players[buyer].Gold -= cost;
            _game.Players[seller].Gold += cost;
            Capital(seller).Warehouse.Take(good, amount);
            Capital(buyer).Warehouse.Add(good, amount);
            _game.Diplomacy.AddOpinion(buyer, seller, 2);
            _game.Report($"Сделка: {Name(buyer)} покупает у {Name(seller)} {amount} × {good.displayName} за {cost} золота");
            return true;
        }

        public bool Sell(int seller, int buyer, GoodData good, int amount = DealAmount)
        {
            if (CanSell(seller, buyer, good, amount) != null) return false;
            int gain = DealSellGain(buyer, good, amount);
            _game.Players[buyer].Gold -= gain;
            _game.Players[seller].Gold += gain;
            Capital(seller).Warehouse.Take(good, amount);
            Capital(buyer).Warehouse.Add(good, amount);
            _game.Diplomacy.AddOpinion(buyer, seller, 2);
            _game.Report($"Сделка: {Name(seller)} продаёт {Name(buyer)} {amount} × {good.displayName} за {gain} золота");
            return true;
        }
    }
}

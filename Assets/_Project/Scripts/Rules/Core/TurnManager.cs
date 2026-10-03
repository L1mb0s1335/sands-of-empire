using System;
using System.Collections.Generic;
using Runeterra.Map;
using Runeterra.Units;

namespace Runeterra.Core
{
    public class PlayerState
    {
        public int Index { get; }
        public RegionData Region { get; }
        public bool IsHuman { get; }
        public List<Unit> Units { get; } = new List<Unit>();
        public int Gold { get; internal set; }
        /// <summary>Пошлина с караванов (0…0.3).</summary>
        public float Tariff { get; internal set; } = 0.1f;
        /// <summary>Пошлины, собранные за последний ход.</summary>
        public int TariffIncomeLastTurn { get; internal set; }
        /// <summary>Ввозные пошлины с иностранных караванов за последний круг ходов.</summary>
        public int ImportDutyLastTurn { get; internal set; }
        /// <summary>Всего пошлин за партию (для очков).</summary>
        public int TradeIncomeTotal { get; internal set; }

        // ---------- Налоги и казна ----------

        /// <summary>Земельный налог: доля стоимости добытого сырья.</summary>
        public float LandTax { get; internal set; } = 0.1f;
        /// <summary>Подушный налог: золото с каждого жителя.</summary>
        public float PeopleTax { get; internal set; } = 0.05f;
        /// <summary>Налог на роскошь: доля стоимости потреблённых предметов роскоши.</summary>
        public float LuxuryTax { get; internal set; } = 0.05f;

        /// <summary>Резервная казна: не тратится на покупки, закрывает шоки (засуха, пожар).</summary>
        public int Reserve { get; internal set; }

        /// <summary>Сбор налогов за последний ход: земля, люди, роскошь; и сколько ушло контрабандой.</summary>
        public int LandIncome { get; internal set; }
        public int PeopleIncome { get; internal set; }
        public int LuxuryIncome { get; internal set; }
        public int SmuggledLastTurn { get; internal set; }

        // ---------- Монета и долги ----------

        /// <summary>Порча монеты (0…0.5): быстрый доход, но инфляция и уход торговцев.</summary>
        public float Debasement { get; internal set; }
        /// <summary>Долг купеческим домам.</summary>
        public int Debt { get; internal set; }
        public int MissedPayments { get; internal set; }
        public int InterestLastTurn { get; internal set; }
        /// <summary>До этого хода действует банкротство: нет торговли и займов.</summary>
        public int BankruptUntil { get; internal set; }
        /// <summary>До этого хода рейтинг не выше C (память о банкротстве).</summary>
        public int StigmaUntil { get; internal set; }

        // ---------- Развитие ----------

        /// <summary>Изученные узлы (по id).</summary>
        public SortedSet<string> Techs { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public Runeterra.Tech.TechData Researching { get; internal set; }

        /// <summary>Накопленный прогресс по каждому узлу (сохраняется при смене темы, растёт и от практики).</summary>
        public SortedDictionary<string, float> TechProgress { get; } = new SortedDictionary<string, float>(StringComparer.Ordinal);

        public float ResearchProgress
        {
            get => Researching != null && TechProgress.TryGetValue(Researching.id, out var v) ? v : 0f;
            set { if (Researching != null) TechProgress[Researching.id] = value; }
        }

        /// <summary>Открытые скрытые узлы и наступившие эпохи.</summary>
        public SortedSet<string> RevealedTechs { get; } = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> Epochs { get; } = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Практика за последний ход: караваны доставлены, боёв проведено.</summary>
        public int CaravansDeliveredLastTurn { get; internal set; }
        public int BattlesThisTurn { get; internal set; }
        public int BattlesLastTurn { get; internal set; }
        /// <summary>Ветки без носителей знаний: сколько ходов подряд.</summary>
        public SortedDictionary<Runeterra.Tech.TechBranch, int> BranchWithoutCarrier { get; } = new SortedDictionary<Runeterra.Tech.TechBranch, int>();
        public int ScienceLastTurn { get; internal set; }
        public int ArmyUpkeepLastTurn { get; internal set; }
        /// <summary>Лет интенсивного земледелия (истощение почвы).</summary>
        public int SoilExhaustionYears { get; internal set; }

        public bool Has(string techId) => Techs.Contains(techId);

        public PlayerState(int index, RegionData region, bool isHuman)
        {
            Index = index;
            Region = region;
            IsHuman = isHuman;
            Gold = region != null ? region.startingGold : 0;
        }
    }

    /// <summary>
    /// Очерёдность ходов. Чистый C#: ходят игроки по кругу, после последнего — новый ход.
    /// ИИ-игроки ходят через aiTurn и сразу передают ход дальше.
    /// </summary>
    public class TurnManager
    {
        public int Turn { get; private set; } = 1;
        public int CurrentIndex { get; private set; }
        public IReadOnlyList<PlayerState> Players => _players;
        public PlayerState Current => _players[CurrentIndex];

        public event Action<PlayerState> PlayerTurnStarted;
        public event Action<int> NewTurnStarted;

        private readonly List<PlayerState> _players;

        public TurnManager(List<PlayerState> players)
        {
            _players = players;
        }

        /// <summary>Восстановление из сохранения: ход продолжается с того же игрока, без начисления дохода.</summary>
        internal void Restore(int turn, int currentIndex)
        {
            Turn = turn;
            CurrentIndex = currentIndex;
        }

        public void Start()
        {
            NewTurnStarted?.Invoke(Turn);
            BeginPlayerTurn();
        }

        public void EndTurn()
        {
            CurrentIndex++;
            if (CurrentIndex >= _players.Count)
            {
                CurrentIndex = 0;
                Turn++;
                NewTurnStarted?.Invoke(Turn);
            }
            BeginPlayerTurn();
        }

        public Unit UnitAt(HexCoord coord)
        {
            foreach (var p in _players)
                foreach (var u in p.Units)
                    if (u.IsAlive && u.Coord == coord) return u;
            return null;
        }

        /// <summary>Убирает погибших юнитов из списков игроков.</summary>
        public void RemoveDead()
        {
            foreach (var p in _players) p.Units.RemoveAll(u => !u.IsAlive);
        }

        /// <summary>Начало хода стороны. Ходы ИИ отыгрывает шина команд (<see cref="CommandBus.RunAiTurns"/>).</summary>
        private void BeginPlayerTurn()
        {
            foreach (var u in Current.Units) u.ResetMoves();
            PlayerTurnStarted?.Invoke(Current);
        }
    }
}

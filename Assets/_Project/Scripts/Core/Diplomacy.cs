using System;
using System.Collections.Generic;
using System.Linq;
using Runeterra.Cities;

namespace Runeterra.Core
{
    /// <summary>Состояние отношений пары сторон.</summary>
    public enum Stance
    {
        /// <summary>Мир без договоров.</summary>
        Peace,
        War,
        /// <summary>Перемирие после войны (или стартовое): войну объявить нельзя до его конца.</summary>
        Truce,
        /// <summary>Пакт о ненападении на срок.</summary>
        NonAggression,
        /// <summary>Союз: союзники вступают в оборонительную войну друг за друга.</summary>
        Alliance,
    }

    /// <summary>Отношения пары сторон (симметричные).</summary>
    public class Relation
    {
        /// <summary>Мнение −100…+100.</summary>
        public int Value;
        public Stance Stance;
        /// <summary>До какого хода действует перемирие или пакт.</summary>
        public int Until;
        /// <summary>Ход начала войны (для усталости и мирных переговоров).</summary>
        public int Since;
    }

    /// <summary>
    /// Претензия на город — единственный законный повод к войне. Историческая действует сразу,
    /// созданная (подложные грамоты, обиженные наследники) созревает несколько ходов.
    /// </summary>
    public class Claim
    {
        public int Owner;
        public string CityId;
        public int ReadyTurn;
        public bool Historical;
    }

    public enum ProposalKind
    {
        Peace,
        NonAggression,
        Alliance,
        TradeAgreement,
    }

    /// <summary>Предложение ИИ игроку (ждёт ответа в окне «Дипломатия»).</summary>
    public class Proposal
    {
        public int From;
        public ProposalKind Kind;
        public int Turn;
    }

    /// <summary>
    /// Дипломатия: мнения сторон, войны, перемирия, пакты, союзы и претензии. Чистый C#.
    /// Первые 20 ходов действует стартовое перемирие; войну можно объявить только при созревшей претензии
    /// на город противника. Объявление войны тянет в неё союзников жертвы.
    /// </summary>
    public class Diplomacy
    {
        public const int StartTruceTurns = 20;
        public const int ClaimMaturity = 8;
        public const int ClaimCost = 40;
        public const int ClaimRange = 8;
        public const int TruceTurns = 10;
        public const int PactTurns = 20;
        public const int AllianceOpinion = 40;
        public const int PactOpinion = -10;

        private readonly GameState _game;
        private readonly Dictionary<(int, int), Relation> _relations = new Dictionary<(int, int), Relation>();

        public List<Claim> Claims { get; } = new List<Claim>();
        public List<Proposal> Proposals { get; } = new List<Proposal>();

        /// <summary>Коалиция против сильнейшего: против кого (-1 — нет) и кто в ней.</summary>
        public int CoalitionTarget { get; internal set; } = -1;
        public HashSet<int> Coalition { get; } = new HashSet<int>();

        public bool InCoalition(int a) => CoalitionTarget >= 0 && Coalition.Contains(a);

        /// <summary>Обе стороны в коалиции — союз заключается при меньшем мнении.</summary>
        public int AllianceOpinionFor(int a, int b) => InCoalition(a) && InCoalition(b) ? 10 : AllianceOpinion;

        public bool SameFaith(int a, int b) =>
            Christian.Contains(_game.Players[a].Region.id) == Christian.Contains(_game.Players[b].Region.id);

        /// <summary>Сценарий без войн: объявить войну нельзя никому.</summary>
        public bool WarsDisabled { get; set; }

        public event Action Changed;

        public Diplomacy(GameState game)
        {
            _game = game;
        }

        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        public Relation Get(int a, int b)
        {
            var k = Key(a, b);
            if (!_relations.TryGetValue(k, out var r)) _relations[k] = r = new Relation { Value = 0, Stance = Stance.Peace };
            return r;
        }

        internal IEnumerable<((int, int) key, Relation rel)> All => _relations.Select(kv => (kv.Key, kv.Value));

        internal void Set(int a, int b, Relation r) => _relations[Key(a, b)] = r;

        public int Opinion(int a, int b) => a == b ? 100 : Get(a, b).Value;

        public Stance StanceOf(int a, int b) => a == b ? Stance.Alliance : Get(a, b).Stance;

        public bool AtWar(int a, int b) => a != b && Get(a, b).Stance == Stance.War;

        public bool Allied(int a, int b) => a != b && Get(a, b).Stance == Stance.Alliance;

        public void AddOpinion(int a, int b, int delta)
        {
            if (a == b) return;
            var r = Get(a, b);
            r.Value = Math.Max(-100, Math.Min(100, r.Value + delta));
        }

        public IEnumerable<int> EnemiesOf(int a) => _game.Players.Where(p => AtWar(a, p.Index)).Select(p => p.Index);

        public IEnumerable<int> AlliesOf(int a) => _game.Players.Where(p => Allied(a, p.Index)).Select(p => p.Index);

        public static string StanceName(Stance s) => s switch
        {
            Stance.War => "война",
            Stance.Truce => "перемирие",
            Stance.NonAggression => "пакт о ненападении",
            Stance.Alliance => "союз",
            _ => "мир",
        };

        // ---------- Начало партии ----------

        private static readonly HashSet<string> Christian = new HashSet<string> { "jerusalem_kingdom", "byzantium" };

        /// <summary>Исторические претензии 1187 года: (кто, на какой город).</summary>
        private static readonly (string owner, string city)[] HistoricalClaims =
        {
            ("palestine", "acre"),          // Салах ад-Дин против латинян
            ("jerusalem_kingdom", "jerusalem"),
            ("rum", "attalia"),             // сельджуки рвутся к морю
            ("byzantium", "konya"),         // Иконий — ромейский город
            ("mosul", "damascus"),          // наследство Нур ад-Дина
            ("palestine", "sinjar"),        // Салах ад-Дин брал Синджар в 1182
        };

        /// <summary>Исходные мнения (вера, старые обиды) и стартовое перемирие на 20 ходов.</summary>
        public void Setup()
        {
            var players = _game.Players;
            foreach (var a in players)
            foreach (var b in players.Where(p => p.Index > a.Index))
            {
                int v = Christian.Contains(a.Region.id) == Christian.Contains(b.Region.id) ? 10 : -20;
                v += Grudge(a.Region.id, b.Region.id);
                Set(a.Index, b.Index, new Relation { Value = v, Stance = Stance.Truce, Until = StartTruceTurns });
            }
            foreach (var (owner, cityId) in HistoricalClaims)
            {
                var p = players.FirstOrDefault(x => x.Region.id == owner);
                var city = _game.Cities.FirstOrDefault(c => c.Data.id == cityId);
                if (p == null || city == null || city.OwnerIndex == p.Index) continue;
                Claims.Add(new Claim { Owner = p.Index, CityId = cityId, ReadyTurn = 0, Historical = true });
            }
        }

        private static int Grudge(string a, string b)
        {
            bool Pair(string x, string y) => (a == x && b == y) || (a == y && b == x);
            if (Pair("palestine", "jerusalem_kingdom")) return -30;
            if (Pair("byzantium", "rum")) return -20;
            if (Pair("palestine", "mosul")) return -15;
            if (Pair("abbasids", "mosul")) return -5;
            if (Pair("palestine", "abbasids")) return 15; // халиф признаёт султана
            return 0;
        }

        // ---------- Претензии ----------

        public City CityById(string id) => _game.Cities.FirstOrDefault(c => c.Data.id == id);

        /// <summary>Действующие претензии стороны (город чужой).</summary>
        public IEnumerable<Claim> ClaimsOf(int owner) =>
            Claims.Where(c => c.Owner == owner && CityById(c.CityId) is City city && city.OwnerIndex != owner);

        public bool IsRipe(Claim c) => _game.Turns.Turn >= c.ReadyTurn;

        /// <summary>Созревшая претензия a на город стороны b.</summary>
        public Claim RipeClaim(int a, int b) =>
            ClaimsOf(a).FirstOrDefault(c => IsRipe(c) && CityById(c.CityId).OwnerIndex == b);

        public string CanFabricate(int owner, City city)
        {
            if (WarsDisabled) return "войны отключены сценарием";
            if (city.OwnerIndex == owner) return "свой город";
            if (Claims.Any(c => c.Owner == owner && c.CityId == city.Data.id)) return "претензия уже есть";
            if (!_game.Cities.Any(c => c.OwnerIndex == owner && c.Coord.DistanceTo(city.Coord) <= ClaimRange))
                return $"слишком далеко (нужен свой город в {ClaimRange} кл.)";
            if (_game.Players[owner].Gold < ClaimCost) return $"нужно {ClaimCost} золота";
            return null;
        }

        /// <summary>Создать претензию: золото на грамоты, мнение владельца города падает, созревает 8 ходов.</summary>
        public bool Fabricate(int owner, City city)
        {
            if (CanFabricate(owner, city) != null) return false;
            _game.Players[owner].Gold -= ClaimCost;
            Claims.Add(new Claim { Owner = owner, CityId = city.Data.id, ReadyTurn = _game.Turns.Turn + ClaimMaturity });
            AddOpinion(owner, city.OwnerIndex, -15);
            _game.Report($"{Name(owner)} заявляет претензию на {city.Data.displayName} ({Name(city.OwnerIndex)}) — созреет через {ClaimMaturity} х.");
            Changed?.Invoke();
            return true;
        }

        private string Name(int i) => _game.Players[i].Region.displayName;

        // ---------- Война ----------

        public string CanDeclareWar(int a, int b)
        {
            if (a == b) return "это вы";
            if (WarsDisabled) return "войны отключены сценарием";
            if (_game.IsEliminated(_game.Players[b])) return "сторона сошла со сцены";
            var r = Get(a, b);
            switch (r.Stance)
            {
                case Stance.War: return "уже война";
                case Stance.Truce: return _game.Turns.Turn <= StartTruceTurns && r.Until == StartTruceTurns
                    ? $"стартовое перемирие до хода {StartTruceTurns}"
                    : $"перемирие до хода {r.Until}";
                case Stance.NonAggression: return $"пакт о ненападении до хода {r.Until}";
                case Stance.Alliance: return "союзник";
            }
            if (RipeClaim(a, b) == null)
            {
                var pending = ClaimsOf(a).FirstOrDefault(c => CityById(c.CityId).OwnerIndex == b);
                return pending != null ? $"претензия созреет к ходу {pending.ReadyTurn}" : "нет претензии на их город";
            }
            return null;
        }

        /// <summary>Объявить войну. Союзники жертвы вступают в войну против нападающего.</summary>
        public bool DeclareWar(int a, int b)
        {
            if (CanDeclareWar(a, b) != null) return false;
            var claim = RipeClaim(a, b);
            StartWar(a, b);
            AddOpinion(a, b, -40);
            // Остальные осуждают агрессора (кроме его союзников и врагов жертвы).
            foreach (var p in _game.Players)
                if (p.Index != a && p.Index != b && !Allied(p.Index, a) && !AtWar(p.Index, b)) AddOpinion(p.Index, a, -5);
            _game.Report($"{Name(a)} объявляет войну: {Name(b)} (претензия на {CityById(claim.CityId).Data.displayName})");
            foreach (var ally in AlliesOf(b).ToList())
            {
                if (ally == a) continue;
                if (Allied(ally, a)) { BreakTreaty(ally, a, quiet: true); }
                StartWar(ally, a);
                _game.Report($"{Name(ally)} вступает в войну на стороне союзника: {Name(b)}");
            }
            Changed?.Invoke();
            return true;
        }

        private void StartWar(int a, int b)
        {
            var r = Get(a, b);
            r.Stance = Stance.War;
            r.Since = _game.Turns.Turn;
            r.Until = 0;
            Proposals.RemoveAll(p => (p.From == a || p.From == b) && p.Kind != ProposalKind.Peace);
            _game.OnWarStarted(a, b);
        }

        /// <summary>Сколько ходов идёт война.</summary>
        public int WarTurns(int a, int b) => AtWar(a, b) ? _game.Turns.Turn - Get(a, b).Since : 0;

        public string CanMakePeace(int a, int b)
        {
            if (!AtWar(a, b)) return "войны нет";
            if (WarTurns(a, b) < 3) return "война только началась";
            return null;
        }

        /// <summary>Мир: война кончается перемирием на 10 ходов.</summary>
        public void MakePeace(int a, int b)
        {
            var r = Get(a, b);
            r.Stance = Stance.Truce;
            r.Until = _game.Turns.Turn + TruceTurns;
            AddOpinion(a, b, 10);
            Proposals.RemoveAll(p => p.Kind == ProposalKind.Peace && (p.From == a || p.From == b));
            _game.Report($"Мир: {Name(a)} и {Name(b)} заключают перемирие на {TruceTurns} ходов");
            _game.OnPeaceMade(a, b);
            _game.Trade.ClearRouteCache();
            Changed?.Invoke();
        }

        // ---------- Договоры ----------

        public string CanSignPact(int a, int b)
        {
            var r = Get(a, b);
            if (r.Stance == Stance.War) return "идёт война";
            if (r.Stance == Stance.NonAggression || r.Stance == Stance.Alliance) return "договор уже есть";
            if (r.Value < PactOpinion) return $"мнение ниже {PactOpinion}";
            return null;
        }

        public void SignPact(int a, int b)
        {
            var r = Get(a, b);
            r.Stance = Stance.NonAggression;
            r.Until = _game.Turns.Turn + PactTurns;
            AddOpinion(a, b, 10);
            _game.Report($"{Name(a)} и {Name(b)} заключают пакт о ненападении на {PactTurns} ходов");
            Changed?.Invoke();
        }

        public string CanAlly(int a, int b)
        {
            var r = Get(a, b);
            if (r.Stance == Stance.War) return "идёт война";
            if (r.Stance == Stance.Alliance) return "уже союзники";
            if (r.Value < AllianceOpinionFor(a, b)) return $"мнение ниже {AllianceOpinionFor(a, b)}";
            if (EnemiesOf(a).Any(e => Allied(e, b)) || EnemiesOf(b).Any(e => Allied(e, a))) return "союзник врага";
            return null;
        }

        public void Ally(int a, int b)
        {
            var r = Get(a, b);
            r.Stance = Stance.Alliance;
            r.Until = 0;
            AddOpinion(a, b, 15);
            _game.Report($"{Name(a)} и {Name(b)} заключают союз");
            Changed?.Invoke();
        }

        /// <summary>Разорвать пакт или союз: мир без договоров, мнение −30.</summary>
        public void BreakTreaty(int a, int b, bool quiet = false)
        {
            var r = Get(a, b);
            if (r.Stance != Stance.NonAggression && r.Stance != Stance.Alliance) return;
            r.Stance = Stance.Peace;
            r.Until = 0;
            AddOpinion(a, b, -30);
            if (!quiet) _game.Report($"{Name(a)} разрывает договор с {Name(b)}");
            Changed?.Invoke();
        }

        // ---------- Предложения игроку ----------

        public void Propose(int from, int to, ProposalKind kind)
        {
            if (Proposals.Any(p => p.From == from && p.Kind == kind)) return;
            Proposals.Add(new Proposal { From = from, Kind = kind, Turn = _game.Turns.Turn });
            _game.Report($"{Name(from)} предлагает: {KindName(kind)} — ответ в окне «Дипломатия»");
            Changed?.Invoke();
        }

        public static string KindName(ProposalKind k) => k switch
        {
            ProposalKind.Peace => "мир",
            ProposalKind.NonAggression => "пакт о ненападении",
            ProposalKind.TradeAgreement => "торговое соглашение",
            _ => "союз",
        };

        public void Answer(Proposal p, int human, bool accept)
        {
            Proposals.Remove(p);
            if (!accept)
            {
                AddOpinion(p.From, human, -5);
                _game.Report($"Вы отклоняете предложение: {KindName(p.Kind)} ({Name(p.From)})");
                return;
            }
            switch (p.Kind)
            {
                case ProposalKind.Peace when AtWar(p.From, human): MakePeace(p.From, human); break;
                case ProposalKind.NonAggression when CanSignPact(p.From, human) == null: SignPact(p.From, human); break;
                case ProposalKind.Alliance when CanAlly(p.From, human) == null: Ally(p.From, human); break;
                case ProposalKind.TradeAgreement when _game.Trade.CanSignAgreement(p.From, human) == null: _game.Trade.SignAgreement(p.From, human); break;
            }
        }

        // ---------- Коалиция против сильнейшего ----------

        public const int CoalitionFrom = 30;
        public const float CoalitionRatio = 1.5f;

        /// <summary>
        /// Если одна сторона мощнее среднего остальных в 1.5 раза (и у неё не меньше 5 городов),
        /// ИИ-стороны, не связанные с ней союзом, складываются в коалицию: мнение о ней падает каждый ход,
        /// союзы между участниками заключаются легче, войну с ней начинают при меньшем перевесе.
        /// Распадается, когда перевес падает ниже 1.3.
        /// </summary>
        private void UpdateCoalition(int turn)
        {
            if (WarsDisabled || turn < CoalitionFrom) return;
            var alive = _game.Players.Where(p => !_game.IsEliminated(p)).ToList();
            if (alive.Count < 3) { Dissolve(); return; }
            var scores = alive.ToDictionary(p => p.Index, p => _game.PowerScore(p.Index));
            var top = alive.OrderByDescending(p => scores[p.Index]).First();
            float avg = (float)alive.Where(p => p != top).Average(p => scores[p.Index]);
            int topCities = _game.Cities.Count(c => c.OwnerIndex == top.Index);

            if (CoalitionTarget >= 0 && (CoalitionTarget != top.Index || scores[top.Index] < avg * 1.3f)) Dissolve();
            if (CoalitionTarget < 0 && scores[top.Index] >= avg * CoalitionRatio && topCities >= 5)
            {
                CoalitionTarget = top.Index;
                _game.Report($"Коалиция против сильнейшего: соседи объединяются против {Name(top.Index)}");
            }
            if (CoalitionTarget < 0) return;
            Coalition.Clear();
            foreach (var p in alive)
                if (p.Index != CoalitionTarget && !p.IsHuman && !Allied(p.Index, CoalitionTarget)) Coalition.Add(p.Index);
        }

        private void Dissolve()
        {
            if (CoalitionTarget < 0) return;
            _game.Report($"Коалиция против {Name(CoalitionTarget)} распалась");
            CoalitionTarget = -1;
            Coalition.Clear();
        }

        // ---------- Ход ----------

        /// <summary>Новый ход: сроки договоров, дрейф мнений, устаревшие предложения.</summary>
        public void NewTurn(int turn)
        {
            foreach (var kv in _relations)
            {
                var r = kv.Value;
                if ((r.Stance == Stance.Truce || r.Stance == Stance.NonAggression) && turn > r.Until)
                {
                    if (r.Until != StartTruceTurns)
                        _game.Report($"{Name(kv.Key.Item1)} и {Name(kv.Key.Item2)}: срок «{StanceName(r.Stance)}» вышел — теперь мир без договоров");
                    r.Stance = Stance.Peace;
                }
                int delta = r.Stance switch
                {
                    Stance.War => -1,
                    Stance.Alliance => 1,
                    Stance.NonAggression => turn % 2 == 0 ? 1 : 0,
                    _ => 0,
                };
                delta += _game.TradeOpinion(kv.Key.Item1, kv.Key.Item2);
                r.Value = Math.Max(-100, Math.Min(100, r.Value + delta));
            }
            UpdateCoalition(turn);
            foreach (int m in Coalition) AddOpinion(m, CoalitionTarget, -1);
            if (turn == StartTruceTurns + 1) _game.Report("Стартовое перемирие окончено: войну можно объявить по созревшей претензии");
            Proposals.RemoveAll(p => turn - p.Turn > 2);
            Claims.RemoveAll(c => CityById(c.CityId) == null);
            Changed?.Invoke();
        }
    }
}

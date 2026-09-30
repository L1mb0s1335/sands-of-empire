using Runeterra.Tech;

namespace Runeterra.Core
{
    /// <summary>Главная стратегическая цель правителя.</summary>
    public enum AiGoal
    {
        /// <summary>Завоевать город-цель (по исторической претензии).</summary>
        Conquest,
        /// <summary>Богатство: рынки, порты, торговые соглашения.</summary>
        Wealth,
        /// <summary>Знание: исследования, библиотеки.</summary>
        Knowledge,
        /// <summary>Рост: новые города и население.</summary>
        Growth,
        /// <summary>Оборона: крепкие гарнизоны, пакты, союзы.</summary>
        Defense,
    }

    /// <summary>
    /// Характер ИИ страны 1187 года: черты 0…1 и стратегическая цель. Черты влияют на то,
    /// когда объявлять войну, создавать претензии, заключать мир, пакты, союзы и торговые соглашения,
    /// сколько держать войска и что строить и изучать.
    /// </summary>
    public class AiPersonality
    {
        /// <summary>Агрессия: готовность воевать при меньшем перевесе и раньше.</summary>
        public float Aggression;
        /// <summary>Торговля: соглашения, сделки, терпимость к чужим.</summary>
        public float Trade;
        /// <summary>Развитие: постройки и знания вместо войска.</summary>
        public float Development;
        /// <summary>Набожность: тянется к единоверцам и холоден к иноверцам.</summary>
        public float Piety;
        /// <summary>Доверие: держит слово; с низким доверием рвёт пакты ради войны.</summary>
        public float Trust;
        /// <summary>Осторожность: большие гарнизоны, мир при первых потерях.</summary>
        public float Caution;

        public AiGoal Goal;
        /// <summary>Город-цель для Conquest (id).</summary>
        public string GoalCity;
        /// <summary>Ветвь развития, которую правитель изучает в первую очередь.</summary>
        public TechBranch FavoriteBranch;
        /// <summary>Краткое описание характера для окна «Дипломатия».</summary>
        public string Summary;

        /// <summary>Во сколько раз нужно превосходить врага (с его союзниками), чтобы объявить войну.</summary>
        public float WarRatio => 1.45f - 0.6f * Aggression + 0.35f * Caution;

        /// <summary>Раньше этого хода войну не начинает (стартовое перемирие + выжидание).</summary>
        public int EarliestWar => Diplomacy.StartTruceTurns + 2 + (int)((1f - Aggression) * 30f);

        /// <summary>Шанс за ход заявить новую претензию.</summary>
        public float ClaimChance => 0.02f + 0.18f * Aggression;

        public static string GoalName(AiGoal g) => g switch
        {
            AiGoal.Conquest => "завоевание",
            AiGoal.Wealth => "богатство",
            AiGoal.Knowledge => "знание",
            AiGoal.Growth => "рост",
            _ => "оборона",
        };

        public static AiPersonality For(string regionId) => regionId switch
        {
            "palestine" => new AiPersonality
            {
                Aggression = 0.6f, Trade = 0.5f, Development = 0.6f, Piety = 0.9f, Trust = 0.8f, Caution = 0.4f,
                Goal = AiGoal.Conquest, GoalCity = "acre", FavoriteBranch = TechBranch.Governance,
                Summary = "благородный воитель: держит слово, но стремится изгнать латинян",
            },
            "jerusalem_kingdom" => new AiPersonality
            {
                Aggression = 0.9f, Trade = 0.6f, Development = 0.3f, Piety = 0.8f, Trust = 0.4f, Caution = 0.2f,
                Goal = AiGoal.Conquest, GoalCity = "jerusalem", FavoriteBranch = TechBranch.War,
                Summary = "дерзкий крестоносец: воюет при малейшей возможности, цель — Иерусалим",
            },
            "byzantium" => new AiPersonality
            {
                Aggression = 0.3f, Trade = 0.9f, Development = 0.6f, Piety = 0.6f, Trust = 0.3f, Caution = 0.8f,
                Goal = AiGoal.Wealth, GoalCity = "konya", FavoriteBranch = TechBranch.Trade,
                Summary = "хитрый дипломат: богатеет торговлей, договоры соблюдает, пока выгодно",
            },
            "rum" => new AiPersonality
            {
                Aggression = 0.7f, Trade = 0.5f, Development = 0.4f, Piety = 0.5f, Trust = 0.5f, Caution = 0.4f,
                Goal = AiGoal.Conquest, GoalCity = "attalia", FavoriteBranch = TechBranch.War,
                Summary = "степной султан: быстрая конница и выход к морю через Атталию",
            },
            "abbasids" => new AiPersonality
            {
                Aggression = 0.15f, Trade = 0.7f, Development = 0.9f, Piety = 1f, Trust = 0.7f, Caution = 0.7f,
                Goal = AiGoal.Knowledge, FavoriteBranch = TechBranch.Knowledge,
                Summary = "халиф-книжник: знания и вера важнее войн",
            },
            "mosul" => new AiPersonality
            {
                Aggression = 0.4f, Trade = 0.5f, Development = 0.5f, Piety = 0.6f, Trust = 0.5f, Caution = 0.9f,
                Goal = AiGoal.Defense, GoalCity = "damascus", FavoriteBranch = TechBranch.Governance,
                Summary = "осторожный атабек: крепкие стены и союзы против сильных соседей",
            },
            _ => new AiPersonality
            {
                Aggression = 0.5f, Trade = 0.5f, Development = 0.5f, Piety = 0.5f, Trust = 0.5f, Caution = 0.5f,
                Goal = AiGoal.Growth, FavoriteBranch = TechBranch.LandAndWater, Summary = "расчётливый правитель",
            },
        };
    }
}

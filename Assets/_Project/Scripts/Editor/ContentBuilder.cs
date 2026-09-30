using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runeterra.Cities;
using Runeterra.Core;
using Runeterra.Economy;
using Runeterra.Units;
using UnityEditor;
using UnityEngine;

namespace Runeterra.EditorTools
{
    /// <summary>
    /// Контент кампании 1187 года: шесть сторон с лидерами, 18 исторических городов,
    /// региональные ресурсы (месторождения), конница и прядильня хлопка.
    /// Меню: Runeterra → Build Content. Повторный запуск обновляет ассеты, не плодя дубликаты.
    /// </summary>
    public static class ContentBuilder
    {
        private const string Data = "Assets/_Project/Data";

        [MenuItem("Runeterra/Build Content")]
        public static void Build()
        {
            var goods = BuildGoods();
            BuildUnits(goods);
            BuildBuildings(goods);
            BuildRegions(goods);
            AssetDatabase.SaveAssets();
            Debug.Log("[Runeterra] Контент 1187 года обновлён: 6 сторон, 18 городов, региональные ресурсы");
        }

        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

        // ---------- Товары ----------

        private static Dictionary<string, GoodData> BuildGoods()
        {
            var goods = new Dictionary<string, GoodData>();
            GoodData Deposit(string file, string id, string name, float price, float spoil, float consumed, Color color, float[] seasons)
            {
                var g = Asset<GoodData>($"{Data}/Goods/Good_{file}.asset");
                g.id = id;
                g.displayName = name;
                g.basePrice = price;
                g.spoilagePerTurn = spoil;
                g.consumedPerPop = consumed;
                g.tier = 0;
                g.sourceTerrain = new List<Runeterra.Map.TerrainType>();
                g.sourceFeatures = new List<Runeterra.Map.TileFeature>();
                g.tilesPerUnit = 1;
                g.seasonMultipliers = seasons;
                g.fromDeposit = true;
                g.depositColor = color;
                EditorUtility.SetDirty(g);
                return goods[id] = g;
            }
            var flat = new[] { 1f, 1f, 1f, 1f };
            Deposit("Horses", "horses", "Кони", 10f, 0f, 0f, new Color(0.55f, 0.36f, 0.20f), flat);
            Deposit("Silk", "silk", "Шёлк и пурпур", 16f, 0f, 0.03f, new Color(0.55f, 0.15f, 0.55f), flat);
            Deposit("Dates", "dates", "Финики и ладан", 6f, 0.03f, 0.05f, new Color(0.85f, 0.55f, 0.15f), new[] { 0.5f, 2f, 1f, 0.5f });
            Deposit("Cotton", "cotton", "Хлопок", 5f, 0.01f, 0f, new Color(0.95f, 0.95f, 0.92f), new[] { 0f, 2.5f, 1f, 0.5f });
            Deposit("Salt", "salt", "Соль", 5f, 0f, 0.04f, new Color(0.85f, 0.88f, 0.95f), flat);
            Deposit("Glass", "glass", "Стекло", 12f, 0f, 0.02f, new Color(0.30f, 0.75f, 0.80f), flat);
            foreach (var id in new[] { "iron", "wood", "yarn", "grain" })
            {
                var g = AssetDatabase.FindAssets("t:GoodData", new[] { $"{Data}/Goods" })
                    .Select(x => Load<GoodData>(AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault(x => x.id == id);
                if (g != null) goods[id] = g;
            }
            return goods;
        }

        // ---------- Юниты и постройки ----------

        private static void BuildUnits(Dictionary<string, GoodData> goods)
        {
            var u = Asset<UnitData>($"{Data}/Units/Unit_Cavalry.asset");
            u.id = "cavalry";
            u.displayName = "Конница";
            u.description = "Быстрый всадник: 3 хода, сила 26. На найм уходят 2 коня со склада города.";
            u.role = UnitRole.Melee;
            u.movement = 3;
            u.strength = 26;
            u.maxHealth = 100;
            u.sightRange = 2;
            u.productionCost = 45;
            u.goldCost = 70;
            u.canCapture = true;
            u.mounted = true;
            u.goodsCost = new List<GoodAmount> { new GoodAmount { good = goods["horses"], amount = 2 } };
            EditorUtility.SetDirty(u);
        }

        private static void BuildBuildings(Dictionary<string, GoodData> goods)
        {
            var b = Asset<BuildingData>($"{Data}/Buildings/Building_CottonSpinnery.asset");
            b.id = "cotton_spinnery";
            b.displayName = "Хлопкопрядильня";
            b.description = "Прядёт пряжу из хлопка — ткань без овечьей шерсти.";
            b.productionCost = 30;
            b.goodsCost = new List<GoodAmount> { new GoodAmount { good = goods["wood"], amount = 6 } };
            b.maxPerCity = 3;
            b.requiredTier = 1;
            b.inputs = new List<GoodAmount> { new GoodAmount { good = goods["cotton"], amount = 2 } };
            b.output = new GoodAmount { good = goods["yarn"], amount = 1 };
            b.batchesPerTurn = 2;
            EditorUtility.SetDirty(b);

            // Лечебнице нужны соль (консервация снадобий) и стекло (сосуды).
            var infirmary = Load<BuildingData>($"{Data}/Buildings/Building_Infirmary.asset");
            if (infirmary != null)
            {
                infirmary.goodsCost.RemoveAll(g => g.good == null || g.good.id == "salt" || g.good.id == "glass");
                infirmary.goodsCost.Add(new GoodAmount { good = goods["salt"], amount = 2 });
                infirmary.goodsCost.Add(new GoodAmount { good = goods["glass"], amount = 1 });
                infirmary.description = "Эпидемии реже на 2%. Нужны соль и стекло.";
                EditorUtility.SetDirty(infirmary);
            }
        }

        // ---------- Стороны и города ----------

        private struct CitySpec
        {
            public string File, Id, Name, Alt, Text;
            public float Lon, Lat;
            public bool Capital;
            public int Pop, Food, Prod, Gold;
        }

        private static CitySpec C(string file, string id, string name, string alt, float lon, float lat, string text,
            bool capital = false, int pop = 1, int food = 2, int prod = 1, int gold = 1) =>
            new CitySpec { File = file, Id = id, Name = name, Alt = alt, Lon = lon, Lat = lat, Text = text, Capital = capital,
                Pop = capital ? 2 : pop, Food = food, Prod = capital ? prod + 1 : prod, Gold = capital ? gold + 1 : gold };

        private static void BuildRegions(Dictionary<string, GoodData> goods)
        {
            var units = new[] { "Warrior", "Scout", "Builder" }.Select(n => Load<UnitData>($"{Data}/Units/Unit_{n}.asset")).ToList();

            Region("Palestine", "palestine", "Палестина", "Айюбиды Салах ад-Дина: Иерусалим, Дамаск и Газа. Оазисы, финики, стекло Хеврона и соль Мёртвого моря.",
                "Салах ад-Дин", LeaderAbility.Mercy, "Милосердие", "юниты лечатся на +10 за ход быстрее",
                new Color(0f, 0.478f, 0.239f), new Color(0.808f, 0.067f, 0.149f), UnitStyle.Saracen,
                new[] { "dates", "glass", "salt" }, new[] { "Наблус", "Рамла", "Бейсан", "Тверия", "Иерихон", "Керак" },
                goods, units,
                C("Jerusalem", "jerusalem", "Иерусалим", "Аль-Кудс", 35.23f, 31.78f, "Столица. Святой город трёх религий и центр паломничества.", capital: true),
                C("Damascus", "damascus", "Дамаск", "Димашк", 36.29f, 33.51f, "Город садов Гуты и оружейников, опора Айюбидов.", gold: 2),
                C("Gaza", "gaza", "Газа", "", 34.47f, 31.50f, "Ворота в Египет на караванном пути через Синай."));

            Region("JerusalemKingdom", "jerusalem_kingdom", "Крестоносцы", "Латинские государства Востока: Акра, Бейрут и Тортоса. Сильны в атаке, торгуют шёлком, пурпуром и стеклом.",
                "Ричард Львиное Сердце", LeaderAbility.Lionheart, "Львиное сердце", "юниты +3 к силе при атаке",
                new Color(0.93f, 0.91f, 0.86f), new Color(0.85f, 0.66f, 0.18f), UnitStyle.Crusader,
                new[] { "silk", "glass" }, new[] { "Тир", "Сидон", "Кесария", "Яффа", "Аскалон", "Маркаб" },
                goods, units,
                C("Acre", "acre", "Акра", "Акко", 35.07f, 32.93f, "Столица. Портовая крепость, главные ворота крестоносцев.", capital: true),
                C("Beirut", "beirut", "Бейрут", "", 35.50f, 33.89f, "Порт у подножия Ливана, славен пурпуром и шёлком."),
                C("Tortosa", "tortosa", "Тортоса", "Тартус", 35.88f, 34.89f, "Твердыня тамплиеров на побережье."));

            Region("Byzantium", "byzantium", "Византия", "Ромейская держава Исаака II Ангела: Антиохия, Атталия и Селевкия. Богатая казна, шёлк и соль.",
                "Исаак II Ангел", LeaderAbility.ImperialTreasury, "Имперская казна", "+1 золота за ход с каждого города",
                new Color(0.45f, 0.12f, 0.45f), new Color(0.90f, 0.72f, 0.20f), UnitStyle.Crusader,
                new[] { "silk", "salt" }, new[] { "Тарс", "Адана", "Мопсуестия", "Аланья", "Анамур", "Мира" },
                goods, units,
                C("Antioch", "antioch", "Антиохия", "Антакья", 36.16f, 36.20f, "Столица. Великий город на Оронте, ткацкие мастерские шёлка.", capital: true),
                C("Attalia", "attalia", "Атталия", "Анталья", 30.71f, 36.89f, "Гавань Памфилии, морские ворота империи."),
                C("Seleucia", "seleucia", "Селевкия", "Силифке", 33.93f, 36.38f, "Крепость в устье Каликадна, стережёт киликийский берег."));

            Region("Rum", "rum", "Султанат Рум", "Сельджуки Кылыч-Арслана II на Анатолийском плато: Конья, Кайсери и Аксарай. Степные кони и соль озера Туз.",
                "Кылыч-Арслан II", LeaderAbility.SteppeRiders, "Степные всадники", "конные юниты +1 к движению",
                new Color(0.12f, 0.45f, 0.72f), new Color(0.75f, 0.15f, 0.10f), UnitStyle.Saracen,
                new[] { "horses", "salt" }, new[] { "Сивас", "Малатья", "Никде", "Караман", "Акшехир", "Эрменек" },
                goods, units,
                C("Konya", "konya", "Конья", "Икониум", 32.48f, 37.87f, "Столица. Город караван-сараев на Анатолийском плато.", capital: true),
                C("Kayseri", "kayseri", "Кайсери", "Кесария Каппадокийская", 35.48f, 38.72f, "Торговый перекрёсток у горы Эрджиес.", gold: 2),
                C("Aksaray", "aksaray", "Аксарай", "", 34.03f, 38.37f, "Степной город, где пасут табуны султана."));

            Region("Abbasids", "abbasids", "Аббасидский халифат", "Халиф ан-Насир в Багдаде: Багдад, Самарра и Куфа. Пальмовые рощи, хлопок и Дом мудрости.",
                "ан-Насир", LeaderAbility.HouseOfWisdom, "Дом мудрости", "+25% знаний",
                new Color(0.12f, 0.12f, 0.14f), new Color(0.85f, 0.70f, 0.30f), UnitStyle.Saracen,
                new[] { "dates", "cotton" }, new[] { "Васит", "Хилла", "Анбар", "Бакуба", "Тикрит", "Хит" },
                goods, units,
                C("Baghdad", "baghdad", "Багдад", "Мадинат ас-Салам", 44.36f, 33.31f, "Столица. Круглый город халифов на Тигре.", capital: true, food: 3),
                C("Samarra", "samarra", "Самарра", "", 43.88f, 34.20f, "Бывшая столица с великой спиральной мечетью."),
                C("Kufa", "kufa", "Куфа", "", 44.40f, 32.03f, "Город у Евфрата на краю пустыни, край фиников."));

            Region("Mosul", "mosul", "Зангиды Мосула", "Изз ад-Дин Масуд в Мосуле: Мосул, Синджар и Джазира. Муслин из хлопка, кони Джазиры и крепкие стены.",
                "Изз ад-Дин Масуд", LeaderAbility.MosulCitadel, "Цитадель Мосула", "города +5 к силе",
                new Color(0.78f, 0.33f, 0.10f), new Color(0.95f, 0.85f, 0.60f), UnitStyle.Saracen,
                new[] { "cotton", "horses" }, new[] { "Нисибин", "Мардин", "Эрбиль", "Дохук", "Амадия", "Харран" },
                goods, units,
                C("Mosul", "mosul", "Мосул", "аль-Мавсиль", 43.13f, 36.34f, "Столица. Город муслина на Тигре напротив Ниневии.", capital: true),
                C("Sinjar", "sinjar", "Синджар", "", 41.87f, 36.32f, "Крепость у одноимённого хребта посреди Джазиры."),
                C("Jazira", "jazira", "Джазира", "Джазират ибн Умар", 42.19f, 37.33f, "Город на острове Тигра у гор Курдистана."));

            // Хеврон заменён Газой (слишком близко к Иерусалиму на новой карте).
            AssetDatabase.DeleteAsset($"{Data}/Regions/Palestine/City_AlKhalil.asset");
        }

        private static void Region(string folder, string id, string name, string description, string leader, LeaderAbility ability,
            string abilityName, string abilityText, Color primary, Color secondary, UnitStyle style, string[] specialties, string[] cityNames,
            Dictionary<string, GoodData> goods, List<UnitData> units, params CitySpec[] cities)
        {
            string dir = $"{Data}/Regions/{folder}";
            var r = Asset<RegionData>($"{dir}/Region_{folder}.asset");
            r.id = id;
            r.displayName = name;
            r.description = description;
            r.leaderName = leader;
            r.leaderAbility = ability;
            r.leaderAbilityName = abilityName;
            r.leaderAbilityText = abilityText;
            r.primaryColor = primary;
            r.secondaryColor = secondary;
            r.unitStyle = style;
            r.startingUnits = units.Where(u => u != null).ToList();
            r.startingGold = 20;
            r.cityNames = cityNames.ToList();
            r.specialties = specialties.Select(s => goods[s]).ToList();
            r.cities = new List<CityData>();
            foreach (var spec in cities)
            {
                var c = Asset<CityData>($"{dir}/City_{spec.File}.asset");
                c.id = spec.Id;
                c.displayName = spec.Name;
                c.altName = spec.Alt;
                c.description = spec.Text;
                c.isCapital = spec.Capital;
                c.startOffset = default;
                c.lon = spec.Lon;
                c.lat = spec.Lat;
                c.startingPopulation = spec.Pop;
                c.food = spec.Food;
                c.production = spec.Prod;
                c.gold = spec.Gold;
                c.science = 1;
                c.culture = 1;
                c.faith = spec.Capital ? 2 : 1;
                EditorUtility.SetDirty(c);
                r.cities.Add(c);
            }
            EditorUtility.SetDirty(r);
        }
    }
}

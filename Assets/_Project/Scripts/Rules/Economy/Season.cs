namespace Runeterra.Economy
{
    /// <summary>Сезоны года: 4 хода = 1 год.</summary>
    public enum Season
    {
        Sowing,
        Harvest,
        Drought,
        Trade,
    }

    public static class Seasons
    {
        public const int TurnsPerYear = 4;

        public static Season Of(int turn) => (Season)((turn - 1) % TurnsPerYear);
        public static int Year(int turn) => (turn - 1) / TurnsPerYear + 1;

        public static string Name(Season s) => s switch
        {
            Season.Sowing => "Посев",
            Season.Harvest => "Урожай",
            Season.Drought => "Засуха",
            _ => "Торговый сезон",
        };

        public static string Hint(Season s) => s switch
        {
            Season.Sowing => "стригут шерсть, зерно не растёт",
            Season.Harvest => "собирают зерно — главный урожай года",
            Season.Drought => "города проедают зерно со склада, нет тростника",
            _ => "немного зерна, караваны ходят чаще",
        };
    }
}

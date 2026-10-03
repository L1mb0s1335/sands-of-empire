namespace Runeterra.Cities
{
    /// <summary>Ступень поселения. Открывает постройки и приносит свои проблемы.</summary>
    public enum SettlementTier
    {
        Hamlet,
        Village,
        Town,
        City,
        TradeCapital,
    }

    public static class SettlementTiers
    {
        public static string Name(SettlementTier t) => t switch
        {
            SettlementTier.Hamlet => "Хутор",
            SettlementTier.Village => "Деревня",
            SettlementTier.Town => "Городок",
            SettlementTier.City => "Город",
            _ => "Столица торговли",
        };

        public static string Name(int t) => Name((SettlementTier)t);
    }
}

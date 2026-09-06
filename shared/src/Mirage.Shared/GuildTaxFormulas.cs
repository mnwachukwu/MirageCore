namespace Mirage.Shared;

/// <summary>Pure guild-tax math, SHARED by the server settlement (<c>GuildScheduleSystem</c>) and the client's
/// Vault dashboard — so the "what to expect" figure the panel shows is computed the exact same way the 00:00
/// settlement charges it (no drift). All bounds are named <see cref="Constants"/> (playtest-tunable).</summary>
public static class GuildTaxFormulas
{
    /// <summary>Base weekly tax for a guild <paramref name="level"/>: level x
    /// <see cref="Constants.GuildTaxPerLevel"/> (L0 = 0, free — and no perks either).</summary>
    public static long WeeklyTax(int level) => (long)Math.Max(0, level) * Constants.GuildTaxPerLevel;

    /// <summary>How much vault valor offsets a <paramref name="tax"/> bill: every
    /// <see cref="Constants.GuildValorPerTaxDiscount"/> valor removes <see cref="Constants.GuildGoldPerTaxDiscount"/>
    /// gold, in whole increments, capped at <see cref="Constants.GuildValorTaxOffsetCapPercent"/>% of the tax.
    /// Returns the valor spent and the gold discount it buys (both 0 if there's no valor or no tax).</summary>
    public static (int ValorSpent, long GoldDiscount) ValorTaxOffset(int vaultValor, long tax)
    {
        if (vaultValor <= 0 || tax <= 0) return (0, 0);
        long maxDiscount = tax * Constants.GuildValorTaxOffsetCapPercent / 100;
        long chunks = Math.Min(vaultValor / Constants.GuildValorPerTaxDiscount, maxDiscount / Constants.GuildGoldPerTaxDiscount);
        return ((int)(chunks * Constants.GuildValorPerTaxDiscount), chunks * Constants.GuildGoldPerTaxDiscount);
    }

    /// <summary>The gold actually owed at the next settlement after the vault-valor offset — the base
    /// <see cref="WeeklyTax"/> minus the <see cref="ValorTaxOffset"/> discount for the current vault valor.</summary>
    public static long EffectiveTax(int level, int vaultValor)
    {
        long tax = WeeklyTax(level);
        return tax - ValorTaxOffset(vaultValor, tax).GoldDiscount;
    }
}

namespace Mirage.Shared;

/// <summary>Small shared predicates describing which weathers gate which effects, so the server's
/// effect sites stay in lockstep. Pure — reads only <see cref="Constants"/> and <see cref="WeatherType"/>.</summary>
public static class WeatherEffects
{
    /// <summary>Heat Wave and Snow both halve vital regen.</summary>
    public static bool ReducesRegen(WeatherType weather) =>
        weather is WeatherType.HeatWave or WeatherType.Snow;

    /// <summary>The vital-regen magnitude multiplier for the given weather (0.5 for Heat Wave / Snow,
    /// else 1.0). Fold into the regen formula BEFORE its round/floor.</summary>
    public static double RegenMultiplier(WeatherType weather) =>
        ReducesRegen(weather) ? Constants.WeatherReducedRegenMultiplier : 1.0;

    /// <summary>Percent chance the weather tears an attack or a cast off course before it reaches anyone
    /// (Heavy Wind only; 0 elsewhere).
    ///
    /// <para>A miss belongs to the ATTACKER, not the defender: it is rolled on the attacker's map and
    /// resolved before any block or dodge, so a blown swing costs the target no stamina — there was
    /// nothing there to block. That also keeps it independent of the proc cascade, which Heavy Wind
    /// disables outright.</para></summary>
    public static int MissChancePercent(WeatherType weather) =>
        weather == WeatherType.HeavyWind ? Constants.WeatherHeavyWindMissChancePercent : 0;
}

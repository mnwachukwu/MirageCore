using System.Runtime.CompilerServices;

namespace Mirage.Shared.Localization;

/// <summary>Binds the ambient name tokens to the reserved item slots this game charges and pays in, so
/// every localized line calls each of them whatever its item record is called.
///
/// <para>The one place that says which slots this game reserves. <see cref="StringLoader"/> itself knows
/// nothing about them — it holds whatever it is handed — which is what keeps that file identical across
/// MirageCore, MirageSourceRemastered and PokéStory, where the reserved set is different.</para></summary>
public static class ItemNameTokens
{
    public const string Currency = "Currency";

    /// <summary>Registers the tokens against their fallbacks as the assembly loads, so a host that never
    /// binds a world — a test, an editor with nothing open — still prints a noun where one belongs
    /// rather than throwing on an unresolved placeholder.</summary>
    // CA2255 warns off module initializers in a library, on the grounds that a consumer cannot see or
    // order the side effect. It is the right tool here and the objection does not apply: the side effect
    // is two dictionary entries in this assembly's own state, every consumer is in this repository, and
    // the alternative is every test fixture remembering to register before it formats a string.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterFallbacks() => Bind(_ => null);

    /// <summary>Points the tokens at a world's item table. <paramref name="itemName"/> takes an item
    /// index and returns that record's name, or null where the table has not loaded — each host reaches
    /// its own table differently, and none of them may be read at bind time.</summary>
    public static void Bind(Func<int, string?> itemName)
    {
        StringLoader.SetItemNameToken(Currency, Constants.DefaultCurrencyName, () => itemName(Constants.GoldItemIndex));
    }
}

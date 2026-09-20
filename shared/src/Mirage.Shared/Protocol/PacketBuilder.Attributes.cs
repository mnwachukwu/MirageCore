using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Shared.Protocol;

/// <summary>
/// Turning an <see cref="AttributeBag"/> into what one viewer is allowed to be told about it.
///
/// <para><b>The filter is here, on the sending side, and nowhere else.</b> A client that received
/// everything and hid some of it would be handing a determined player the hidden half; there is no
/// version of "the client filters it" that is not a leak. So a value a viewer may not see is never
/// written to their socket, and the client has no filtering code at all.</para>
/// </summary>
public static partial class PacketBuilder
{
    /// <summary>The key numbering a client needs before it can read a single sync. Sent once.</summary>
    public static AttributeSchemaPacket AttributeSchema(AttributeSchema schema) =>
        new()
        {
            Keys = [.. schema.Declarations.Select(d =>
                new AttributeSchemaPacket.Row(d.Ordinal, d.Key, d.Visibility, d.LabelKey))],
        };

    /// <summary>The screens this game paints. The declaration travels whole: it is already nothing but
    /// names and numbers.</summary>
    public static GamePanelsPacket GamePanels(GamePanels panels) => new() { Panels = panels.All };

    /// <summary>What this game lets the player do. Sent once, like the rest of what a client has to be
    /// told before it can draw a game it was not compiled against.</summary>
    public static GameActionsPacket GameActions(GameActions actions) =>
        new()
        {
            Actions = [.. actions.All.Select(a => new GameActionsPacket.Row(
                a.Id, a.LabelKey, a.Surface, a.GroupKey, a.OpensPanel, a.Key, a.When, a.Icon,
                a.Interacts, a.Aimed, a.Unmet, a.Hotkeyable))],
        };

    /// <summary>What each surface shows about a body, in draw order. Sent once, beside the numbering:
    /// a field naming a key the client has no declaration for could never fill.</summary>
    public static DisplayFieldsPacket DisplayFields(DisplayFieldSet fields) =>
        new()
        {
            Fields = [.. fields.Fields.Select(f => new DisplayFieldsPacket.Row(
                f.Surface, f.ValueKey, f.MaxKey, f.LabelKey, f.Rgb, f.Style))],
        };

    /// <summary>Which attributes this game draws over a head, in draw order. Sent once, beside the
    /// numbering — a bar naming a key the client has no declaration for could never fill.</summary>
    public static OverheadBarsPacket OverheadBars(OverheadBarSet bars) =>
        new()
        {
            Bars = [.. bars.Bars.Select(b => new OverheadBarsPacket.Row(b.ValueKey, b.MaxKey, b.Rgb))],
        };

    /// <summary>Which attributes color a creature's name, in the order they are asked, and what a
    /// creature matching none of them is named in. Sent once, beside the numbering.</summary>
    /// <summary>The tags a guild may wear in this world.</summary>
    public static GuildLabelsPacket GuildLabels(GuildLabelSet labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return new GuildLabelsPacket
        {
            Labels = [.. labels.Labels.Select(l => new GuildLabelsPacket.Row(l.Key, l.LabelKey))],
        };
    }

    /// <summary>What this game charges for what the engine offers.</summary>
    public static GamePricesPacket Prices(GamePrices prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        return new GamePricesPacket
        {
            GuildCost = prices.GuildCost,
            InnSpawnCost = prices.InnSpawnCost,
            MailBaseCost = prices.MailBaseCost,
            MailAttachmentCost = prices.MailAttachmentCost,
            MailValuePercent = prices.MailValuePercent,
            MarketTaxPercent = prices.MarketTaxPercent,
            SellBackPercent = prices.SellBackPercent,
            RepairPercent = prices.RepairPercent,
            HomeCooldownSeconds = prices.HomeCooldownSeconds,
        };
    }

    public static NameTintsPacket NameTints(NameTintSet tints) =>
        new()
        {
            Tints = [.. tints.Tints.Select(t => new NameTintsPacket.Row(t.Key, t.Rgb))],
            OtherwiseRgb = tints.OtherwiseRgb,
            MarkedRgb = tints.MarkedRgb,
            AggressorRgb = tints.AggressorRgb,
        };

    /// <summary>What <paramref name="viewer"/> is told about <paramref name="bag"/>, or null when that
    /// is nothing.
    ///
    /// <para>Null rather than an empty packet on purpose: "no visible attributes" is the ordinary case
    /// — every body in a world whose game declared nothing — and a caller that has to check anyway is
    /// better served by a check that also skips the send.</para></summary>
    /// <param name="who">Whose attributes these are.</param>
    /// <param name="bag">The body's values.</param>
    /// <param name="schema">The loaded game's declarations. <see cref="Extensibility.AttributeSchema.Empty"/>
    /// yields null for every body, as Core alone does.</param>
    /// <param name="viewer">How close the receiver stands: <see cref="AttributeVisibility.Owner"/> for
    /// the body's own player, <see cref="AttributeVisibility.Viewport"/> for anyone who can see it.</param>
    /// <param name="keys">Which keys changed, or null for all of them. A changed key the viewer may not
    /// see is dropped here, so a caller never has to ask.</param>
    public static AttributeSyncPacket? AttributeSync(
        EntityHandle who, AttributeBag bag, AttributeSchema schema, AttributeVisibility viewer,
        IReadOnlyCollection<string>? keys = null)
    {
        if (!who.IsSet || bag.IsEmpty || schema.Declarations.Count == 0) return null;

        List<AttributeSyncPacket.Entry>? set = null;
        foreach (string key in keys ?? bag.Keys)
        {
            if (!schema.IsVisibleTo(key, viewer)) continue;
            if (!bag.TryGet(key, out var value)) continue;
            if (!schema.TryGet(key, out var declaration)) continue;
            (set ??= []).Add(new AttributeSyncPacket.Entry(declaration.Ordinal, value));
        }

        if (set is null) return null;
        // Ordinal order, so the same change produces the same bytes whichever order the keys were set in
        // — the property the bag's own writer keeps for a world file, kept here for a captured line.
        set.Sort(static (a, b) => a.Ordinal.CompareTo(b.Ordinal));
        return new AttributeSyncPacket { Who = who, Set = set };
    }
}

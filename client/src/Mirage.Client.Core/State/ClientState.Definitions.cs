using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>The record tables the server pushes once on join: items, NPCs (with each one's keeper-shop
/// kind), shops and spells. All 1-based, so index 0 is an unused dummy.</summary>
public sealed partial class ClientState
{
    public ItemRecord[] Items { get; private set; } = new ItemRecord[RecordLimits.Default.Items + 1];
    public NpcRecord[] NpcDefs { get; private set; } = new NpcRecord[RecordLimits.Default.Npcs + 1];
    // Client-only: NPC template num → keeper-shop KIND (0 none / 1 store / 2 inn; from SendNpcsPacket +
    // UpdateNpcPacket). Drives the $ vendor glyph, the melee-key/right-click interact routing, and the
    // right-click menu label (Shop vs Inn). Parallel to NpcDefs; never persisted.
    public int[] NpcKeeperShop { get; private set; } = new int[RecordLimits.Default.Npcs + 1];
    public ShopRecord[] ShopDefs { get; private set; } = new ShopRecord[RecordLimits.Default.Shops + 1];
    public SpellRecord[] SpellDefs { get; private set; } = new SpellRecord[RecordLimits.Default.Spells + 1];
}

using Mirage.Shared.Extensibility;

namespace Mirage.Client.Core.State;

/// <summary>
/// Where a character may wear something in the game this client is connected to.
///
/// <para><b>Learned, never assumed.</b> The slots are the server's game's, so the client is told them on
/// connect and draws whatever it was told — two slots or twenty. Empty until the list arrives, and empty
/// for good in a world whose game declared none, where nothing can be worn at all.</para>
/// </summary>
public sealed partial class ClientState
{
    public EquipSlotSet EquipSlots { get; set; } = EquipSlotSet.Empty;
}

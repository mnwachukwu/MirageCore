using Mirage.Shared.Extensibility;

namespace Mirage.Client.Core.State;

/// <summary>
/// The loaded game's attribute keys, and whatever values this client has been told about.
///
/// <para><b>Only what the server chose to send is here.</b> The filtering happens before the socket, so
/// this is not a partial copy of the truth with the rest hidden — it is the whole of what this client is
/// entitled to know. A key a game declared <see cref="AttributeVisibility.Owner"/> appears on the local
/// player and on nobody else.</para>
/// </summary>
public sealed partial class ClientState
{
    /// <summary>The key numbering, learned from the server on connect. Empty until it arrives, and
    /// empty for good in a world whose game declared nothing.</summary>
    public AttributeSchema Attributes { get; set; } = AttributeSchema.Empty;

    /// <summary>Bumped whenever any body's attributes change, so a panel can redraw on a change rather
    /// than re-reading every frame. The same pattern as <see cref="QuestVersion"/>.</summary>
    public int AttributeVersion { get; set; }

    private readonly Dictionary<EntityHandle, AttributeBag> _npcAttributes = [];

    /// <summary>The bag a sync should be applied to, or null when this client is not tracking that body.
    ///
    /// <para>A player's bag hangs on the <c>PlayerRecord</c> the client already keeps, so it arrives and
    /// leaves with them. An NPC's is kept here by handle instead: the client's own NPC slots are a view
    /// of one map and are reused as bodies come and go, so values hung on a slot would be inherited by
    /// whatever spawned into it next.</para></summary>
    public AttributeBag? AttributesOf(EntityHandle who)
    {
        if (who.IsPlayer)
        {
            return who.PlayerIndex >= 1 && who.PlayerIndex < Players.Length
                ? Players[who.PlayerIndex].Attributes
                : null;
        }

        if (!who.IsNpc) return null;
        if (!_npcAttributes.TryGetValue(who, out var bag)) _npcAttributes[who] = bag = new AttributeBag();
        return bag;
    }

    /// <summary>Forgets an NPC's values. Called when a body leaves for good rather than when it walks
    /// off the edge of what this client can see — a chaser crossing a seam is the same body, and its
    /// values must not reset because the camera did.</summary>
    public void ForgetAttributes(EntityHandle who) => _npcAttributes.Remove(who);

    /// <summary>Drops every NPC's values. For a map change or a reconnect, where the whole picture is
    /// about to be rebuilt from the server.</summary>
    public void ClearNpcAttributes() => _npcAttributes.Clear();
}

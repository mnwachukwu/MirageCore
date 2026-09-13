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

    /// <summary>Which of those keys are drawn as rows over a body's head, in draw order. Empty until the
    /// server says otherwise, and empty for good in a world whose game declared none.</summary>
    public OverheadBarSet OverheadBars { get; set; } = OverheadBarSet.Empty;

    /// <summary>Bumped whenever any body's attributes change, so a panel can redraw on a change rather
    /// than re-reading every frame.</summary>
    public int AttributeVersion { get; set; }

    private readonly Dictionary<EntityHandle, AttributeBag> _npcAttributes = [];

    /// <summary>What this client has been told about a body, or null when it has been told nothing —
    /// which is every body in a world whose game declares no keys.
    ///
    /// <para>A player's bag hangs on the <c>PlayerRecord</c> the client already keeps, so it arrives and
    /// leaves with them. An NPC's is kept here by handle instead: the client's own NPC slots are a view
    /// of one map and are reused as bodies come and go, so values hung on a slot would be inherited by
    /// whatever spawned into it next.</para>
    ///
    /// <para><b>Reading never creates.</b> This is asked once per visible body per frame by the thing
    /// that draws overhead bars; a get-or-create here would put an empty bag behind every NPC that ever
    /// crossed the screen and keep it forever.</para></summary>
    public AttributeBag? AttributesOf(EntityHandle who)
    {
        if (who.IsPlayer)
        {
            return who.PlayerIndex >= 1 && who.PlayerIndex < Players.Length
                ? Players[who.PlayerIndex].Attributes
                : null;
        }

        return who.IsNpc && _npcAttributes.TryGetValue(who, out var bag) ? bag : null;
    }

    /// <summary>The bag a sync should be written into, made if this is the first value for that body,
    /// or null for a body this client cannot hold values for at all.</summary>
    public AttributeBag? BagFor(EntityHandle who)
    {
        if (who.IsPlayer) return AttributesOf(who);
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

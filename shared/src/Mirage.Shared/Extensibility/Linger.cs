namespace Mirage.Shared.Extensibility;

/// <summary>
/// What a game says about a player whose connection goes while they are still in the world.
///
/// <para><b>Core knows how to leave a body behind; it does not know when that is fair.</b> Holding a
/// disconnected player in play — their sprite on the map, their slot occupied, their session reclaimable
/// if they get back in time — is engine work: it is the roster, the viewport and the save path every
/// other departure goes through. WHETHER to is a rule. Logging out mid-fight, mid-auction, mid-raid or
/// mid-conversation are four games' answers to the same moment, and none of them is in here.</para>
///
/// <para><b>The default is no ghost at all</b>, so an engine with no game loaded takes a dropped player
/// straight out of the world — which is the honest behavior for a world with nothing worth staying
/// for.</para>
/// </summary>
public interface ILingerPolicy
{
    /// <summary>How long <paramref name="who"/>'s body stays in the world after the connection drops.
    ///
    /// <para><see cref="Deadline.None"/> — the default — takes them out at once. The first policy naming
    /// a deadline wins, the same rule <see cref="IDeathPolicy.RespawnFor"/> uses, because two games
    /// disagreeing about how long a body lingers is a question with one answer and no way to average
    /// it.</para>
    ///
    /// <para>Asked while the player is still in the world, so a policy may read anything about them.</para></summary>
    Deadline LingerFor(EntityHandle who) => Deadline.None;
}

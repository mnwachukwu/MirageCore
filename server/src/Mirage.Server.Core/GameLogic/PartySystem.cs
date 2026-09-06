using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.GameLogic;

public sealed class PartySystem : GameSystem
{
    private readonly PlayerManager _pm;

    // Pending party invites expire after this long if not accepted.
    private const long InviteTimeoutMs = 60_000;

    public PartySystem(PlayerManager pm, IPacketDispatcher dispatcher) : base(dispatcher)
    {
        _pm = pm;
    }

    /// <summary>
    /// Pushes a fresh <see cref="PartyVitalsPacket"/> snapshot of <paramref name="playerIndex"/>
    /// to that player's partner so their party overlay stays current.  No-op when the player has
    /// no partner.  Call after any mutation that changes the partner's overlay-visible state:
    /// HP/MP/SP, level, map/X/Y, combat stamp.
    /// </summary>
    public void NotifyPartner(int playerIndex)
    {
        if (!_pm[playerIndex].InParty) return;
        int partner = _pm[playerIndex].PartyPlayer;
        if (partner == 0 || !_pm[partner].IsPlaying) return;
        var sp = _pm[playerIndex];
        _dispatcher.SendTo(partner, PacketBuilder.PartyVitals(
            playerIndex, sp.Char, sp.CombatExpiresAt, sp.PkGraceUntilUtc,
            Environment.TickCount64, CombatSystem.CombatDurationMs));
    }

    private void ClearPartnerOverlay(int partnerIndex)
    {
        if (partnerIndex > 0 && _pm[partnerIndex].IsPlaying)
            _dispatcher.SendTo(partnerIndex, PacketBuilder.PartyCleared());
    }

    /// <summary>Player attacker sends a party request to targetName.</summary>
    public void SendPartyRequest(int attackerIndex, string targetName)
    {
        if (!_pm[attackerIndex].IsPlaying) return;

        int target = _pm.FindPlayerByName(targetName);
        if (target == 0 || !_pm[target].IsPlaying)
        {
            SendMsg(attackerIndex, ServerStrings.PartySystem_TargetNotOnline, GameColor.White);
            return;
        }
        if (_pm[attackerIndex].Char.Access > AdminLevel.Monitor)
        {
            SendMsg(attackerIndex, ServerStrings.PartySystem_AdminCannotParty, GameColor.BrightBlue);
            return;
        }
        if (_pm[target].Char.Access > AdminLevel.Monitor)
        {
            SendMsg(attackerIndex, ServerStrings.PartySystem_TargetIsAdmin, GameColor.BrightBlue);
            return;
        }
        if (_pm[attackerIndex].InParty)
        {
            SendMsg(attackerIndex, ServerStrings.PartySystem_AlreadyInParty, GameColor.Pink);
            return;
        }
        if (_pm[target].InParty)
        {
            SendMsg(attackerIndex, ServerStrings.PartySystem_TargetAlreadyInParty, GameColor.Pink);
            return;
        }

        // If the named target already has a pending invite out to me, treat this as an
        // acceptance rather than overwriting both sides' state. Resolves the symmetric
        // case where two players /join each other's names — without this, both end up
        // flagged as PartyStarter and neither can complete the join.
        if (_pm[target].PartyStarter && _pm[target].PartyPlayer == attackerIndex)
        {
            JoinParty(attackerIndex);
            return;
        }

        // Re-inviting while a previous invite is still pending: clear the prior invitee's
        // dangling PartyPlayer back-pointer so they don't accept into a stale slot.
        if (_pm[attackerIndex].PartyStarter)
        {
            int prior = _pm[attackerIndex].PartyPlayer;
            if (prior > 0 && _pm[prior].IsPlaying && !_pm[prior].InParty && _pm[prior].PartyPlayer == attackerIndex)
            {
                _pm[prior].PartyPlayer = 0;
                _pm[prior].PartyStarter = false;
                _pm[prior].PartyInviteExpiresAt = 0;
            }
        }

        _pm[attackerIndex].PartyStarter = true;
        _pm[attackerIndex].PartyPlayer = target;
        _pm[target].PartyPlayer = attackerIndex;
        _pm[attackerIndex].PartyInviteExpiresAt = Environment.TickCount64 + InviteTimeoutMs;

        string fromName = _pm[attackerIndex].Char.TrimmedName;
        _dispatcher.SendTo(target, new PartyRequestNotifyPacket
        {
            FromName = fromName,
            FromIndex = attackerIndex
        });
        SendMsg(target, ServerStrings.PartySystem_InviteReceived, GameColor.Pink, ("FromName", fromName));
        SendMsg(attackerIndex, ServerStrings.PartySystem_InviteSent, GameColor.Pink, ("TargetName", targetName.TrimEnd()));
    }

    /// <summary>Player requesterIndex accepts the pending party invite.</summary>
    public void JoinParty(int requesterIndex)
    {
        if (!_pm[requesterIndex].IsPlaying) return;

        int acceptorIndex = _pm[requesterIndex].PartyPlayer;
        if (acceptorIndex == 0)
        {
            SendMsg(requesterIndex, ServerStrings.PartySystem_NoInvite, GameColor.Pink);
            return;
        }
        if (!_pm[acceptorIndex].IsPlaying)
        {
            SendMsg(requesterIndex, ServerStrings.PartySystem_NoInvite, GameColor.Pink);
            return;
        }
        if (_pm[requesterIndex].PartyStarter)
        {
            SendMsg(requesterIndex, ServerStrings.PartySystem_NoInvitePending, GameColor.Pink);
            return;
        }
        if (_pm[acceptorIndex].PartyPlayer != requesterIndex)
        {
            SendMsg(requesterIndex, ServerStrings.PartySystem_Failed, GameColor.Pink);
            return;
        }
        if (_pm[requesterIndex].InParty || _pm[acceptorIndex].InParty) return;

        _pm[requesterIndex].InParty = true;
        _pm[acceptorIndex].InParty = true;
        _pm[acceptorIndex].PartyStarter = false;
        _pm[acceptorIndex].PartyInviteExpiresAt = 0;

        string n1 = _pm[acceptorIndex].Char.TrimmedName;
        string n2 = _pm[requesterIndex].Char.TrimmedName;

        SendMsg(requesterIndex, ServerStrings.PartySystem_YouJoined, GameColor.Pink, ("PartnerName", n1));
        SendMsg(acceptorIndex, ServerStrings.PartySystem_TheyJoined, GameColor.Pink, ("JoinerName", n2));

        // Initial overlay snapshot in both directions — each side learns its partner's identity + vitals.
        NotifyPartner(requesterIndex);
        NotifyPartner(acceptorIndex);
    }

    /// <summary>Player at index leaves or declines a party.</summary>
    public void LeaveParty(int index)
    {
        if (!_pm[index].IsPlaying) return;

        int partner = _pm[index].PartyPlayer;
        if (partner == 0)
        {
            SendMsg(index, ServerStrings.PartySystem_NotInParty, GameColor.Pink);
            return;
        }

        string name = _pm[index].Char.TrimmedName;
        bool wasInParty = _pm[index].InParty;

        _pm[index].InParty = false;
        _pm[index].PartyPlayer = 0;
        _pm[index].PartyStarter = false;
        _pm[index].PartyInviteExpiresAt = 0;

        if (wasInParty)
        {
            SendMsg(index, ServerStrings.PartySystem_YouLeft, GameColor.Pink);
            if (_pm[partner].IsPlaying)
                SendMsg(partner, ServerStrings.PartySystem_TheyLeft, GameColor.Pink, ("LeaverName", name));
            // Tear down the overlay on both sides — the leaver loses their partner immediately;
            // the partner is cleared a moment later when their state is reset below.
            ClearPartnerOverlay(index);
            ClearPartnerOverlay(partner);
        }
        else
        {
            SendMsg(index, ServerStrings.PartySystem_Declined, GameColor.Pink);
            if (_pm[partner].IsPlaying)
                SendMsg(partner, ServerStrings.PartySystem_TheyDeclined, GameColor.Pink, ("DeclinerName", name));
        }

        if (_pm[partner].IsPlaying)
        {
            _pm[partner].InParty = false;
            _pm[partner].PartyPlayer = 0;
            _pm[partner].PartyStarter = false;
            _pm[partner].PartyInviteExpiresAt = 0;
        }
    }

    /// <summary>Disband party on logout — silently clears both sides.</summary>
    public void DisbandParty(int index)
    {
        if (!_pm[index].InParty) return;

        int partner = _pm[index].PartyPlayer;

        _pm[index].InParty = false;
        _pm[index].PartyPlayer = 0;
        _pm[index].PartyStarter = false;
        _pm[index].PartyInviteExpiresAt = 0;

        if (partner > 0 && _pm[partner].IsPlaying)
        {
            _pm[partner].InParty = false;
            _pm[partner].PartyPlayer = 0;
            _pm[partner].PartyStarter = false;
            _pm[partner].PartyInviteExpiresAt = 0;
            SendMsg(partner, ServerStrings.PartySystem_TheyLeft, GameColor.Pink, ("LeaverName", _pm[index].Char.TrimmedName));
            ClearPartnerOverlay(partner);
        }
    }

    /// <summary>
    /// Expires pending party invites that weren't accepted in time, and refreshes each partnered
    /// player's overlay snapshot to their partner.  Runs on the AI cadence (every 500 ms) — fast
    /// enough that vitals + combat-state changes feel immediate against the client's smooth bar
    /// lerp, slow enough that we don't churn the network for an idle party.
    /// </summary>
    public void Tick(long now)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            var sp = _pm[i];
            if (sp.InParty) NotifyPartner(i);
            if (sp.PartyInviteExpiresAt == 0 || now < sp.PartyInviteExpiresAt) continue;
            // Only the inviter carries the timer, and only while still pending (not yet accepted).
            if (!sp.PartyStarter || sp.InParty)
            {
                sp.PartyInviteExpiresAt = 0;
                continue;
            }

            int partner = sp.PartyPlayer;
            string inviterName = sp.IsPlaying ? sp.Char.TrimmedName : "";
            string partnerName = partner > 0 && _pm[partner].IsPlaying
                ? _pm[partner].Char.TrimmedName : "";

            sp.PartyPlayer = 0;
            sp.PartyStarter = false;
            sp.PartyInviteExpiresAt = 0;
            if (sp.IsPlaying)
                SendMsg(i, ServerStrings.PartySystem_InviteExpiredSelf, GameColor.Pink, ("PartnerName", partnerName));

            if (partner > 0 && _pm[partner].IsPlaying && !_pm[partner].InParty)
            {
                _pm[partner].PartyPlayer = 0;
                _pm[partner].PartyStarter = false;
                _pm[partner].PartyInviteExpiresAt = 0;
                SendMsg(partner, ServerStrings.PartySystem_InviteExpiredOther, GameColor.Pink, ("InviterName", inviterName));
            }
        }
    }
}

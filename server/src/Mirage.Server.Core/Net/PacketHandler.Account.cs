using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;

namespace Mirage.Server.Core.Net;

/// <summary>Everything before a character is in the world: account create/delete, password change,
/// login, the character list, and character add/delete/select — including the combat-ghost takeover
/// that lets a player reclaim a character still fighting on a dropped connection.</summary>
public sealed partial class PacketHandler
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  Pre-login handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private void HandleGetClasses(int index, GetClassesPacket _)
    {
        if (_pm[index].IsPlaying) return;

        _dispatcher.SendTo(index, PacketBuilder.NewCharClasses(_world.Classes, _world.Items, _world.Spells));
    }

    private void HandleNewAccount(int index, NewAccountPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.IsConnected && sp.Login != "") return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;

        string name = p.Username.Trim();
        string pass = p.Password;

        // Minimum counts alphanumerics only (so "A__" / all-underscore has too little real content); the
        // maximum length below counts the whole string (underscores included).
        if (NameRules.EffectiveLength(name) < Constants.MinFieldLength || pass.Length < Constants.MinFieldLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_ShortNameAndPass);
            return;
        }

        if (name.Length > Constants.NameLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_NameTooLong, ("Max", Constants.NameLength));
            return;
        }

        if (!IsValidName(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_InvalidName);
            return;
        }

        RunAsync(HandleNewAccountAsync(index, name, pass, p.MachineKey), nameof(HandleNewAccountAsync));
    }

    private async Task HandleNewAccountAsync(int index, string name, string pass, string machineKey)
    {
        try
        {
            // Before the name is even checked: registering again is the hole a machine ban exists to
            // close, so a banned machine must not be able to learn which names are free either.
            if (!await ApplyMachineKeyAsync(index, machineKey, name)) return;

            if (await _persistence.AccountNameTakenAsync(name))
            {
                AlertAndDisconnect(index, ServerStrings.Auth_AccountTaken);
                return;
            }

            // The FIRST account on a fresh server, created from this machine, becomes a Creator. Setting
            // up a world otherwise means shutting the server down and hand-editing a JSON file to reach
            // your own admin tools — a step every operator hits once and nobody should have to.
            //
            // Gated on LOOPBACK, not merely on being first. On a server that is already reachable, the
            // first stranger to find it would otherwise own it.
            bool bootstrap = IsLoopback(_pm[index].RemoteIp) && _persistence.HasNoAccounts();
            await _persistence.CreateAccountAsync(name, pass, bootstrap ? AdminLevel.Creator : AdminLevel.Player);
            if (bootstrap)
            {
                _logger.LogInformation(
                    "Account {Name} is the first on this server and was created locally — granted Creator.", name);
            }
            else
            {
                _logger.LogInformation("Account {Name} has been created.", name);
            }
            AlertAndDisconnect(index, ServerStrings.Auth_AccountCreated, AlertCode.AccountCreated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create account {Name}", name);
            AlertAndDisconnect(index, ServerStrings.Auth_AccountCreateFailed);
        }
    }

    /// <summary>Whether a connection came from this machine. Parsed rather than string-compared: "::1",
    /// "127.0.0.1" and any other 127.x address all mean the same thing, and an unparseable value is
    /// treated as remote.</summary>
    private static bool IsLoopback(string ip) =>
        System.Net.IPAddress.TryParse(ip, out var parsed) && System.Net.IPAddress.IsLoopback(parsed);

    private void HandleDelAccount(int index, DelAccountPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.IsConnected && sp.Login != "") return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;

        RunAsync(HandleDelAccountAsync(index, p.Username.Trim(), p.Password), nameof(HandleDelAccountAsync));
    }

    private async Task HandleDelAccountAsync(int index, string name, string pass)
    {
        if (name.Length < Constants.MinFieldLength || pass.Length < Constants.MinFieldLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_ShortDeleteNameAndPass);
            return;
        }

        if (name.Length > Constants.NameLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_NameTooLong, ("Max", Constants.NameLength));
            return;
        }

        if (!await _persistence.AccountExistsAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_AccountNotFound, AlertCode.AccountNotFound);
            return;
        }

        if (!await _persistence.PasswordOkAsync(name, pass))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_IncorrectPassword, AlertCode.IncorrectPassword);
            return;
        }

        if (await _persistence.IsBannedAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_BannedCannotDelete);
            return;
        }

        if (_pm.IsMultiAccount(name, index))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_AccountLoggedInDelete);
            return;
        }

        await _persistence.DeleteAccountAsync(name);
        _logger.LogInformation("Account {Name} has been deleted.", name);
        AlertAndDisconnect(index, ServerStrings.Auth_AccountDeleted, AlertCode.AccountDeleted);
    }

    private void HandleChangePassword(int index, ChangePasswordPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.IsConnected && sp.Login != "") return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;

        RunAsync(HandleChangePasswordAsync(index, p.Username.Trim(), p.Password, p.NewPassword),
                 nameof(HandleChangePasswordAsync));
    }

    private async Task HandleChangePasswordAsync(int index, string name, string pass, string newPass)
    {
        if (name.Length < Constants.MinFieldLength || pass.Length < Constants.MinFieldLength || newPass.Length < Constants.MinFieldLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_ShortNameOrPass);
            return;
        }

        if (name.Length > Constants.NameLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_NameTooLong, ("Max", Constants.NameLength));
            return;
        }

        if (!await _persistence.AccountExistsAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_AccountNotFound, AlertCode.AccountNotFound);
            return;
        }

        if (!await _persistence.PasswordOkAsync(name, pass))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_IncorrectPassword, AlertCode.IncorrectPassword);
            return;
        }

        if (_pm.IsMultiAccount(name, index))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_AccountLoggedIn);
            return;
        }

        // Safe to write directly (no per-login chain needed): the account is guaranteed offline here
        // - the session isn't in-game (caller's guard) and the IsMultiAccount check above rejects the
        // change if the account is logged in anywhere, so no character save can race this file.
        await _persistence.ChangePasswordAsync(name, newPass);
        _logger.LogInformation("Account {Name} changed their password.", name);
        AlertAndDisconnect(index, ServerStrings.Auth_PasswordChanged, AlertCode.PasswordChanged);
    }

    private void HandleLogin(int index, LoginPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.IsConnected && sp.Login != "") return;
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;

        RunAsync(HandleLoginAsync(index, p), nameof(HandleLoginAsync));
    }

    private async Task HandleLoginAsync(int index, LoginPacket p)
    {
        string name = p.Username.Trim();
        string pass = p.Password;

        // Version check runs before any account lookup. Major/Minor/Revision compare as an ordered tuple
        // (lexicographic), so only a client strictly older than the server's build is turned away.
        if ((p.Major, p.Minor, p.Revision).CompareTo((Constants.ClientMajor, Constants.ClientMinor, Constants.ClientRevision)) < 0)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_ClientOutdated);
            return;
        }

        if (name.Length < Constants.MinFieldLength || pass.Length < Constants.MinFieldLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_ShortNameAndPass);
            return;
        }

        if (name.Length > Constants.NameLength)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_NameTooLong, ("Max", Constants.NameLength));
            return;
        }

        if (!await _persistence.AccountExistsAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_AccountNotFound, AlertCode.AccountNotFound);
            return;
        }

        var account = await _persistence.LoadAccountAsync(name);
        if (account is null)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_LoadFailed);
            return;
        }

        if (!PasswordHasher.Verify(pass, account.Password))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_IncorrectPassword, AlertCode.IncorrectPassword);
            return;
        }

        if (_pm.IsMultiAccount(name, index))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_MultiAccount);
            return;
        }

        // Bans are keyed by ACCOUNT, so this catches every character on it. It does NOT stop the same
        // person registering again — that is what an account-key block is, and pretending otherwise is
        // how the old "covers both login name and IP" comment survived here without an IP ever being read.
        if (await _persistence.IsBannedAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_Banned, ("GameName", _config.GameName));
            return;
        }

        // AFTER the account ban, so somebody banned both ways is refused for the reason that actually
        // needs no explaining, and the machine check only ever speaks about people the account list missed.
        if (!await ApplyMachineKeyAsync(index, p.MachineKey, name)) return;

        long nowUtc = NowUtc;
        if (account.KickedUntilUtc > nowUtc)
        {
            int minsLeft = (int)Math.Max(1, (account.KickedUntilUtc - nowUtc + 59) / 60);
            AlertAndDisconnect(index, ServerStrings.Auth_KickedTryAgain, ("Minutes", minsLeft));
            return;
        }
        if (account.KickedUntilUtc != 0)
            _saver.MutateAccountInBackground(account.Login, a => a.KickedUntilUtc = 0);

        var sp = _pm[index];
        sp.Login = account.Login;
        sp.Password = account.Password;
        sp.MutedUntilUtc = account.MutedUntilUtc;
        sp.Guild = account.Guild;             // per-account guild membership, mirrored for O(1) in-game checks
        sp.GuildRank = account.GuildRank;
        sp.Friends = account.Friends;
        sp.Ignore = account.Ignore;
        sp.Mail = account.Mail;
        sp.Outbox = account.Outbox;
        for (int i = 1; i <= Constants.MaxChars; i++)
        {
            sp.Chars[i] = account.Chars[i];
            sp.Chars[i].Access = account.Access;   // access is per-account — stamp every char's runtime mirror
        }
        sp.Bank = account.Bank;   // account-shared vault; every character on this account draws from it

        _logger.LogInformation("{Name} has logged in.", name);

        // If a combat ghost exists for this account, record it so UseChar can do a takeover.
        int ghostSlot = _pm.FindGhostByLogin(name);
        if (ghostSlot != 0)
            sp.GhostTransferSlot = ghostSlot;

        // Send character list (1-based slots)
        _dispatcher.SendTo(index, PacketBuilder.SendChars(
            Enumerable.Range(1, Constants.MaxChars).Select(i => (PlayerRecord?)sp.Chars[i]),
            _world.Classes));
    }

    private void HandleAddChar(int index, AddCharPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.Login == "") return;

        RunAsync(HandleAddCharAsync(index, p.Name.Trim(), p.Sex, p.Class), nameof(HandleAddCharAsync));
    }

    private async Task HandleAddCharAsync(int index, string name, Sex sex, int classNum)
    {
        // Max counts the whole string; min counts alphanumerics only (rejects "A__" / all-underscore).
        switch (NameRules.CheckLength(name, Constants.MinFieldLength, Constants.NameLength))
        {
            case NameLengthResult.TooShort:
                AlertAndDisconnect(index, ServerStrings.Auth_CharNameTooShort);
                return;
            case NameLengthResult.TooLong:
                AlertAndDisconnect(index, ServerStrings.Auth_CharNameTooLong, ("Max", Constants.NameLength));
                return;
        }

        if (!IsValidName(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_InvalidName);
            return;
        }

        if (sex > Sex.Female)
        {
            HackingAttempt(index, "Invalid Sex");
            return;
        }

        if (!SlotValidation.IsValidClassNum(classNum) || string.IsNullOrEmpty(_world.Classes[classNum].Name))
        {
            HackingAttempt(index, "Invalid Class");
            return;
        }

        var sp = _pm[index];

        // Find first empty 1-based slot
        int slot = 0;
        for (int i = 1; i <= Constants.MaxChars; i++)
        {
            if (string.IsNullOrEmpty(sp.Chars[i].Name.Trim()))
            {
                slot = i;
                break;
            }
        }
        if (slot == 0)
        {
            AlertAndDisconnect(index, ServerStrings.Auth_CharSlotsFull);
            return;
        }

        if (sp.Chars[slot].Name.Trim() != "")
        {
            AlertAndDisconnect(index, ServerStrings.Auth_CharAlreadyExists);
            return;
        }

        if (await _persistence.CharExistsAsync(name))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_CharNameInUse);
            return;
        }

        var cls = _world.Classes[classNum];
        var chr = sp.Chars[slot];
        chr.Name = name;
        chr.Sex = sex;
        chr.Class = classNum;
        // Copied onto the character, not looked up through the class later: re-arting a class must not
        // silently restyle everyone who already plays one.
        chr.Sprite = cls.SpriteFor(sex);
        chr.SpriteSheet = cls.SpriteSheetFor(sex);
        chr.Level = 1;
        chr.Str = cls.Str;
        chr.Def = cls.Def;
        chr.Spd = cls.Spd;
        chr.Int = cls.Int;
        chr.Map = (short)_config.Spawn.Map;
        chr.X = _config.Spawn.X;
        chr.Y = _config.Spawn.Y;
        chr.Hp = StatFormulas.GetPlayerMaxHp(chr, cls);
        chr.Mp = StatFormulas.GetPlayerMaxMp(chr, cls);
        chr.Sp = StatFormulas.GetPlayerMaxSp(chr, cls);
        GrantStartingLoadout(chr, cls);

        // Persist the new character through the per-login chain (a load-merge): it can't race a
        // concurrent write, and the account's other chars, bank, penalty timers, and guild fields are
        // all preserved from disk. Snapshot the char detached, per the saver contract.
        var newChar = chr.Clone();
        _saver.MutateAccountInBackground(sp.Login, a => a.Chars[slot] = newChar);
        await _persistence.AddCharNameAsync(name);
        _logger.LogInformation("Character {Name} added to {Login}'s account.", name, sp.Login);
        _dispatcher.SendTo(index, PacketBuilder.SendChars(
            Enumerable.Range(1, Constants.MaxChars).Select(i => (PlayerRecord?)sp.Chars[i]),
            _world.Classes));
    }

    /// <summary>Fill a brand-new character's bag and spellbook from its class's authored loadout.
    ///
    /// <para>Which lines survive the gates is <see cref="StartingLoadout"/>'s call, not this method's —
    /// the character-create screen previews the same answer, and the two must not be able to disagree.
    /// This half is only the application: put the granted items in the bag, wear the wearable ones, and
    /// write the spells into the book.</para></summary>
    private void GrantStartingLoadout(PlayerRecord chr, ClassRecord cls)
    {
        foreach (var g in StartingLoadout.ResolveItems(cls, chr.Class, _world.Items))
        {
            chr.Inv[g.Slot].Num = g.Num;
            chr.Inv[g.Slot].Quantity = g.Value;
            chr.Inv[g.Slot].Dur = g.Durability;   // starts pristine

            if (!g.Worn) continue;
            switch (g.Type)
            {
                case ItemType.Weapon: chr.WeaponSlot = g.Slot; break;
                case ItemType.Armor: chr.ArmorSlot = g.Slot; break;
                case ItemType.Helmet: chr.HelmetSlot = g.Slot; break;
                case ItemType.Shield: chr.ShieldSlot = g.Slot; break;
            }
        }

        int spellSlot = 1;
        var startSpells = StartingLoadout.ResolveSpells(cls, chr.Class, _world.Spells);
        foreach (int spellNum in startSpells)
            chr.Spell[spellSlot++] = spellNum;
        chr.PreparedSpell = StartingLoadout.ResolvePreparedSlot(startSpells, _world.Spells);
    }

    private void HandleDelChar(int index, DelCharPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.Login == "") return;

        int slot = p.Slot;
        if (slot < 1 || slot > Constants.MaxChars)
        {
            HackingAttempt(index, "Invalid CharNum");
            return;
        }

        RunAsync(HandleDelCharAsync(index, slot), nameof(HandleDelCharAsync));
    }

    private async Task HandleDelCharAsync(int index, int slot)
    {
        var sp = _pm[index];
        string name = sp.Chars[slot].Name.Trim();
        if (!string.IsNullOrEmpty(name))
            await _persistence.DeleteCharNameAsync(name);

        sp.Chars[slot] = new PlayerRecord();
        // Clear the slot on disk through the per-login chain; the load-merge preserves the account's
        // other chars, bank, penalty timers, and guild fields.
        _saver.MutateAccountInBackground(sp.Login, a => a.Chars[slot] = new PlayerRecord());
        _logger.LogInformation("Character deleted on {Login}'s account.", sp.Login);
        _dispatcher.SendTo(index, PacketBuilder.SendChars(
            Enumerable.Range(1, Constants.MaxChars).Select(i => (PlayerRecord?)sp.Chars[i]),
            _world.Classes));
    }

    private void HandleUseChar(int index, UseCharPacket p)
    {
        var sp = _pm[index];
        if (sp.IsPlaying || sp.Login == "") return;
        // Before ANY localized text below — the char-not-found alert, the combat-ghost warning, and
        // above all the whole JoinGame sequence (welcome, MOTD, join broadcast). A language changed
        // at character select has no other packet to ride in on, and the client cannot tell us in
        // time: it only learns it is in-game once this handler's output is already on the wire.
        if (ServerStrings.IsLoaded(p.Locale)) sp.Language = p.Locale;

        int slot = p.Slot;
        if (slot < 1 || slot > Constants.MaxChars)
        {
            HackingAttempt(index, "Invalid CharNum");
            return;
        }

        if (string.IsNullOrEmpty(sp.Chars[slot].Name.Trim()))
        {
            AlertAndDisconnect(index, ServerStrings.Auth_CharNotFound);
            return;
        }

        if (sp.GhostTransferSlot != 0)
        {
            var ghost = _pm[sp.GhostTransferSlot];
            if (ghost.IsGhost)
            {
                if (ghost.CharNum != slot)
                {
                    // The player's ghost is still in combat on a different character — block selection.
                    _dispatcher.SendLocalizedChatTo(index, ServerStrings.Auth_CombatGhostWarning,
                        new ChatMetadata(GameColor.BrightRed, ChatChannel.System));
                    return;
                }
                DoGhostTakeover(index, sp.GhostTransferSlot, slot);
                // Fall through to JoinGame below.
            }
            else
            {
                // Ghost cleared (died or combat expired) before the player made a selection.
                sp.GhostTransferSlot = 0;
            }
        }

        sp.CharNum = slot;
        _joinLeave.JoinGame(index);
        _logger.LogInformation("{Login}/{Name} has began playing {GameName}.", sp.Login, sp.Char.Name.Trim(), _config.GameName);
    }

    private void HandleLogoutToCharSelect(int index)
    {
        var sp = _pm[index];
        if (!sp.InGame) return;
        long now = Environment.TickCount64;
        if (sp.IsInCombat(now))
        {
            _dispatcher.SendTo(index, PacketBuilder.Alert(ServerStrings.ForPlayer(index, ServerStrings.PacketHandler_CannotLogoutCombat)));
            return;
        }
        // Runs on the game thread; LeftGame is synchronous (its save is offloaded internally).
        _joinLeave.LeftGame(index);
        sp.CharNum = 0;
        _dispatcher.SendTo(index, PacketBuilder.SendChars(
            Enumerable.Range(1, Constants.MaxChars).Select(i => (PlayerRecord?)sp.Chars[i]),
            _world.Classes));
    }

    private void DoGhostTakeover(int index, int ghostSlot, int charSlot)
    {
        var sp = _pm[index];
        var ghost = _pm[ghostSlot];
        int ghostMap = ghost.Chars[charSlot].Map;

        // Copy character state from ghost to the new connection slot.
        sp.Chars[charSlot] = ghost.Chars[charSlot];
        sp.CombatExpiresAt = ghost.CombatExpiresAt;
        sp.WasInCombat = ghost.WasInCombat;

        // Remove ghost slot's visual presence from the map.
        SendToMapBut(ghostMap, ghostSlot, PacketBuilder.LeaveMap(ghostSlot));

        // Clear NPC damage contributions that were accrued under the ghost slot index.
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            _world.MapNpcs[ghostMap, i].DamageByPlayer[ghostSlot] = 0;

        // Reset ghost slot to empty without running LeftGame cleanup.
        ghost.IsGhost = false;
        ghost.InGame = false;
        ghost.CharNum = 0;
        ghost.Login = "";
        ghost.Password = "";
        ghost.CombatExpiresAt = 0;
        ghost.WasInCombat = false;
        ghost.AttackTimer = 0;
        ghost.Target = 0;
        ghost.TargetType = 0;
        ghost.TargetSpawnSlot = 0;
        ghost.GettingMap = false;
        ghost.GhostTransferSlot = 0;
        ghost.PartyPlayer = 0;
        ghost.InParty = false;
        ghost.PartyStarter = false;
        for (int i = 1; i <= Constants.MaxChars; i++)
            ghost.Chars[i] = new PlayerRecord();
        ghost.Bank = AccountRecord.NewBank();   // sp already loaded the shared bank from disk at login

        sp.GhostTransferSlot = 0;

        _logger.LogInformation("{Login} reconnected to ghost on slot {Ghost} → now on slot {New}.", sp.Login, ghostSlot, index);
    }
}

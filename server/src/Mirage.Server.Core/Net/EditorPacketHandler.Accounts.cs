using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>
/// The editor's account browser — CREATOR only, and the only editor surface that describes a person.
///
/// <para> <b>Everything here reads account FILES,</b> so none of it may run on the game thread. Each
/// handler starts its work off the loop and hops back once, for the two things only the loop knows: who
/// is online, and applying an edit to a live player.</para>
///
/// <para> <b>The password is never loaded into a packet, never sent, and never accepted back.</b> A
/// save re-reads the record from disk and copies only the fields a Creator may change onto it, so
/// anything absent from the wire is preserved rather than blanked — which is also what keeps the
/// moderation timers out of reach from here.</para>
/// </summary>
public sealed partial class EditorPacketHandler
{
    /// <summary>Never let a page ask for the whole account directory in one packet.</summary>
    private const int MaxAccountPageSize = 100;

    private void HandleEditorRequestAccounts(int editorIndex, EditorRequestAccountsPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;

        int pageSize = Math.Clamp(p.PageSize, 1, MaxAccountPageSize);
        int page = Math.Max(0, p.Page);
        string search = p.Search.Trim();

        RunAsync(SendAccountPageAsync(editorIndex, search, p.Access, page, pageSize), nameof(SendAccountPageAsync));
    }

    private async Task SendAccountPageAsync(int editorIndex, string search, AdminLevel? access, int page, int pageSize)
    {
        var (rows, total) = await _persistence.ListAccountsAsync(search, access, page * pageSize, pageSize);
        var online = await OnGameThreadAsync(OnlineByLogin);

        var accounts = rows.Select(r =>
        {
            bool isOnline = online.TryGetValue(r.Login, out string? playingAs);
            return new EditorAccountRow
            {
                Login = r.Login,
                Access = r.Access,
                IsOnline = isOnline,
                PlayingAs = playingAs ?? "",
                CharNames = [.. r.CharNames],
            };
        }).ToList();

        SendToEditorIfStillCreator(editorIndex, new EditorAccountListPacket
        {
            Accounts = accounts,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    private void HandleEditorRequestAccount(int editorIndex, EditorRequestAccountPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0) return;

        RunAsync(SendAccountAsync(editorIndex, login), nameof(SendAccountAsync));
    }

    private async Task SendAccountAsync(int editorIndex, string login)
    {
        var account = await _persistence.LoadAccountAsync(login);
        if (account is null) return;
        var online = await OnGameThreadAsync(OnlineByLogin);

        SendToEditorIfStillCreator(editorIndex, new EditorAccountPacket
        {
            Login = account.Login,
            Access = account.Access,
            IsOnline = online.ContainsKey(account.Login),
            Guild = account.Guild,
            GuildRank = account.GuildRank,
            Chars = [.. NamedCharRows(account)],
            Bank = VaultOf(account),
        });
    }

    private void HandleEditorSaveAccount(int editorIndex, EditorSaveAccountPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0) return;

        var session = _editors.GetSession(editorIndex);
        RunAsync(SaveAccountAsync(editorIndex, login, p, session?.Login ?? ""), nameof(SaveAccountAsync));
    }

    private async Task SaveAccountAsync(int editorIndex, string login, EditorSaveAccountPacket p, string byLogin)
    {
        // Read-modify-write through the SAME per-login chain every other account write uses, so an edit
        // cannot race a character autosave. The closure captures values only — it runs later, off-thread.
        var edits = p.Chars.Where(c => c.Slot >= 1 && c.Slot <= Constants.MaxChars).ToList();
        var access = p.Access;

        int refused = edits.RemoveAll(c => !IsAcceptableCharEdit(c));
        if (refused > 0)
        {
            _logger.LogWarning("Editor {By} sent {Count} character edit(s) for {Login} holding more stats "
                + "than their level allows; those rows were not applied.", byLogin, refused, login);
        }

        // Nobody edits their OWN access. A Creator who demotes themselves by mistake locks themselves
        // out of the section that could put it back, and the only repair is a hand-edited JSON file. The
        // editor greys the picker too, but this is the check that counts.
        bool self = string.Equals(byLogin, login, StringComparison.OrdinalIgnoreCase);
        if (self)
        {
            _logger.LogInformation("Editor {By} saved their own account; the access change was ignored.", byLogin);
        }

        await _saver.MutateAccountAsync(login, account =>
        {
            if (!self) account.Access = access;
            foreach (var e in edits)
            {
                var c = account.Chars[e.Slot];
                // An empty slot stays empty: an edit to a character that does not exist would otherwise
                // conjure a nameless one that the character-select screen would then offer.
                if (c.Name.Trim().Length == 0) continue;
                ApplyCharEdit(c, e);
            }
        });

        _logger.LogInformation("Editor {By} saved account {Login}.", byLogin, login);

        // The live player carries its own copy of everything above, so an edit that only reached the file
        // would not show until they relogged. Re-sync on the loop, then re-send the record so the form
        // shows what actually landed.
        await OnGameThreadAsync(() =>
        {
            foreach (int slot in _pm.Online)
            {
                if (!_pm[slot].IsPlaying) continue;
                if (!string.Equals(_pm[slot].Login, login, StringComparison.OrdinalIgnoreCase)) continue;

                if (!self) _pm[slot].Char.Access = access;
                foreach (var e in edits)
                {
                    if (!string.Equals(_pm[slot].Char.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    ApplyCharEdit(_pm[slot].Char, e);
                }
                // The join handshake WITHOUT the welcome: re-sends their own record and re-syncs the
                // region around them, which is what makes a moved or re-levelled character land.
                _joinLeave.SendJoinData(slot);
            }
            return true;
        });

        await SendAccountAsync(editorIndex, login);
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    private void HandleEditorRenameChar(int editorIndex, EditorRenameCharPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;

        var session = _editors.GetSession(editorIndex);
        RunAsync(RenameCharAsync(editorIndex, login, p.Slot, p.Name.Trim(),
            session?.Login ?? "", session?.Locale ?? ""), nameof(RenameCharAsync));
    }

    /// <summary>
    /// Rename one character, or say why not.
    ///
    /// <para>The blast radius is small, and that is a property of the data rather than luck: guild
    /// membership, friends, ignore lists, mail and market listings all key off the account LOGIN. The
    /// character name is a key in exactly one place — the registry that stops two players sharing one — so
    /// this reserves the new name, writes it, and releases the old one.</para>
    ///
    /// <para><b>Refused while the character is logged in.</b> The name is live identity: it is on the map,
    /// in party lists, in somebody else's open trade window and in their chat scrollback. An operator who
    /// means it can kick first.</para>
    /// </summary>
    private async Task RenameCharAsync(int editorIndex, string login, int slot, string name,
        string byLogin, string locale)
    {
        string Say(string key, params (string Key, object? Value)[] args) =>
            ServerStrings.ForLocale(locale, key, args);

        void Refuse(string message) =>
            SendToEditorIfStillCreator(editorIndex, new EditorNoticePacket { Ok = false, Message = message });

        string Explain(CharRenameResult result, string subject) => result switch
        {
            CharRenameResult.BadChars => Say(ServerStrings.EditorAccounts_RenameBadChars),
            CharRenameResult.TooShort => Say(ServerStrings.EditorAccounts_RenameTooShort, ("Min", Constants.MinFieldLength)),
            CharRenameResult.TooLong => Say(ServerStrings.EditorAccounts_RenameTooLong, ("Max", Constants.NameLength)),
            CharRenameResult.NoCharacter => Say(ServerStrings.EditorAccounts_RenameNoCharacter),
            CharRenameResult.Unchanged => Say(ServerStrings.EditorAccounts_RenameUnchanged),
            CharRenameResult.Online => Say(ServerStrings.EditorAccounts_RenameOnline, ("Name", subject)),
            _ => Say(ServerStrings.EditorAccounts_RenameTaken, ("Name", subject)),
        };

        // Free checks before the file read, and the file read before the registry scan.
        var check = CharRename.CheckName(name);
        if (check != CharRenameResult.Ok) { Refuse(Explain(check, name)); return; }

        var account = await _persistence.LoadAccountAsync(login);
        string oldName = account?.Chars[slot].Name.Trim() ?? "";
        // Online-ness is the game thread's to answer, not the account file's.
        var online = await OnGameThreadAsync(OnlineByLogin);
        bool isOnline = online.TryGetValue(login, out string? playingAs) && playingAs == oldName;

        check = CharRename.CheckTarget(oldName, name, isOnline);
        if (check != CharRenameResult.Ok) { Refuse(Explain(check, oldName)); return; }

        bool sameIdentity = CharRename.SameIdentity(oldName, name);
        if (!sameIdentity && await _persistence.CharExistsAsync(name))
        {
            Refuse(Explain(CharRenameResult.Taken, name));
            return;
        }

        // Reserved BEFORE the write: two renames racing for one name would otherwise both find it free.
        if (!sameIdentity) await _persistence.AddCharNameAsync(name);
        await _saver.MutateAccountAsync(login, a =>
        {
            if (a.Chars[slot].Name.Trim() == oldName) a.Chars[slot].Name = name;
        });
        await _persistence.DeleteCharNameAsync(oldName);
        if (sameIdentity) await _persistence.AddCharNameAsync(name);   // the delete above dropped the shared key

        _logger.LogInformation("Editor {By} renamed character {Old} to {New} on account {Login}.",
            byLogin, oldName, name, login);

        SendToEditorIfStillCreator(editorIndex, new EditorNoticePacket
        { Ok = true, Message = Say(ServerStrings.EditorAccounts_Renamed, ("Old", oldName), ("New", name)) });
    }

    // ── The bag ───────────────────────────────────────────────────────────────

    private void HandleEditorGiveItem(int editorIndex, EditorGiveItemPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;
        if (p.ItemNum <= 0 || p.ItemNum > _world.Limits.Items) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        var item = _world.Items[p.ItemNum];
        // A stack takes an amount; anything else is one indivisible piece however many are asked for.
        int amount = item.Type == ItemType.Currency ? Math.Max(p.Quantity, 1) : 1;

        RunAsync(EditCharAsync(editorIndex, login, p.Slot, session?.Login ?? "", locale,
            c => ItemSystem.PlaceInInventory(c, _world.Items, p.ItemNum, amount) == 0
                ? ServerStrings.ForLocale(locale, ServerStrings.Common_InventoryFull)
                : "",
            $"gave {amount}x item {p.ItemNum} to"), nameof(EditCharAsync));
    }

    private void HandleEditorTakeItem(int editorIndex, EditorTakeItemPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;
        if (!SlotValidation.IsValidInvSlot(p.InvSlot)) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        RunAsync(EditCharAsync(editorIndex, login, p.Slot, session?.Login ?? "", locale,
            c => ItemSystem.TakeFromInventory(c, _world.Items, p.InvSlot, p.Quantity).ItemNum == 0
                ? ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BagSlotEmpty)
                : "",
            $"emptied bag slot {p.InvSlot} of"), nameof(EditCharAsync));
    }

    /// <summary>
    /// Runs one bag edit against whichever copy of the character is authoritative, and says what came of it.
    /// <paramref name="edit"/> returns a refusal string key, or "" when it worked.
    ///
    /// <para><b>An ONLINE character is edited in memory, not on disk.</b> The live record is the one being
    /// played out of; writing the file underneath it would be overwritten by the player's own next save, and
    /// writing both leaves two authors for one bag. Marking the slot dirty is what persists it, and the join
    /// handshake is what puts the new bag on their screen.</para>
    /// </summary>
    private async Task EditCharAsync(int editorIndex, string login, int slot, string byLogin, string locale,
        Func<PlayerRecord, string> edit, string what)
    {
        void Answer(bool ok, string message) => SendToEditorIfStillCreator(editorIndex,
            new EditorNoticePacket { Ok = ok, Message = message });

        var account = await _persistence.LoadAccountAsync(login);
        string name = account?.Chars[slot].Name.Trim() ?? "";
        if (name.Length == 0)
        {
            Answer(false, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_RenameNoCharacter));
            return;
        }

        string refusal = await OnGameThreadAsync(() =>
        {
            foreach (int i in _pm.Online)
            {
                if (!_pm[i].IsPlaying) continue;
                if (!string.Equals(_pm[i].Login, login, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(_pm[i].Char.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;

                string live = edit(_pm[i].Char);
                if (live.Length == 0)
                {
                    _pm.MarkDirty(i);
                    // A quest edit leaves the objective kernel tracking whatever the log used to say, so the
                    // handles are torn down and rebuilt from what it says NOW — the same pair login uses.
                    // Wasted work for a bag edit; the alternative is a caller remembering to ask for it.
                    _quests.OnPlayerGone(i);
                    _quests.OnPlayerJoin(i);
                    _joinLeave.SendJoinData(i);
                }
                return live;
            }
            return NotOnline;
        });

        if (refusal != NotOnline)
        {
            if (refusal.Length > 0) { Answer(false, refusal); return; }
            _logger.LogInformation("Editor {By} {What} {Name} on account {Login} (in play).", byLogin, what, name, login);
            Answer(true, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BagEdited));
            await SendAccountAsync(editorIndex, login);
            return;
        }

        // Offline: the file is the only copy, so it is the one edited.
        string offline = "";
        await _saver.MutateAccountAsync(login, a =>
        {
            var c = a.Chars[slot];
            if (c.Name.Trim() != name) return;   // renamed or deleted between the read and the write
            offline = edit(c);
        });

        if (offline.Length > 0) { Answer(false, offline); return; }
        _logger.LogInformation("Editor {By} {What} {Name} on account {Login}.", byLogin, what, name, login);
        Answer(true, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BagEdited));
        await SendAccountAsync(editorIndex, login);
    }

    /// <summary>Sentinel for "no live player answered", which is not a refusal — it is the signal to go and
    /// edit the file instead. A key nothing looks up, so it can never be shown.</summary>
    private const string NotOnline = "(offline)";

    // ── The spell book ────────────────────────────────────────────────────────

    private void HandleEditorLearnSpell(int editorIndex, EditorLearnSpellPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;
        if (p.SpellNum <= 0 || p.SpellNum > _world.Limits.Spells) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        RunAsync(EditCharAsync(editorIndex, login, p.Slot, session?.Login ?? "", locale,
            c => LearnSpell(c, p.SpellNum, locale), $"taught spell {p.SpellNum} to"), nameof(EditCharAsync));
    }

    private void HandleEditorForgetSpell(int editorIndex, EditorForgetSpellPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;
        if (p.SpellSlot < 1 || p.SpellSlot > Constants.MaxPlayerSpells) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        RunAsync(EditCharAsync(editorIndex, login, p.Slot, session?.Login ?? "", locale,
            c => ForgetSpell(c, p.SpellSlot, locale), $"cleared book slot {p.SpellSlot} of"), nameof(EditCharAsync));
    }

    /// <summary>
    /// Teaching, through the SAME gates a scroll goes through — <see cref="SpellSystem.CanLearn"/>.
    ///
    /// <para>The editor hands over things; what can be done with them is the game's decision. It does not
    /// choose what a character wears either — removing a worn piece merely takes it off. A spell that should
    /// arrive early arrives as a scroll, exactly as gear does.</para>
    /// </summary>
    private string LearnSpell(PlayerRecord c, int spellNum, string locale)
    {
        var spell = _world.Spells[spellNum];
        var cls = _world.Classes[c.Class];

        switch (SpellSystem.CanLearn(c, spellNum, spell, cls))
        {
            case SpellSystem.LearnResult.WrongClass:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_SpellWrongClass,
                    ("Class", ClassGate.Describe(spell.AllowedClasses, _world.Classes)));
            case SpellSystem.LearnResult.LevelTooLow:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_SpellLevelReq,
                    ("Level", spell.LevelReq));
            case SpellSystem.LearnResult.IntTooLow:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_SpellIntReq,
                    ("Int", CombatFormulas.GetSpellIntRequirement(spell, cls.Int)));
            case SpellSystem.LearnResult.AlreadyKnown:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_SpellKnown);
            case SpellSystem.LearnResult.BookFull:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BookFull);
        }

        c.Spell[SpellSystem.FindOpenSpellSlot(c)] = spellNum;
        return "";
    }

    private static string ForgetSpell(PlayerRecord c, int spellSlot, string locale)
    {
        if (c.Spell[spellSlot] <= 0)
            return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BookSlotEmpty);
        c.Spell[spellSlot] = 0;
        return "";
    }

    // ── The quest log ─────────────────────────────────────────────────────────

    private void HandleEditorSetQuestStatus(int editorIndex, EditorSetQuestStatusPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.Slot < 1 || p.Slot > Constants.MaxChars) return;
        if (!SlotValidation.IsValidQuestNum(p.QuestNum, _world.Limits.Quests)) return;
        if (!Enum.IsDefined(p.Status)) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        RunAsync(EditCharAsync(editorIndex, login, p.Slot, session?.Login ?? "", locale,
            c => SetQuestStatus(c, p.QuestNum, p.Status, locale),
            $"set quest {p.QuestNum} to {p.Status} for"), nameof(EditCharAsync));
    }

    /// <summary>
    /// Put one quest into a given state, through the same requirement gate accepting one goes through
    /// (<see cref="QuestSystem.CanHold"/>) — the editor should not be able to put a quest somewhere the game
    /// would not.
    ///
    /// <para><see cref="QuestStatus.NotStarted"/> removes the row, which is what that state means. An active
    /// state gets a progress list sized to the quest's objectives, and Done clears it, exactly as accepting
    /// and turning in do. <c>PeriodKey</c> is left alone: an empty one reads as a permanent cooldown on a
    /// non-repeatable quest and as "available again now" on a repeatable, which are both the right
    /// answers.</para>
    /// </summary>
    private string SetQuestStatus(PlayerRecord c, int questNum, QuestStatus status, string locale)
    {
        var q = _world.Quests[questNum];
        if (q.TrimmedName.Length == 0) return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestNotInLog);

        var existing = QuestSystem.FindQuest(c, questNum);
        if (status == QuestStatus.NotStarted)
        {
            if (existing is null) return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestNotInLog);
            c.Quests.Remove(existing);
            return "";
        }

        switch (QuestSystem.CanHold(c, q))
        {
            case QuestSystem.HoldResult.LevelTooLow:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestLevelReq, ("Level", q.ReqLevel));
            case QuestSystem.HoldResult.StatTooLow:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestStatReq);
            case QuestSystem.HoldResult.WrongClass:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestWrongClass,
                    ("Class", ClassGate.Describe(q.AllowedClasses, _world.Classes)));
            case QuestSystem.HoldResult.PrereqNotDone:
                return ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_QuestPrereq,
                    ("Name", _world.Quests[q.PrereqQuest].TrimmedName));
        }

        var pq = existing;
        if (pq is null)
        {
            pq = new PlayerQuest { QuestNum = questNum };
            c.Quests.Add(pq);
        }
        pq.Status = status;
        pq.Progress = status == QuestStatus.Done
            ? new List<int>()
            : new List<int>(new int[q.Objectives.Count]);
        return "";
    }

    // ── The vault ─────────────────────────────────────────────────────────────

    private void HandleEditorBankGive(int editorIndex, EditorBankGivePacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.ItemNum <= 0 || p.ItemNum > _world.Limits.Items) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        int amount = _world.Items[p.ItemNum].Type == ItemType.Currency ? Math.Max(p.Quantity, 1) : 1;

        RunAsync(EditBankAsync(editorIndex, login, session?.Login ?? "", locale,
            bank => BankSystem.PlaceInBank(bank, _world.Items, p.ItemNum, amount) == 0
                ? ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BankFull)
                : "",
            $"put {amount}x item {p.ItemNum} in the vault of"), nameof(EditBankAsync));
    }

    private void HandleEditorBankTake(int editorIndex, EditorBankTakePacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0 || p.BankSlot < 1 || p.BankSlot > Constants.MaxBankSlots) return;

        var session = _editors.GetSession(editorIndex);
        string locale = session?.Locale ?? "";
        RunAsync(EditBankAsync(editorIndex, login, session?.Login ?? "", locale,
            bank => BankSystem.TakeFromBank(bank, _world.Items, p.BankSlot, p.Quantity).ItemNum == 0
                ? ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BankSlotEmpty)
                : "",
            $"emptied vault slot {p.BankSlot} of"), nameof(EditBankAsync));
    }

    /// <summary>The vault's version of <see cref="EditCharAsync"/>, following the same rule: whichever copy
    /// is authoritative, never both.
    /// <para>What decides here is whether ANYBODY from the account is logged in, not which character is on
    /// screen — the vault is account-shared, so every character is looking at the same one and only the
    /// logged-in one is holding it open.</para></summary>
    private async Task EditBankAsync(int editorIndex, string login, string byLogin, string locale,
        Func<PlayerInvSlot[], string> edit, string what)
    {
        void Answer(bool ok, string message) => SendToEditorIfStillCreator(editorIndex,
            new EditorNoticePacket { Ok = ok, Message = message });

        if (await _persistence.LoadAccountAsync(login) is null)
        {
            Answer(false, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_RenameNoCharacter));
            return;
        }

        string refusal = await OnGameThreadAsync(() =>
        {
            foreach (int i in _pm.Online)
            {
                if (!_pm[i].IsPlaying) continue;
                if (!string.Equals(_pm[i].Login, login, StringComparison.OrdinalIgnoreCase)) continue;

                string live = edit(_pm[i].Bank);
                if (live.Length == 0) _pm.MarkDirty(i);
                return live;
            }
            return NotOnline;
        });

        if (refusal != NotOnline)
        {
            if (refusal.Length > 0) { Answer(false, refusal); return; }
            _logger.LogInformation("Editor {By} {What} {Login} (in play).", byLogin, what, login);
            Answer(true, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BagEdited));
            await SendAccountAsync(editorIndex, login);
            return;
        }

        string offline = "";
        await _saver.MutateAccountAsync(login, a => offline = edit(a.Bank));

        if (offline.Length > 0) { Answer(false, offline); return; }
        _logger.LogInformation("Editor {By} {What} {Login}.", byLogin, what, login);
        Answer(true, ServerStrings.ForLocale(locale, ServerStrings.EditorAccounts_BagEdited));
        await SendAccountAsync(editorIndex, login);
    }

    /// <summary>Whether a row describes a character the game itself could have produced: no more stat
    /// value — spent or unspent — than its level has granted. The editor blocks such a row before it is
    /// sent and says which character is at fault; this is the check that holds against a packet the
    /// editor did not write. Refused rather than clamped: which of six numbers to cut is the operator's
    /// call, and a silent trim would leave the form asserting an edit that did not land.</summary>
    private static bool IsAcceptableCharEdit(EditorCharRow e) =>
        StatFormulas.IsWithinPointBudget(Math.Clamp(e.Level, 1, Constants.MaxLevel),
            e.Str, e.Def, e.Spd, e.Int, e.Points);

    // The fields a Creator may change, in one place so the file write and the live player cannot disagree
    // about what an edit means.
    private void ApplyCharEdit(PlayerRecord c, EditorCharRow e)
    {
        c.Level = Math.Clamp(e.Level, 1, Constants.MaxLevel);
        c.Exp = Math.Max(0, e.Exp);
        c.Str = Math.Max(0, e.Str);
        c.Def = Math.Max(0, e.Def);
        c.Spd = Math.Max(0, e.Spd);
        c.Int = Math.Max(0, e.Int);
        c.Points = Math.Max(0, e.Points);
        if (SlotValidation.IsValidMapNum(e.Map, _world.Limits.Maps))
        {
            c.Map = e.Map;
            c.X = Math.Clamp(e.X, 0, _world.Maps[c.Map].Width - 1);
            c.Y = Math.Clamp(e.Y, 0, _world.Maps[c.Map].Height - 1);
        }
    }

    private List<EditorCharRow> NamedCharRows(AccountRecord account)
    {
        var rows = new List<EditorCharRow>();
        for (int i = 1; i < account.Chars.Length; i++)
        {
            var c = account.Chars[i];
            if (c.Name.Trim().Length == 0) continue;
            rows.Add(new EditorCharRow
            {
                Slot = i,
                Name = c.Name.Trim(),
                Class = c.Class,
                Level = c.Level,
                Exp = c.Exp,
                Map = c.Map,
                X = c.X,
                Y = c.Y,
                Str = c.Str,
                Def = c.Def,
                Spd = c.Spd,
                Int = c.Int,
                Points = c.Points,
                Inv = BagOf(c),
                Spells = BookOf(c),
                Quests = LogOf(c),
            });
        }
        return rows;
    }

    /// <summary>The occupied slots of one character's bag, named so the operator is not reading item numbers.
    /// Empty slots are left out — a bag is mostly empty and forty blank rows say nothing.</summary>
    private List<EditorInvSlot> BagOf(PlayerRecord c)
    {
        var bag = new List<EditorInvSlot>();
        for (int i = 1; i <= Constants.MaxInv; i++)
        {
            var inv = c.Inv[i];
            if (inv.Num <= 0 || inv.Num > _world.Limits.Items) continue;
            var item = _world.Items[inv.Num];
            bag.Add(new EditorInvSlot
            {
                Slot = i,
                Num = inv.Num,
                Name = item.TrimmedName,
                Quantity = inv.Quantity,
                Dur = inv.Dur,
                Worn = ItemSystem.EquippedSlotForType(c, item.Type) == i,
            });
        }
        return bag;
    }

    /// <summary>The occupied slots of one character's spell book, named rather than numbered.</summary>
    private List<EditorSpellSlot> BookOf(PlayerRecord c)
    {
        var book = new List<EditorSpellSlot>();
        for (int i = 1; i <= Constants.MaxPlayerSpells; i++)
        {
            int num = c.Spell[i];
            if (num <= 0 || num > _world.Limits.Spells) continue;
            book.Add(new EditorSpellSlot { Slot = i, Num = num, Name = _world.Spells[num].TrimmedName });
        }
        return book;
    }

    /// <summary>One character's quest log, with the objective counts read against the quest's own
    /// definition so the operator sees "2/5" rather than a bare number.</summary>
    private List<EditorQuestRow> LogOf(PlayerRecord c)
    {
        var log = new List<EditorQuestRow>();
        foreach (var pq in c.Quests)
        {
            if (!SlotValidation.IsValidQuestNum(pq.QuestNum, _world.Limits.Quests)) continue;
            var q = _world.Quests[pq.QuestNum];
            if (q.TrimmedName.Length == 0) continue;

            var parts = new List<string>(q.Objectives.Count);
            for (int k = 0; k < q.Objectives.Count; k++)
            {
                int done = k < pq.Progress.Count ? pq.Progress[k] : 0;
                parts.Add($"{done}/{q.Objectives[k].Count}");
            }

            log.Add(new EditorQuestRow
            {
                QuestNum = pq.QuestNum,
                Name = q.TrimmedName,
                Status = pq.Status,
                Progress = string.Join(", ", parts),
                Eligible = QuestSystem.CanHold(c, q) == QuestSystem.HoldResult.Ok,
            });
        }
        return log;
    }

    /// <summary>The occupied slots of the account vault. Nothing is worn out of a vault, so every row here
    /// reads as unworn.</summary>
    private List<EditorInvSlot> VaultOf(AccountRecord account)
    {
        var vault = new List<EditorInvSlot>();
        for (int i = 1; i <= Constants.MaxBankSlots; i++)
        {
            var slot = account.Bank[i];
            if (slot.Num <= 0 || slot.Num > _world.Limits.Items) continue;
            vault.Add(new EditorInvSlot
            {
                Slot = i,
                Num = slot.Num,
                Name = _world.Items[slot.Num].TrimmedName,
                Quantity = slot.Quantity,
                Dur = slot.Dur,
            });
        }
        return vault;
    }

    // Who is logged in, by account login, valued by the character they are on. Game thread only.
    private Dictionary<string, string> OnlineByLogin()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (int slot in _pm.Online)
        {
            if (!_pm[slot].IsPlaying) continue;
            map[_pm[slot].Login] = _pm[slot].Char.Name.Trim();
        }
        return map;
    }

    /// <summary>Sends an account payload only if that session is still authenticated AND still a Creator.
    /// The gather is async, so access can have been taken away — or the slot handed to somebody else —
    /// between the request and the reply.</summary>
    private void SendToEditorIfStillCreator(int editorIndex, Mirage.Shared.Protocol.IPacket packet)
    {
        var session = _editors.GetSession(editorIndex);
        if (session is null || !session.IsAuthenticated || session.AdminLevel < AdminLevel.Creator) return;
        _dispatcher.SendToEditor(editorIndex, packet);
    }

    /// <summary>Runs <paramref name="read"/> on the game thread and awaits it. The editor dispatch runs
    /// off the loop, so anything touching player state has to make this hop.</summary>
    private Task<T> OnGameThreadAsync<T>(Func<T> read)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            try { tcs.TrySetResult(read()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Config;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text;

namespace Mirage.Client.Shell.Screens;

/// <summary>The right-click menus over players and NPCs, and the quest/conversation dialogs they
/// open. Includes the stacked-NPC case, where several NPCs occupy one tile.</summary>
public sealed partial class GameplayScreen : IGameScreen
{
    /// <summary>Builds and opens the right-click player context menu. Items are filtered by the
    /// local player's access tier.</summary>
    private void OpenPlayerContextMenu(string targetName, Point at)
    {
        if (BuildPlayerMenuItems(targetName) is not { } items) return;
        var screen = new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH);
        _contextMenu.Open(at, targetName, items, screen, _gameFont!);
    }

    /// <summary>The actions offered on another player, or null if there is no menu to show (an empty
    /// name, or the local player pointing at themselves). Split out from the opener so the TILE menu
    /// can nest the same list under a player's name alongside whatever else is on that square.</summary>
    private List<ContextMenu.Item>? BuildPlayerMenuItems(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;
        var me = _ctx.State.Me;
        if (targetName == me.Name.Trim()) return null;
        var access = me.Access;
        var sender = _ctx.Sender;

        var items = new List<ContextMenu.Item>
        {
            new(ClientStrings.Get(ClientStrings.ContextMenu_Info), () => sender.SendPlayerInfoRequest(targetName)),
        };
        // Party Invite — Player & Monitor only; hidden for Mapper+ (admins can't party).
        if (access <= AdminLevel.Monitor)
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_PartyInvite), () => sender.SendPartyRequest(targetName)));
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Whisper), () => StartWhisper(targetName)));

        // Direct trade — a Player-only economy action (matches guild). The server validates that the
        // target is online and within casting range (r=5), so an out-of-range click just gets a refusal.
        if (access == AdminLevel.Player)
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Trade), () => sender.SendTradeInvite(targetName)));

        // Friends / Ignore. Both are keyed to the target's ACCOUNT (the server resolves this character
        // name to it), so ignoring here silences every character that player owns, on every channel.
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_AddFriend), () => sender.SendSocialAddFriend(targetName)));
        items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Ignore), () => sender.SendSocialAddIgnore(targetName)));

        // Guild (a Player-only feature). Invite a guildless player if I'm an officer/leader; or request
        // to join the target's guild if I'm guildless and their open guild has an officer/leader here.
        if (access == AdminLevel.Player)
        {
            var target = FindPlayerByName(targetName);
            if (target is not null)
            {
                if (me.GuildRank >= GuildRank.Officer && target.GuildId == 0)
                    items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_GuildInvite), () => sender.SendGuildInvite(targetName)));
                if (me.GuildId == 0 && target.GuildRank >= GuildRank.Officer && target.GuildOpen)
                    items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_GuildRequest), () => sender.SendGuildJoinRequest(targetName)));
            }
        }

        if (access > AdminLevel.Player) // Monitor+
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Mute), () => sender.SendMute(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Kick), () => sender.SendKick(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Ban), () => sender.SendBan(targetName)));
        }
        if (access > AdminLevel.Mapper) // Developer+
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_TeleportTo), () => sender.SendWarpMeTo(targetName)));
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_BringHere), () => sender.SendWarpToMe(targetName)));
        }
        if (access >= AdminLevel.Creator)
        {
            var subItems = new List<ContextMenu.Item>
            {
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Player),    () => sender.SendSetAccess(targetName, AdminLevel.Player)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Monitor),   () => sender.SendSetAccess(targetName, AdminLevel.Monitor)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Mapper),    () => sender.SendSetAccess(targetName, AdminLevel.Mapper)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Developer), () => sender.SendSetAccess(targetName, AdminLevel.Developer)),
                new(ClientStrings.Get(ClientStrings.ContextMenu_Access_Creator),   () => sender.SendSetAccess(targetName, AdminLevel.Creator)),
            };
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_SetAccess), subItems));
        }

        return items;
    }

    /// <summary>
    /// The tile menu: right-click a square and get everything on it — the loot lying there, the
    /// players standing on it, the NPCs occupying it — each opening its own actions.
    ///
    /// <para><b>Why a square rather than a thing.</b> Pointing at a pixel is ambiguous by design in a
    /// two-plane world: a mob on a bridge and a player underneath it draw within a few pixels of each
    /// other, and a pile of loot has no sprite of its own worth aiming at. Asking "what is HERE" has
    /// one answer where "what did I click" has several.</para>
    ///
    /// <para>It also makes loot reachable without standing on it, which is the whole point of the
    /// exercise: an item you can take from five tiles away cannot be denied by somebody parking on
    /// top of it. Range is drawn as an enabled/disabled state that updates per frame, so an entry
    /// lights up as you walk toward it rather than failing when you click.</para>
    ///
    /// <para>Falls back to the old flat presentation when the square holds exactly one interesting
    /// thing — a lone keeper should not cost two clicks to talk to.</para>
    /// </summary>
    private void OpenTileContextMenu(IReadOnlyList<TargetRef> npcStack, float worldPixelX, float worldPixelY, Point at)
    {
        if (_gameFont is null) return;

        // World pixel → world tile → which of the nine map cells owns it.
        int wtx = (int)MathF.Floor(worldPixelX / Constants.PicX);
        int wty = (int)MathF.Floor(worldPixelY / Constants.PicY);
        if (wtx < 0 || wty < 0) return;
        int cell = wtx / _ctx.State.MapTilesX, row = wty / _ctx.State.MapTilesY;
        if (cell > 2 || row > 2) return;

        int mapNum = _ctx.State.NeighborMapNums[cell, row];
        if (mapNum <= 0) return;
        int tileX = wtx - cell * _ctx.State.MapTilesX;
        int tileY = wty - row * _ctx.State.MapTilesY;

        var groups = new List<(string Name, List<ContextMenu.Item> Items)>();

        // Players first: the thing on a square most likely to be the reason for right-clicking it.
        //
        // Two ways in, and both are needed. The tile sweep is what makes a player on a pile of loot
        // appear beside it. The pixel hit-test is what preserves the old precision: a sprite is drawn
        // taller than its tile and slides between tiles while walking, so clicking someone's head can
        // land on the SQUARE ABOVE them — which the sweep alone would answer with "nobody there".
        var named = new HashSet<string>(StringComparer.Ordinal);

        void AddPlayer(string name)
        {
            if (!named.Add(name)) return;
            if (BuildPlayerMenuItems(name) is { } playerItems) groups.Add((name, playerItems));
        }

        var hovered = ComputeHoveredEntity();
        if (hovered.Kind == TargetKind.Player && hovered.A != _ctx.State.MyIndex)
        {
            string name = _ctx.State.Players[hovered.A].Name.Trim();
            if (name.Length > 0) AddPlayer(name);
        }

        for (int i = 1; i < _ctx.State.Players.Length; i++)
        {
            if (i == _ctx.State.MyIndex) continue;
            var p = _ctx.State.Players[i];
            if (p.Map != mapNum || p.X != tileX || p.Y != tileY) continue;
            string name = p.Name.Trim();
            if (name.Length > 0) AddPlayer(name);
        }

        // Then NPCs, on the same terms the NPC-only menu uses: a menu-bearing NPC on an unreachable
        // plane contributes nothing, because the server would refuse the interact anyway.
        foreach (var npc in npcStack)
            if (BuildNpcMenuItems(npc, out string npcName) is { } npcItems && NpcLayerReachable(npc))
                groups.Add((npcName, npcItems));

        // Loot last: it is under everything else, literally and in the menu.
        if (BuildTileLootItems(mapNum, tileX, tileY, wtx, wty) is { } loot)
            groups.Add((ClientStrings.Get(ClientStrings.ContextMenu_TileGround), loot));

        if (groups.Count == 0) return;

        var screen = new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH);
        if (groups.Count == 1)
        {
            _contextMenu.Open(at, groups[0].Name, groups[0].Items, screen, _gameFont);
        }
        else
        {
            var top = new List<ContextMenu.Item>(groups.Count);
            foreach (var g in groups) top.Add(new ContextMenu.Item(g.Name, g.Items));
            _contextMenu.Open(at, "", top, screen, _gameFont);
        }
    }

    /// <summary>
    /// What is lying on one square, or null if nothing is.
    ///
    /// <para><b>The pile is grouped by whose it is.</b> Anything the local player can claim collapses
    /// under "Your Loot" with a single "Pick Up All" at the top; anything held by somebody else is
    /// listed but disabled, named with its holder. Showing a stranger's claim rather than hiding it is
    /// deliberate — "there is loot here you cannot have yet" is information, and an item that silently
    /// vanished from the menu would read as a bug.</para>
    ///
    /// <para>A split gold drop puts one tagged stack per contributor on a single tile, so several
    /// claimable stacks sharing a square is the ordinary case, not an edge one.</para>
    ///
    /// <para>Every entry is range-gated by the same per-frame predicate the NPC menu uses, so walking
    /// closer enables it in place.</para>
    /// </summary>
    private List<ContextMenu.Item>? BuildTileLootItems(int mapNum, int tileX, int tileY, int worldTx, int worldTy)
    {
        var onMap = _ctx.State.ItemsForMap(mapNum);
        if (onMap is null) return null;

        long nowMs = Environment.TickCount64;
        int me = _ctx.State.MyIndex;
        var mine = new List<MapItemRecord>();
        var held = new List<MapItemRecord>();

        foreach (var mi in onMap.Values)
        {
            if (mi.Num <= 0 || mi.X != tileX || mi.Y != tileY) continue;
            // A tag that has run out is nobody's, including its old owner's — same rule the server
            // applies, just for the sake of what the menu says.
            bool claimed = mi.TaggedToPlayer > 0 && mi.TaggedToPlayer != me && nowMs < mi.TagExpiresAt;
            (claimed ? held : mine).Add(mi);
        }
        if (mine.Count == 0 && held.Count == 0) return null;

        // Top of the stack first, so the menu's order matches what the pick-up key would take.
        // Ordered by SLOT because the client is never told a drop's DropSeq — slots are handed out in
        // increasing order per map, so the newest drop is the highest one. That is a proxy rather than
        // the real thing, and it only decides presentation: the server picks its own order for
        // Pick Up All, and each single pick-up names its slot outright.
        mine.Sort((a, b) => b.Slot.CompareTo(a.Slot));
        held.Sort((a, b) => b.Slot.CompareTo(a.Slot));

        bool InReach(WorldLayer layer) =>
            WorldCoordHelper.IsInSpellRange(
                _ctx.State.MapTilesX + _ctx.State.Me.X, _ctx.State.MapTilesY + _ctx.State.Me.Y, 1,
                worldTx, worldTy, 1)
            && ClientLineOfSight.LayerConnectsFromLocalPlayer(_ctx.State, worldTx, worldTy, layer);

        string Label(MapItemRecord mi)
        {
            string name = _ctx.State.Items[mi.Num].Name.Trim() ?? "";
            // Currency is the only thing whose stack size means anything, so it is the only thing that
            // says how much — see NpcDrop on why Quantity is dead for everything else.
            return mi.Quantity > 0 && _ctx.State.Items[mi.Num].Type == ItemType.Currency
                ? $"{name} x{mi.Quantity}"
                : name;
        }

        var entries = new List<ContextMenu.Item>();

        if (mine.Count > 0)
        {
            var claimable = new List<ContextMenu.Item>();
            var layer = mine[0].Layer;
            claimable.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_PickUpAll),
                () => _ctx.Sender.SendMapPickUpAll(mapNum, tileX, tileY, layer),
                (Func<bool>)(() => InReach(layer))));

            foreach (var mi in mine)
            {
                int slot = mi.Slot;
                var itemLayer = mi.Layer;
                claimable.Add(new(Label(mi),
                    () => _ctx.Sender.SendMapPickUp(mapNum, slot),
                    (Func<bool>)(() => InReach(itemLayer))));
            }

            // One item is not a pile: a lone drop reads better as "Pick Up <thing>" than as a
            // submenu whose only sibling is "Pick Up All" of one.
            if (mine.Count == 1)
            {
                int slot = mine[0].Slot;
                var itemLayer = mine[0].Layer;
                entries.Add(new($"{ClientStrings.Get(ClientStrings.ContextMenu_PickUp)}: {Label(mine[0])}",
                    () => _ctx.Sender.SendMapPickUp(mapNum, slot),
                    (Func<bool>)(() => InReach(itemLayer))));
            }
            else
            {
                entries.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_PickUpMenu), claimable));
            }
        }

        foreach (var mi in held)
        {
            string owner = _ctx.State.Players[mi.TaggedToPlayer].Name.Trim();
            entries.Add(new(ClientStrings.Format(ClientStrings.ContextMenu_LootHeldBy,
                    ("Item", Label(mi)), ("Owner", owner)),
                () => { },
                (Func<bool>)(() => false)));   // listed, never clickable — the claim is the server's to release
        }

        return entries.Count > 0 ? entries : null;
    }

    // Right-click / interact on an NPC → a menu of its roles (keeper shop/inn + actionable quests), each enabled
    // ONLY within r=5 (grayed out of range, enabling live as the player approaches — a per-frame EnabledFn).
    private void OpenNpcContextMenu(TargetRef npc, Point at)
        => OpenNpcMenusForStack(new[] { npc }, at);

    /// <summary>Opens the right-click menu for one or more NPCs stacked under the cursor. ONE menu-bearing NPC →
    /// its actions flat, titled by its name. TWO-plus (a ground mob + a mob on the bridge above the same tile) →
    /// one menu whose top level is each NPC's NAME, expanding to that NPC's actions, so it's clear which set of
    /// actions belongs to which NPC. NPCs with no keeper/quest menu are skipped.</summary>
    private void OpenNpcMenusForStack(IReadOnlyList<TargetRef> stack, Point at)
    {
        if (_gameFont is null) return;
        var groups = new List<(string Name, List<ContextMenu.Item> Items)>();
        bool blockedByLayer = false;
        foreach (var npc in stack)
        {
            if (BuildNpcMenuItems(npc, out string name) is { } items)
            {
                // A menu-bearing NPC on a plane the player's doesn't connect to is unreachable, so it contributes no
                // menu — the cursor can't tell the two apart, and the server refuses the interact anyway. Noted (not
                // returned on) so a reachable NPC stacked under the same pixel still opens its own menu silently.
                if (NpcLayerReachable(npc)) groups.Add((name, items));
                else blockedByLayer = true;
            }
        }

        if (groups.Count == 0)
        {
            // Only complain when a menu WOULD have opened but for the layer; right-clicking a plain mob (on either
            // plane) has never opened anything, so it stays quiet.
            if (blockedByLayer) AddChatLine(ClientStrings.Get(ClientStrings.GameplayScreen_NpcOtherLayer), GameColor.BrightRed);
            return;
        }

        var screen = new Rectangle(0, 0, UiHelper.RefW, UiHelper.RefH);
        if (groups.Count == 1)
        {
            _contextMenu.Open(at, groups[0].Name, groups[0].Items, screen, _gameFont);   // single → flat, titled by name
        }
        else
        {
            var top = new List<ContextMenu.Item>(groups.Count);
            foreach (var g in groups) top.Add(new ContextMenu.Item(g.Name, g.Items));     // per-NPC name → its action submenu
            _contextMenu.Open(at, "", top, screen, _gameFont);
        }
    }

    // Can the local player reach this native map NPC across the two layers? Same plane always; across them only
    // from a ramp's mount side — so a bridge keeper is unreachable from the ground beneath it (even though both
    // draw under the same cursor pixel) but IS reachable from the ramp's foot. Mirrors the server's interact gate
    // (GameWorld.IsNpcInInteractRange) so the menu never offers what it would refuse. A slot that no longer
    // resolves counts as unreachable and simply drops out of the menu.
    private bool NpcLayerReachable(TargetRef npc)
    {
        if (npc.Kind != TargetKind.Npc) return false;
        var npcs = _ctx.State.NpcsForMap(npc.B);
        if (npcs is null || !SlotValidation.IsValidNpcSlot(npc.A)) return false;
        if (!ResolveTargetTile(npc, out int m, out int nx, out int ny)) return false;
        var off = CellOffsetForMapClient(m);
        if (off is null) return false;
        return ClientLineOfSight.LayerConnectsFromLocalPlayer(
            _ctx.State, off.Value.ox + nx, off.Value.oy + ny, npcs[npc.A].Layer);
    }

    /// <summary>Builds the Talk/Shop/Quest actions for ONE npc (each range-gated), or null if it has no right-click
    /// menu. <paramref name="name"/> is its display name (menu title / per-NPC label when stacked).</summary>
    private List<ContextMenu.Item>? BuildNpcMenuItems(TargetRef npc, out string name)
    {
        name = "";
        if (npc.Kind != TargetKind.Npc) return null;   // right-click menus are for native NPCs (A=slot, B=map); a chasing guest has none
        var npcs = _ctx.State.NpcsForMap(npc.B);
        int num = npcs is not null && SlotValidation.IsValidNpcSlot(npc.A) ? npcs[npc.A].Num : 0;
        if (num <= 0) return null;
        // An NPC needs something to offer: a shop, a quest, or a conversation. A plain mob has none of
        // the three and is handled by the melee key rather than this menu.
        //
        // The conversation clause is what makes the Talk item below reachable. Gating on shop-or-quest
        // alone means the only NPCs that can show Talk are the ones that also sell or hire, and the
        // fourteen whose entire purpose is being talked to — the ferryman, the chronicler, the locals,
        // the road signs — have no menu at all.
        if (_ctx.State.NpcKeeperShop[num] == 0
            && _ctx.State.NpcQuestGlyph[num] == 0
            && _ctx.State.ConversationForNpc(num) == 0) return null;
        name = _ctx.State.NpcDefs[num]?.Name?.Trim() ?? "";
        int npcSize = _ctx.State.NpcDefs[num]?.EffectiveSize ?? 1;   // footprint-aware r=5: an oversize NPC is reachable by its body
        bool InRange()
        {
            var me = _ctx.State.Me;
            if (!ResolveTargetTile(npc, out int m, out int nx, out int ny)) return false;
            var off = CellOffsetForMapClient(m);
            if (off is null) return false;
            return WorldCoordHelper.IsInSpellRange(
                _ctx.State.MapTilesX + me.X, _ctx.State.MapTilesY + me.Y, 1,
                off.Value.ox + nx, off.Value.oy + ny, npcSize);
        }
        var items = new List<ContextMenu.Item>();

        // Conversation ("Talk"): present when the NPC has a dialogue tree. Forces Choice.Talk so it opens the chat
        // even for an NPC that also keeps a shop / gives quests (talk-first is the melee default; here it's explicit).
        if (_ctx.State.ConversationForNpc(num) > 0)
        {
            items.Add(new(ClientStrings.Get(ClientStrings.ContextMenu_Talk),
                () => _ctx.Sender.SendNpcInteract(npc.B, npc.A, NpcInteractChoice.Talk),   // B = map, A = slot
                (Func<bool>)InRange));
        }

        // Keeper shop/inn: an inn keeper (kind 2) reads "Inn", a store keeper "Shop". Forces Choice.Shop
        // so a keeper that also gives quests still opens the shop from this item (no re-route into the quest menu).
        if (_ctx.State.NpcKeeperShop[num] != 0)
        {
            string label = _ctx.State.NpcKeeperShop[num] == 2
                ? ClientStrings.Get(ClientStrings.ContextMenu_Inn)
                : ClientStrings.Get(ClientStrings.ContextMenu_Shop);
            items.Add(new(label,
                () => _ctx.Sender.SendNpcInteract(npc.B, npc.A, NpcInteractChoice.Shop),   // B = map, A = slot
                (Func<bool>)InRange));
        }

        // Actionable quests: a "Quest:" / "Turn in:" item per quest → the offer dialog. Only quests the player can
        // accept or turn in right now, so the menu matches the overhead glyph instead of listing every quest the
        // NPC will ever hold.
        foreach (var (questNum, action) in _ctx.State.ActionableQuestsAt(num))
        {
            string qname = _ctx.State.QuestDefs[questNum]?.TrimmedName ?? "";
            string label = action == ClientState.QuestAction.Accept
                ? ClientStrings.Format(ClientStrings.ContextMenu_QuestAccept, ("Name", qname))
                : ClientStrings.Format(ClientStrings.ContextMenu_QuestTurnIn, ("Name", qname));
            int qn = questNum, map = npc.B, slot = npc.A;
            var act = action;
            // Claim the NPC before the offer opens. Accept and turn-in name only the quest — the giver comes
            // from the NPC the SERVER has recorded — so an offer opened without this is refused.
            items.Add(new(label, () =>
            {
                _ctx.Sender.SendNpcInteract(map, slot, NpcInteractChoice.QuestOffer);
                OpenQuestDialog(qn, act);
            }, (Func<bool>)InRange));
        }

        return items.Count > 0 ? items : null;
    }

    // Open the accept/turn-in offer for a quest at the given NPC (from the NPC menu). It's a panel, so it joins
    // the normal z-order / focus flow (mirrors OpenShop / OpenInnPanel).
    private void OpenQuestDialog(int questNum, ClientState.QuestAction action)
    {
        _questDialog.Open(questNum, action);
        BringToFront(PanelQuestDialog);
        _panelFocused = true;
    }

    /// <summary>Server reply to a talk-first interact that resolved to a quest (melee key, or a conversation's
    /// "ask about quests" hand-off). Opens the quest OFFER panel directly for the common single-quest case — the
    /// melee key has no cursor, and funnelling one quest through a one-item menu is the "opening a quest opens a
    /// menu" wart. Only when several quests are actionable at once does it fall back to the centered NPC menu so
    /// the player can pick which. (map, slot) identify the NPC.</summary>
    public void OpenNpcQuestMenuAt(int map, int slot)
    {
        if (_gameFont is null) return;
        var npcs = _ctx.State.NpcsForMap(map);
        int num = npcs is not null && SlotValidation.IsValidNpcSlot(slot) ? npcs[slot].Num : 0;
        if (num <= 0) return;

        int firstQuest = 0;
        var firstAction = ClientState.QuestAction.Accept;
        int actionable = 0;
        foreach (var (questNum, action) in _ctx.State.ActionableQuestsAt(num))
        {
            actionable++;
            if (actionable == 1)
            {
                firstQuest = questNum;
                firstAction = action;
            }
        }

        if (actionable == 1)
            OpenQuestDialog(firstQuest, firstAction);
        else
            OpenNpcContextMenu(new TargetRef(TargetKind.Npc, slot, map), new Point(UiHelper.RefW / 2, UiHelper.RefH / 2));
    }

    /// <summary>Server reply to an interact that resolved to a conversation (OpenNpcConversation) — open the
    /// conversation panel for the NPC (map, slot) on conversation <paramref name="conv"/>. Joins the z-order flow.</summary>
    public void OpenConversationAt(int map, int slot, int conv)
    {
        _conversation.Open(conv, map, slot);
        BringToFront(PanelConversation);
        _panelFocused = true;
    }

    private PlayerRecord? FindPlayerByName(string name)
    {
        var players = _ctx.State.Players;
        for (int i = 1; i < players.Length; i++)
            if (players[i].Name.Trim() == name) return players[i];
        return null;
    }

    /// <summary>Whisper menu item — focuses chat and prefills `/w <name> ` for the user to type.</summary>
    private void StartWhisper(string targetName)
    {
        _chat.StartWhisper(targetName);
    }
}

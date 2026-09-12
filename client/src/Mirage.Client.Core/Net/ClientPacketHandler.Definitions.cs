using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Net;

/// <summary>The world data loaded once on join and re-pushed whenever the editor saves: items,
/// NPCs, shops, spells, classes, quests, conversations and map groups.</summary>
public sealed partial class ClientPacketHandler : IClientEvents
{
    // ── World data ────────────────────────────────────────────────────────────

    private void HandleSendItems(SendItemsPacket p)
    {
        foreach (var item in p.Items)
        {
            if (!SlotValidation.IsValidItemNum(item.Num, _state.Limits.Items)) continue;
            _state.Items[item.Num] = new ItemRecord
            {
                Name = item.Name,
                Pic = item.Pic,
                ItemSheet = item.ItemSheet,
                Type = item.Type,
                Durability = item.Durability,
                VitalAmount = item.VitalAmount,
                Power = item.Power,
                Tier = item.Tier,
                NonTradeable = item.NonTradeable,
                NonListable = item.NonListable,
                NonMailable = item.NonMailable,
                DestroyOnDrop = item.DestroyOnDrop,
                NonJunkable = item.NonJunkable,
                Price = item.Price,
            };
        }
    }

    private void HandleSendNpcs(SendNpcsPacket p)
    {
        foreach (var n in p.Npcs)
        {
            if (!SlotValidation.IsValidNpcNum(n.Num, _state.Limits.Npcs)) continue;
            _state.NpcDefs[n.Num] = new NpcRecord
            {
                Name = n.Name,
                Sprite = n.Sprite,
                SpriteSheet = n.SpriteSheet,
                Size = n.Size,   // footprint size class 1/2/3; drives the sprite/bar/hit-test scale
                Behavior = n.Behavior,
                SpawnSecs = n.SpawnSecs,
                EmitsLight = n.EmitsLight,
                Light = n.Light,
            };
            _state.NpcKeeperShop[n.Num] = n.KeeperShop;
        }
    }

    private void HandleOpenInn(OpenInnPacket p)
    {
        // Store the keeper's shop number so the InnPanel resolves banking/set-spawn/market against it
        // (shops are not map-bound), then raise the panel.
        _state.ActiveInnShopNum = p.ShopNum;
        OpenInn?.Invoke();
    }

    // ── Quests ────────────────────────────────────────────────────────────────
    // Defs arrive once at join (like items/npcs); the per-player log + eligible set arrive via QuestLog on join
    // and after every change. The overhead ?/! glyphs + the interaction menu are derived in ClientState.

    private void HandleSendQuests(SendQuestsPacket p)
    {
        var defs = new List<(int, QuestRecord)>(p.Quests.Count);
        foreach (var q in p.Quests)
        {
            if (!SlotValidation.IsValidQuestNum(q.Num, _state.Limits.Quests)) continue;
            defs.Add((q.Num, ToQuestRecord(q)));
        }
        _state.SetQuestDefs(defs);
    }

    private static QuestRecord ToQuestRecord(SendQuestsPacket.QuestData q) => new()
    {
        Name = q.Name,
        Description = q.Description,
        Objectives = q.Objectives,   // fresh off the wire — no sharing to guard against
        RewardItems = q.RewardItems,
        RepeatRewardItems = q.RepeatRewardItems,
        GiverNpc = q.GiverNpc, TurnInNpc = q.TurnInNpc, Repeatable = q.Repeatable, Cadence = q.Cadence,
    };

    private void HandleQuestLog(QuestLogPacket p)
    {
        var quests = new List<PlayerQuest>(p.Quests.Count);
        foreach (var e in p.Quests)
        {
            if (!SlotValidation.IsValidQuestNum(e.QuestNum, _state.Limits.Quests)) continue;
            quests.Add(new PlayerQuest { QuestNum = e.QuestNum, Status = e.Status, Progress = new List<int>(e.Progress) });
        }
        _state.SetQuests(quests, p.EligibleQuests, p.CooldownQuests);
    }

    // Live editor edit of one quest DEF (broadcast on an editor save) — refresh the cached def + the ?/! glyphs.
    private void HandleUpdateQuest(UpdateQuestPacket p)
    {
        if (!SlotValidation.IsValidQuestNum(p.QuestNum, _state.Limits.Quests)) return;
        _state.SetQuestDef(p.QuestNum, ToQuestRecord(p));
    }

    private static QuestRecord ToQuestRecord(UpdateQuestPacket q) => new()
    {
        Name = q.Name,
        Description = q.Description,
        Objectives = q.Objectives,   // fresh off the wire — no sharing to guard against
        RewardItems = q.RewardItems,
        RepeatRewardItems = q.RepeatRewardItems,
        GiverNpc = q.GiverNpc, TurnInNpc = q.TurnInNpc, Repeatable = q.Repeatable, Cadence = q.Cadence,
    };

    // ── NPC conversations ─────────────────────────────────────────────────────
    // Defs arrive once at join (like quests); the character's spoken-set arrives via ConversationLog on join and
    // whenever a new conversation is opened. The overhead "..." glyphs are derived in ClientState.

    private void HandleSendConversations(SendConversationsPacket p)
    {
        var defs = new List<(int, ConversationRecord)>(p.Conversations.Count);
        foreach (var c in p.Conversations)
        {
            if (!SlotValidation.IsValidConversationNum(c.Num, _state.Limits.Conversations)) continue;
            defs.Add((c.Num, ToConversationRecord(c.Name, c.SpeakerNpc, c.RootNodeId, c.Nodes)));
        }
        _state.SetConvDefs(defs);
    }

    private static ConversationRecord ToConversationRecord(string name, int speakerNpc, int rootNodeId,
        List<ConversationNode> nodes) => new()
        {
            Name = name,
            SpeakerNpc = speakerNpc,
            RootNodeId = rootNodeId,
            Nodes = nodes,   // fresh off the wire — no sharing to guard against
        };

    private void HandleConversationLog(ConversationLogPacket p) => _state.SetConversationsSpoken(p.Spoken);

    // Live editor edit of one conversation DEF (broadcast on an editor save) — refresh the cached def + "..." glyphs.
    private void HandleUpdateConversation(UpdateConversationPacket p)
    {
        if (!SlotValidation.IsValidConversationNum(p.ConvNum, _state.Limits.Conversations)) return;
        _state.SetConvDef(p.ConvNum, ToConversationRecord(p.Name, p.SpeakerNpc, p.RootNodeId, p.Nodes));
    }

    private void HandleSendShops(SendShopsPacket p)
    {
        foreach (var s in p.Shops)
        {
            if (!SlotValidation.IsValidShopNum(s.Num, _state.Limits.Shops)) continue;
            _state.ShopDefs[s.Num] = new ShopRecord
            {
                Name = s.Name,
                FixesItems = s.FixesItems,
                ShopType = s.ShopType,
                AllowBanking = s.AllowBanking,
            };
        }
    }

    // ── Live edits from the editor (broadcast on every save) ──────────────────
    // Without these, edited names/stats only appear after a full client reconnect,
    // because the only other carriers are the bulk Send*Packets sent on join.

    private void HandleUpdateItem(UpdateItemPacket p)
    {
        if (!SlotValidation.IsValidItemNum(p.ItemNum, _state.Limits.Items)) return;
        _state.Items[p.ItemNum] = new ItemRecord
        {
            Name = p.Name,
            Pic = p.Pic,
            ItemSheet = p.ItemSheet,
            Type = p.Type,
            Durability = p.Durability,
            VitalAmount = p.VitalAmount,
            Power = p.Power,
            Tier = p.Tier,
            NonTradeable = p.NonTradeable,
            NonListable = p.NonListable,
            NonMailable = p.NonMailable,
            DestroyOnDrop = p.DestroyOnDrop,
            NonJunkable = p.NonJunkable,
            Price = p.Price,
        };
    }

    private void HandleUpdateNpc(UpdateNpcPacket p)
    {
        if (!SlotValidation.IsValidNpcNum(p.NpcNum, _state.Limits.Npcs)) return;
        _state.NpcDefs[p.NpcNum] = new NpcRecord
        {
            Name = p.Name,
            Sprite = p.Sprite,
            SpriteSheet = p.SpriteSheet,
            Size = p.Size,   // footprint size class 1/2/3; drives the sprite/bar/hit-test scale
            Behavior = p.Behavior,
            SpawnSecs = p.SpawnSecs,
            EmitsLight = p.EmitsLight,
            Light = p.Light,
        };
        // Keeper-shop kind is a parallel array (not part of NpcDefs) — refresh it so a live shop/keeper edit
        // moves/relabels the $ glyph + interact routing without a client reconnect.
        _state.NpcKeeperShop[p.NpcNum] = p.KeeperShop;
    }

    private void HandleUpdateShop(UpdateShopPacket p)
    {
        if (!SlotValidation.IsValidShopNum(p.ShopNum, _state.Limits.Shops)) return;
        _state.ShopDefs[p.ShopNum] = new ShopRecord
        {
            Name = p.Name,
            FixesItems = p.FixesItems,
            ShopType = p.ShopType,
            AllowBanking = p.AllowBanking,
        };
        // If the player has this shop open, refresh the trade list in-place so
        // they see the new trades without re-entering the shop tile.
        if (_state.ActiveShopNum == p.ShopNum)
        {
            _state.ActiveBarters = p.Barters
                .Select(t => new ShopContentsPacket.BarterRow(t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
                .ToArray();
        }
    }

    // MapGroup defs. Bulk at join, then live per-group on an editor save. The client caches these and
    // resolves each map's effective inheritable values against them (ClientState.*Of), so a live group edit lands
    // with no map reload — the next frame's resolve just reads the updated group. Mirrors items/npcs/etc.
    private void HandleSendMapGroups(SendMapGroupsPacket p)
    {
        // Bulk snapshot: reset then repopulate so a group deleted server-side doesn't linger in the cache.
        Array.Clear(_state.MapGroups);
        foreach (var g in p.Groups)
        {
            if (!SlotValidation.IsValidMapGroupNum(g.Num, _state.Limits.MapGroups)) continue;
            _state.MapGroups[g.Num] = new MapGroupRecord
            {
                Index = g.Num, DisplayName = g.DisplayName, Moral = g.Moral, Music = g.Music,
                Indoors = g.Indoors, AlwaysLit = g.AlwaysLit, AlwaysDark = g.AlwaysDark, BootMap = g.BootMap, BootX = g.BootX, BootY = g.BootY,
            };
        }
    }

    private void HandleUpdateMapGroup(UpdateMapGroupPacket p)
    {
        if (!SlotValidation.IsValidMapGroupNum(p.GroupNum, _state.Limits.MapGroups)) return;
        _state.MapGroups[p.GroupNum] = new MapGroupRecord
        {
            Index = p.GroupNum, DisplayName = p.DisplayName, Moral = p.Moral, Music = p.Music,
            Indoors = p.Indoors, AlwaysLit = p.AlwaysLit, AlwaysDark = p.AlwaysDark, BootMap = p.BootMap, BootX = p.BootX, BootY = p.BootY,
        };
    }
}

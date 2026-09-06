using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

public sealed class JoinLeaveSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly PlayerSaver _saver;
    private readonly MovementSystem _movement;
    private readonly PartySystem _party;
    private readonly GuildSystem _guilds;
    private readonly MailSystem _mail;
    private readonly SocialSystem _social;
    private readonly TradeSystem _trade;
    private readonly QuestSystem _quests;
    private readonly ConversationSystem _conversations;
    private readonly TimeOfDaySystem _tod;
    private readonly WeatherSystem _weather;
    private readonly BloodSystem _blood;
    private readonly ILogger<JoinLeaveSystem> _logger;
    private readonly Configuration.ServerConfig _config;

    public JoinLeaveSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher,
                           PlayerSaver saver, MovementSystem movement,
                           PartySystem party, GuildSystem guilds, MailSystem mail, SocialSystem social, TradeSystem trade, QuestSystem quests,
                           ConversationSystem conversations,
                           TimeOfDaySystem tod, WeatherSystem weather, BloodSystem blood,
                           ILogger<JoinLeaveSystem> logger,
                           IClock? clock = null,
                           Configuration.ServerConfig? config = null)
        : base(dispatcher, clock: clock)
    {
        _config = config ?? Configuration.ServerConfig.Default;
        _world = world;
        _pm = pm;
        _saver = saver;
        _movement = movement;
        _party = party;
        _guilds = guilds;
        _mail = mail;
        _social = social;
        _trade = trade;
        _quests = quests;
        _conversations = conversations;
        _tod = tod;
        _weather = weather;
        _blood = blood;
        _logger = logger;
    }

    // ── Join game (called after UseChar selects a character) ──────────────────

    public void JoinGame(int index)
    {
        var sp = _pm[index];
        sp.InGame = true;
        _pm.NotifyRosterChanged();
        long joinUtc = NowUtc;
        sp.PlayTimeAnchorUtc = joinUtc;   // start banking this session's playtime
        sp.SessionStartUtc = joinUtc;     // session length → guild active-member accrual at logout

        var p = sp.Char;
        // Observer mode persists with the character, so a demotion taken while it was offline has to land
        // here — access is per-account and already stamped by the login path.
        if (p.GodMode && !p.MayUseGodMode) p.GodMode = false;

        // A character saved before the action bar existed has no "hotkeys" key at all, so the property
        // initializer already gives it a correct-length array; this covers the other case — a save made
        // when MaxHotkeys was a different width — so every read site can index 1..MaxHotkeys freely.
        p.Hotkeys = PlayerHotkey.Normalize(p.Hotkeys);

        // Return any items left escrowed by a trade that a crash/shutdown interrupted before the leave-path
        // could unwind it (normal disconnect already unwinds via OnPlayerGone). Done before the inventory is
        // sent below, so the player logs in with the returned items already in the bag.
        _trade.RecoverEscrowOnLogin(index);

        // Recalculate max vitals using class stats
        if (p.Class >= 1 && p.Class < _world.Classes.Length)
            StatFormulas.RefreshPlayerMaxVitals(p, _world.Classes[p.Class], _world.WeatherOn(p.Map));

        // Clamp vitals to max
        p.Hp = Math.Min(p.Hp, p.MaxHp);
        p.Mp = Math.Min(p.Mp, p.MaxMp);
        p.Sp = Math.Min(p.Sp, p.MaxSp);

        // Clear an expired PK timer on login. The broadcast — like every other chat message
        // emitted during JoinGame — is deferred until after SendWelcome so the joining player
        // reads the welcome/MOTD/who's-online block first, then sees the world chatter.
        bool pkExpiredOnLogin = false;
        if (p.PkExpiryUtc > 0 && p.PkExpiryUtc <= NowUtc)
        {
            p.PkExpiryUtc = 0;
            pkExpiredOnLogin = true;
        }

        // Compose the join broadcast color up front but defer the actual SendToAll until after
        // SendWelcome, so the joining player's chat reads: welcome → help-hint → MOTD → who's-online
        // → join broadcast → shop greeting. Other players just see the join broadcast.
        int joinColor = p.Access <= AdminLevel.Monitor ? GameColor.JoinLeft : GameColor.White;

        _dispatcher.SendTo(index, PacketBuilder.Welcome(index));
        CheckEquippedItems(index);

        // ── Send all game data ────────────────────────────────────────────────

        // Classes
        _dispatcher.SendTo(index, PacketBuilder.SendClasses(_world.Classes.Skip(1)));

        // Items. Skips unauthored slots, exactly as the NPC and spell builders below do — the client
        // assigns by num into a MaxItems-sized array, so a sparse list lands in the same places a dense
        // one would. Without the filter the join payload carries every empty slot up to MaxItems, which
        // is the one place raising that ceiling would cost real bandwidth per login.
        var itemList = Enumerable.Range(1, _world.Limits.Items)
            .Where(i => !string.IsNullOrEmpty(_world.Items[i].Name))
            .Select(i => (i, _world.Items[i]));
        _dispatcher.SendTo(index, PacketBuilder.SendItems(itemList));

        // NPCs
        _dispatcher.SendTo(index, BuildSendNpcs());

        // Shops
        _dispatcher.SendTo(index, BuildSendShops());

        // Spells
        _dispatcher.SendTo(index, BuildSendSpells());

        // Quests (definitions — like items/npcs; the per-player quest LOG follows via _quests.OnPlayerJoin below)
        _dispatcher.SendTo(index, BuildSendQuests());

        // Conversations (definitions — like quests; the per-character spoken-log follows via _conversations.OnPlayerJoin)
        _dispatcher.SendTo(index, BuildSendConversations());

        // Map groups: shipped before any map so the client can resolve a map's effective inheritable
        // values (Moral/Music/Indoors/lighting/display name) against its cached group. Kept fresh live by
        // UpdateMapGroupPacket on an editor save.
        _dispatcher.SendTo(index, BuildSendMapGroups());

        // Player inventory
        _dispatcher.SendTo(index, BuildSendInventory(p));

        // Equipped gear
        _dispatcher.SendTo(index, new EquippedGearPacket
        {
            Index = index,
            Armor = p.ArmorSlot,
            Weapon = p.WeaponSlot,
            Helmet = p.HelmetSlot,
            Shield = p.ShieldSlot
        });

        // Player spells (1-based array; send slots 1..MaxPlayerSpells)
        _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = p.Spell[1..], PreparedSpell = p.PreparedSpell });

        // Action bar. Sent here rather than in SendJoinData, which also runs on every warp — the bar only
        // changes when the player edits it, and each edit is echoed by its own handler.
        _dispatcher.SendTo(index, PacketHandler.BuildHotkeysPacket(p));

        // 🔴 The mailbox and the social lists, on the same terms and for a sharper reason: SendRegionSync
        // runs on every SEAM CROSSING as well as every warp, and neither of these changes because the
        // player walked over a border. The mailbox is the whole inbox AND outbox with every attachment —
        // at a sprint, crossing seams several times a second, re-sending it is a packet burst the client
        // drains in one frame. Both are re-pushed by their own handlers whenever they actually change.
        //
        // Guild sync stays in SendRegionSync: unlike these, it IS regional — it walks the observer set and
        // teaches the client the guild of every player newly in view, which is exactly what a crossing
        // changes.
        _mail.SyncTo(index);
        _social.SyncTo(index);

        // Vitals
        _dispatcher.SendTo(index, PacketBuilder.SendHp(index, p.Hp, p.MaxHp));
        _dispatcher.SendTo(index, PacketBuilder.SendMp(index, p.Mp, p.MaxMp));
        _dispatcher.SendTo(index, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));

        // Stats
        _dispatcher.SendTo(index, PacketBuilder.SendStats(p));

        // World state
        _dispatcher.SendTo(index, PacketBuilder.Weather(_world.Weather));
        _dispatcher.SendTo(index, PacketBuilder.TimeOfDay(_world.TimePhase, _world.TimeProgress));

        // Pull the saved tile onto ground that exists — see GameWorld.RepairPosition. Written back to the
        // record, so a corrected position persists with the character.
        (p.Map, p.X, p.Y) = _world.RepairPosition(p.Map, p.X, p.Y, Config.Spawn.HomeFor(p));

        // Warp to saved location (triggers map loading flow via CheckForMap).
        // suppressMapGreeting: true so the spawn-map's NPC chatter can be re-issued AFTER SendWelcome
        // and land last in the joining player's chat.  destLayer: p.Layer restores the PERSISTED layer so a relog
        // on a bridge stays on the bridge (PlayerWarp re-fits if that tile is no longer walkable on that layer).
        _movement.PlayerWarp(index, p.Map, p.X, p.Y, suppressMapGreeting: true, destLayer: p.Layer);

        // Welcome / help-hint / MOTD / who's-online — the joining player only.
        SendWelcome(index);

        // Re-establish kernel tracking for in-progress quests and push the quest log (after the client's set up).
        _quests.OnPlayerJoin(index);

        // Push the character's spoken-conversation set (colors the overhead "..." glyphs yellow/gray).
        _conversations.OnPlayerJoin(index);

        // Then the player-visible broadcasts (everyone, including the joining player).
        _dispatcher.SendLocalizedChatToAll(
            ServerStrings.JoinLeave_JoinBroadcast,
            new ChatMetadata(joinColor, ChatChannel.JoinLeaveNotice),
            ("Name", p.TrimmedName), ("GameName", _config.GameName));
        if (pkExpiredOnLogin)
        {
            _dispatcher.SendLocalizedChatToAll(
                ServerStrings.PkExpirySystem_CrimesFaded,
                new ChatMetadata(GameColor.BrightGreen, ChatChannel.System),
                ("PlayerName", p.TrimmedName));
        }

        // Finally the map-enter greeting, so it reads as the last chat line on login (no-op if the map has none).
        _movement.OnJoinMap(index);

        // Final in-game flag
        _dispatcher.SendTo(index, PacketBuilder.PlayerInGame());
        _dispatcher.SendToAll(PacketBuilder.PlayersOnline(_pm.TotalOnline));
    }

    // ── Called when client confirms map is ready (after CheckForMap flow) ────

    public void SendJoinData(int index)
    {
        _pm[index].GettingMap = false;
        var p = _pm[index].Char;
        // Own data — only on the join/warp handshake (sets the client's initial position/sprite).
        // A seamless crossing already knows its own position client-side, so its re-sync omits this
        // to avoid overwriting the client's predicted move (which would rubber-band under latency).
        _dispatcher.SendTo(index, PacketBuilder.PlayerData(index, p, p.Map, _pm[index].PkGraceUntilUtc, _pm[index].AggressorUntilUtcNow, godMode: _pm[index].Char.GodMode));
        SendRegionSync(index);
    }

    /// <summary>
    /// Syncs the player's whole observable region to their client: every observable player (both
    /// directions), the center map's entities, and the 8 neighbor maps (cache-aware) with their
    /// entities.  Used by the join/warp handshake and, without ever blocking input, by a seamless
    /// border crossing once the client has shifted its grid and asked to be re-synced.
    /// </summary>
    public void SendRegionSync(int index)
    {
        var sp = _pm[index];
        var p = sp.Char;

        // Seamless world: sync players across the whole observable region, not just the same
        // map.  A player is mutually visible when one observes the other's map.  Each side uses
        // that player's OWN map number so neighbor players render at the right grid cell.
        long now = Environment.TickCount64;
        int joinerMsSinceCombat = PacketBuilder.MsSinceCombat(_pm[index].CombatExpiresAt, now, CombatSystem.CombatDurationMs);

        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (i == index) continue;
            if (!_pm[i].IsPlaying) continue;

            var ep = _pm[i].Char;
            int existingMsSinceCombat = PacketBuilder.MsSinceCombat(_pm[i].CombatExpiresAt, now, CombatSystem.CombatDurationMs);

            // Tell existing player i about this player, if i can see this player's map.
            if (_world.IsObserving(i, p.Map))
            {
                _dispatcher.SendTo(i, PacketBuilder.JoinMap(index));
                _dispatcher.SendTo(i, PacketBuilder.PlayerData(index, p, p.Map, _pm[index].PkGraceUntilUtc, _pm[index].AggressorUntilUtcNow, godMode: _pm[index].Char.GodMode));
                _dispatcher.SendTo(i, PacketBuilder.SendHp(index, p.Hp, p.MaxHp, msSinceCombat: joinerMsSinceCombat));
                _dispatcher.SendTo(i, PacketBuilder.SendMp(index, p.Mp, p.MaxMp));
                _dispatcher.SendTo(i, PacketBuilder.SendSp(index, p.Sp, p.MaxSp));
            }

            // Tell this player about existing player i, if this player can see i's map.
            if (_world.IsObserving(index, ep.Map))
            {
                _dispatcher.SendTo(index, PacketBuilder.JoinMap(i));
                _dispatcher.SendTo(index, PacketBuilder.PlayerData(i, ep, ep.Map, _pm[i].PkGraceUntilUtc, _pm[i].AggressorUntilUtcNow, godMode: _pm[i].Char.GodMode));
                _dispatcher.SendTo(index, PacketBuilder.SendHp(i, ep.Hp, ep.MaxHp, msSinceCombat: existingMsSinceCombat));
                _dispatcher.SendTo(index, PacketBuilder.SendMp(i, ep.Mp, ep.MaxMp));
                _dispatcher.SendTo(index, PacketBuilder.SendSp(i, ep.Sp, ep.MaxSp));
            }
        }

        // Sync guild membership across the observable area (the joiner learns others' guilds and
        // observers learn the joiner's) — change-broadcasts alone don't reach a fresh login, and a seam
        // crossing brings players into view the same way a login does. The mailbox and social lists are
        // NOT here: they belong to the account, not the region, and are sent once at JoinGame.
        _guilds.SyncOnJoin(index);

        SendMapItemsSnapshot(index, p.Map);
        _blood.SendSnapshot(index, p.Map);
        _dispatcher.SendTo(index, BuildMapNpcs(_world, p.Map));
        SendTraversalNpcs(index, p.Map);
        SendOpenDoors(index, p.Map);

        _world.PlayersOnMap[p.Map] = true;

        // Seamless scrolling: pre-load the 8 surrounding maps so border crossings
        // are instant and the camera can scroll into them.
        SendNeighborMaps(index, p.Map);
    }

    /// <summary>
    /// Sends the current open-door state of a map so the client's door tracking (used for
    /// collision prediction) is accurate from the moment it starts observing the map — rather
    /// than assuming every door is shut until the next open/close event arrives.
    /// </summary>
    private void SendOpenDoors(int index, int mapNum)
    {
        // Both planes' open doors, so the client predicts each correctly. Reads the map's open-door set
        // directly: a map with nothing open sends nothing and costs nothing, whatever its size.
        foreach (var ((x, y, layer), _) in _world.TempTiles[mapNum].OpenDoors)
            _dispatcher.SendTo(index, new MapKeyPacket { MapNum = mapNum, X = x, Y = y, Open = true, Layer = layer });
    }

    /// <summary>
    /// Pushes refreshed data for <paramref name="editedMapNum"/> to every observer.  Occupants get a
    /// full reload (<see cref="MovementSystem.PlayerWarp"/> blocks input until they confirm); observers
    /// on a neighbor cell get a targeted CheckForMap + items + NPCs + traversal + doors for the cell
    /// where the edited map sits in their grid.  Called after an editor save so a live tile/connection
    /// change shows up without requiring a relog from anyone in the affected region.
    /// </summary>
    public void BroadcastMapRefresh(int editedMapNum)
    {
        if (editedMapNum <= 0 || editedMapNum > _world.Limits.Maps) return;
        // Snapshot the observer set: PlayerWarp's RemoveObserver/AddObserver mutates it mid-iteration.
        var observers = _world.MapObservers[editedMapNum].ToArray();
        foreach (int i in observers)
        {
            if (!_pm[i].IsPlaying) continue;
            var p = _pm[i].Char;
            if (p.Map == editedMapNum)
            {
                // Occupant — same-map "warp" reloads tiles + region and re-blocks input until confirm.
                // The tile is repaired first: the save that triggered this may have shrunk the map.
                var (_, rx, ry) = _world.RepairPosition(editedMapNum, p.X, p.Y, Config.Spawn.HomeFor(p));
                _movement.PlayerWarp(i, editedMapNum, rx, ry);
                continue;
            }
            // Neighbor observer — locate the cell where the edited map sits in their grid and push
            // a per-cell refresh, mirroring the per-cell push SendNeighborMaps does on a fresh join.
            var cell = WorldCoordHelper.GridPosition(_world.Maps, p.Map, editedMapNum);
            if (cell is null) continue; // grid no longer references it (rare — observer set lag)
            var (col, row) = cell.Value;
            _dispatcher.SendTo(i, new CheckForMapPacket
            {
                MapNum = editedMapNum,
                Revision = _world.Maps[editedMapNum].Revision,
                Col = col,
                Row = row,
            });
            SendMapItemsSnapshot(i, editedMapNum);
            _dispatcher.SendTo(i, BuildMapNpcs(_world, editedMapNum));
            SendTraversalNpcs(i, editedMapNum);
            SendOpenDoors(i, editedMapNum);
        }
    }

    /// <summary>
    /// Tells the client which 8 maps surround <paramref name="centerMapNum"/> and at what
    /// revision, tagged with their 3×3 grid cell.  The client serves unchanged maps from its
    /// persistent disk cache and only downloads (via NeedNeighborMap) the ones it lacks — so
    /// repeated border crossings cost almost no map bandwidth.  Center is sent by the join flow.
    /// </summary>
    private void SendNeighborMaps(int index, int centerMapNum)
    {
        var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, centerMapNum);
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 3; row++)
            {
                if (col == 1 && row == 1) continue; // center already sent
                int mapNum = grid[col, row];
                if (mapNum <= 0 || mapNum > _world.Limits.Maps) continue;
                _dispatcher.SendTo(index, new CheckForMapPacket
                {
                    MapNum = mapNum,
                    Revision = _world.Maps[mapNum].Revision,
                    Col = col,
                    Row = row,
                });
                // Snapshot this neighbor's live items + NPCs (tagged with mapNum so the client
                // routes them to the right grid cell).  The CheckForMap above arrives first, so
                // the client already knows which cell this map occupies.
                SendMapItemsSnapshot(index, mapNum);
                _blood.SendSnapshot(index, mapNum);
                _dispatcher.SendTo(index, BuildMapNpcs(_world, mapNum));
                SendTraversalNpcs(index, mapNum);
                SendOpenDoors(index, mapNum);
            }
        }
    }

    // Snapshot any visiting (chasing) NPCs currently on a map so a newly-observing player sees an
    // in-progress chase immediately, not only on the guest's next move/attack broadcast.
    private void SendTraversalNpcs(int index, int mapNum)
    {
        long now = Environment.TickCount64;
        var list = _world.MapTraversalNpcs[mapNum];
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t.Num <= 0) continue;
            var npc = _world.Npcs[t.Num];
            _dispatcher.SendTo(index, new TraversalNpcPacket
            {
                SpawnMapNum = t.SpawnMapNum,
                SpawnSlot = t.SpawnSlot,
                CurrentMapNum = t.CurrentMapNum,
                Num = t.Num,
                X = t.X,
                Y = t.Y,
                Dir = t.Dir,
                Movement = MovementType.None,
                Hp = Math.Max(t.Hp, 0),
                MaxHp = _world.EffectiveNpcMaxHp(npc),
                MsSinceCombat = PacketBuilder.MsSinceCombat(t.CombatExpiresAt, now, CombatSystem.CombatDurationMs),
                HasTarget = t.Target > 0,
                Attacking = false,
                Layer = t.Layer,
            });
        }
    }

    // ── Leave game ────────────────────────────────────────────────────────────

    // Runs on the game thread (posted by the disconnect path, or called inline from logout-to-char-select).
    // Synchronous: the character save is handed to PlayerSaver to run off-thread so file I/O never stalls
    // the game loop.
    public void LeftGame(int index)
    {
        var sp = _pm[index];
        _pm.NotifyRosterChanged();
        if (!sp.InGame)
        {
            ClearPlayer(index);
            return;
        }

        // Cancel any live trade / pending invite and return escrowed items before the ghost branch or save.
        _trade.OnPlayerGone(index);
        _quests.OnPlayerGone(index);   // stop kernel tracking; the persisted quest state carries progress
        sp.ViewingMarket = false;   // a disconnect / ghost isn't a live market browser

        // Stamp the guild roster's last-seen before the ghost branch can return: a combat ghost is
        // still a disconnected account, so either way this is the moment the member was last seen.
        _guilds.StampMemberLastSeen(index);

        // If the player disconnects while in combat, leave a ghost in the world — UNLESS they're dead. A corpse
        // never becomes a combat ghost: it always takes the normal-leave path below so the dead state (Dead +
        // RespawnReadyUtc) is persisted and the body is removed via LeaveMap, and a relogin re-opens the death
        // panel. A dead ghost would be uncleanable — RegenerationSystem skips Dead players, so its combat timer
        // never expires to trigger ClearGhost — which is also what let a corpse re-stamped into combat block logout.
        long now = Environment.TickCount64;
        if (sp.IsInCombat(now) && !sp.Char.Dead)
        {
            BecomeGhost(index);
            return;
        }

        sp.InGame = false;
        var p = sp.Char;

        if (_pm.GetTotalMapPlayers(p.Map) == 0)
            _world.PlayersOnMap[p.Map] = false;

        // Check boot map: if the map (or its MapGroup) boots players to a safe map on logout, move there
        // before saving. Read the effective boot destination via the helpers BEFORE reassigning p.Map.
        int bootMap = _world.BootMapOf(p.Map);
        if (bootMap > 0)
        {
            int bootX = _world.BootXOf(p.Map);
            int bootY = _world.BootYOf(p.Map);
            // A boot point is authored content, so one naming no tile is reported and ignored: the
            // character is saved standing where they logged out, which is somewhere that exists.
            if (_world.IsRealMap(bootMap) && _world.Maps[bootMap].Contains(bootX, bootY))
            {
                p.X = bootX;
                p.Y = bootY;
                p.Map = bootMap;
            }
            else
            {
                _logger.LogWarning(
                    "Map #{Map} boots to map #{BootMap} ({X},{Y}), which is not a tile that exists - {Name} was left where they logged out.",
                    p.Map, bootMap, bootX, bootY, p.TrimmedName);
            }
        }

        _party.DisbandParty(index);

        _logger.LogInformation("{Login}/{Name} has left {GameName}.", sp.Login, p.TrimmedName, _config.GameName);

        sp.BankPlaytime(NowUtc);   // final playtime bank before the logout save
        _saver.SaveCharInBackground(sp.Login, sp.CharNum, p.Clone(), sp.CloneBank());

        int leaveColor = p.Access <= AdminLevel.Monitor ? GameColor.JoinLeft : GameColor.White;
        _dispatcher.SendLocalizedChatToAll(
            ServerStrings.JoinLeave_LeaveBroadcast,
            new ChatMetadata(leaveColor, ChatChannel.JoinLeaveNotice),
            ("Name", p.TrimmedName), ("GameName", _config.GameName));

        SendToMapBut(_world, p.Map, index, PacketBuilder.LeaveMap(index));
        _dispatcher.SendToAll(PacketBuilder.PlayersOnline(_pm.TotalOnline));

        int leavingMap = p.Map;
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            _world.MapNpcs[leavingMap, i].DamageByPlayer[index] = 0;

        _world.RemoveObserverFromAll(index);
        DropOthersTargetsOnPlayer(index);
        ClearPlayer(index);
    }

    // A player's slot is being freed (logout / ghost cleared) — clear any other player's lock on this
    // index so whoever next takes the slot can't inherit a stale target. The client also drops on the
    // LeaveMap it receives; this makes it server-authoritative, matching the NPC target-drop treatment.
    private void DropOthersTargetsOnPlayer(int leavingIndex)
    {
        for (int i = 1; i <= _pm.Slots; i++)
        {
            if (i == leavingIndex) continue;
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            if (sp.TargetType == 0 && sp.Target == leavingIndex)
            {
                sp.Target = 0;
                sp.TargetType = 0;
                sp.TargetMap = 0;
            }
        }
    }

    // ── Ghost management ──────────────────────────────────────────────────────

    private void BecomeGhost(int index)
    {
        var sp = _pm[index];
        var p = sp.Char;

        _party.DisbandParty(index);

        _saver.SaveCharInBackground(sp.Login, sp.CharNum, p.Clone(), sp.CloneBank());

        int leaveColor = p.Access <= AdminLevel.Monitor ? GameColor.JoinLeft : GameColor.White;
        _dispatcher.SendLocalizedChatToAll(
            ServerStrings.JoinLeave_LeaveBroadcast,
            new ChatMetadata(leaveColor, ChatChannel.JoinLeaveNotice),
            ("Name", p.TrimmedName), ("GameName", _config.GameName));
        // No PlayersOnline update here — the ghost still counts as a player in the world.

        // Keep InGame = true and do not broadcast LeaveMap — the ghost body remains on the map.
        sp.IsConnected = false;
        sp.IsGhost = true;
        sp.GettingMap = false;

        _logger.LogInformation("{Login}/{Name} disconnected in combat — ghost remains on map.", sp.Login, p.TrimmedName);
    }

    /// <summary>
    /// Removes a combat ghost from the world. Called when a ghost's combat timer expires or it dies.
    /// The caller is responsible for having already applied any death/penalty state before calling this.
    /// </summary>
    public void ClearGhost(int index)
    {
        var sp = _pm[index];
        if (!sp.IsGhost) return;

        var p = sp.Char;
        var bankSnapshot = sp.CloneBank();   // account bank — snapshot before the slot is wiped below
        int mapNum = p.Map;
        string login = sp.Login;
        int charNum = sp.CharNum;

        sp.InGame = false;
        sp.IsGhost = false;

        if (_pm.GetTotalMapPlayers(mapNum) == 0)
            _world.PlayersOnMap[mapNum] = false;

        _world.RemoveObserverFromAll(index);
        DropOthersTargetsOnPlayer(index);

        // Clear NPC damage contributions for this slot.
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
            _world.MapNpcs[mapNum, i].DamageByPlayer[index] = 0;

        // Only broadcast LeaveMap if the ghost was still on its original map (not mid-death-warp).
        if (!sp.GettingMap)
            SendToMapBut(_world, mapNum, index, PacketBuilder.LeaveMap(index));

        // No chat notification here — the "has left" message was already sent when the ghost was created.
        _dispatcher.SendToAll(PacketBuilder.PlayersOnline(_pm.TotalOnline));

        // Reset slot fields.
        sp.CharNum = 0;
        sp.Login = "";
        sp.Password = "";
        sp.GettingMap = false;
        sp.GhostTransferSlot = 0;
        sp.PartyPlayer = 0;
        sp.InParty = false;
        sp.PartyStarter = false;
        sp.CombatExpiresAt = 0;
        sp.WasInCombat = false;
        sp.AttackTimer = 0;
        sp.MoveAllowedAt = 0;
        sp.Target = 0;
        sp.TargetType = 0;
        sp.ClearActiveShop();
        sp.ClearActiveQuestNpc();
        sp.ClearDamageCredit();
        for (int i = 1; i <= Constants.MaxChars; i++)
            sp.Chars[i] = new PlayerRecord();
        sp.Bank = AccountRecord.NewBank();

        _saver.SaveCharInBackground(login, charNum, p.Clone(), bankSnapshot);

        _logger.LogInformation("{Login} ghost cleared from map {Map}.", login, mapNum);
    }

    private void CheckEquippedItems(int index)
    {
        var p = _pm[index].Char;

        void ValidateSlot(ref int slot, ItemType expectedType)
        {
            if (slot == 0) return;
            int itemNum = p.Inv[slot].Num;
            if (itemNum <= 0 || _world.Items[itemNum].Type != expectedType)
                slot = 0;
        }

        int slot;

        slot = p.WeaponSlot;
        ValidateSlot(ref slot, ItemType.Weapon);
        p.WeaponSlot = slot;
        slot = p.ArmorSlot;
        ValidateSlot(ref slot, ItemType.Armor);
        p.ArmorSlot = slot;
        slot = p.HelmetSlot;
        ValidateSlot(ref slot, ItemType.Helmet);
        p.HelmetSlot = slot;
        slot = p.ShieldSlot;
        ValidateSlot(ref slot, ItemType.Shield);
        p.ShieldSlot = slot;
        if (p.PreparedSpell < 0 || p.PreparedSpell > Constants.MaxPlayerSpells ||
            (p.PreparedSpell > 0 && p.Spell[p.PreparedSpell] <= 0))
        {
            p.PreparedSpell = 0;
        }
    }

    // The welcome batch (welcome line, /help hint, MOTD, who's-online) is tagged Always so it bypasses
    // every per-tab filter on the client. A player can never accidentally hide their own login.
    private void SendWelcome(int index)
    {
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.JoinLeave_Welcome,
            new ChatMetadata(GameColor.BrightBlue, ChatChannel.Always),
            ("GameName", _config.GameName), ("Major", Constants.ClientMajor), ("Minor", Constants.ClientMinor), ("Revision", Constants.ClientRevision));
        _dispatcher.SendLocalizedChatTo(index, ServerStrings.JoinLeave_HelpHint,
            new ChatMetadata(GameColor.Cyan, ChatChannel.Always));

        if (!string.IsNullOrWhiteSpace(_world.Motd))
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.JoinLeave_Motd,
                new ChatMetadata(GameColor.BrightCyan, ChatChannel.Always),
                ("Motd", _world.Motd));
        }

        _dispatcher.SendLocalizedChatTo(index, _tod.WelcomeKey(),
            new ChatMetadata(GameColor.Yellow, ChatChannel.Always));
        _dispatcher.SendLocalizedChatTo(index, _weather.WelcomeKey(),
            new ChatMetadata(GameColor.Yellow, ChatChannel.Always));

        SendWhosOnline(index);
    }

    public void SendWhosOnline(int index)
    {
        string[] names = Enumerable.Range(1, _pm.Slots)
            .Where(i => i != index && _pm[i].IsPlaying)
            .Select(i => _pm[i].Char.Name.Trim())
            .ToArray();
        if (names.Length == 0)
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.JoinLeave_NoOtherPlayers,
                new ChatMetadata(GameColor.Who, ChatChannel.Always));
        }
        else
        {
            _dispatcher.SendLocalizedChatTo(index, ServerStrings.JoinLeave_OtherPlayers,
                new ChatMetadata(GameColor.Who, ChatChannel.Always),
                ("Count", names.Length), ("Names", string.Join(", ", names)));
        }
    }

    private static void ClearPlayer(int index)
    {
        // Host layer closes the socket and resets the slot via IPacketDispatcher.Disconnect.
    }

    // ── Packet builders ───────────────────────────────────────────────────────

    private SendNpcsPacket BuildSendNpcs()
    {
        var npcs = Enumerable.Range(1, _world.Limits.Npcs)
            .Where(i => !string.IsNullOrEmpty(_world.Npcs[i].Name))
            .Select(i => new SendNpcsPacket.NpcData(
                i,
                _world.Npcs[i].Name,
                _world.Npcs[i].Sprite,
                _world.Npcs[i].EffectiveSize,
                _world.Npcs[i].Behavior,
                _world.Npcs[i].SpawnSecs,
                _world.Npcs[i].Spd,
                _world.Npcs[i].EmitsLight,
                _world.Npcs[i].Light,
                _world.KeeperShopKind(i),
                _world.Npcs[i].SpriteSheet))
            .ToArray();
        return new SendNpcsPacket { Npcs = npcs };
    }

    private SendShopsPacket BuildSendShops()
    {
        var shops = Enumerable.Range(1, _world.Limits.Shops)
            .Where(i => !string.IsNullOrEmpty(_world.Shops[i].Name))
            .Select(i => new SendShopsPacket.ShopData(
                i,
                _world.Shops[i].Name,
                _world.Shops[i].FixesItems,
                _world.Shops[i].ShopType,
                _world.Shops[i].AllowBanking))
            .ToArray();
        return new SendShopsPacket { Shops = shops };
    }

    // Quest DEFINITIONS the client caches (like items/npcs) to render the log/dialog + drive the ?/! glyphs. Only
    // non-empty quests are sent; the lists are deep-cloned so the serialized snapshot can't tear if the game
    // thread re-authors a def. TurnInNpc is the RAW value (0 = same as giver); the client resolves the effective.
    private SendQuestsPacket BuildSendQuests()
    {
        var quests = Enumerable.Range(1, _world.Limits.Quests)
            .Where(i => _world.Quests[i].TrimmedName.Length > 0)
            .Select(i =>
            {
                var q = _world.Quests[i];
                return new SendQuestsPacket.QuestData
                {
                    Num = i,
                    Name = q.Name,
                    Description = q.Description,
                    Objectives = q.Objectives.Select(o => o.Clone()).ToList(),
                    ReqLevel = q.ReqLevel,
                    ReqStr = q.ReqStr,
                    ReqDef = q.ReqDef,
                    ReqSpd = q.ReqSpd,
                    ReqInt = q.ReqInt,
                    AllowedClasses = q.AllowedClasses is null ? null : new List<short>(q.AllowedClasses),
                    PrereqQuest = q.PrereqQuest,
                    RewardExp = q.RewardExp,
                    RewardItems = q.RewardItems.Select(r => r.Clone()).ToList(),
                    RepeatRewardExp = q.RepeatRewardExp,
                    RepeatRewardItems = q.RepeatRewardItems.Select(r => r.Clone()).ToList(),
                    GiverNpc = q.GiverNpc,
                    TurnInNpc = q.TurnInNpc,
                    Repeatable = q.Repeatable,
                    Cadence = q.Cadence,
                };
            })
            .ToList();
        return new SendQuestsPacket { Quests = quests };
    }

    // Conversation DEFINITIONS the client caches (like quests) to walk a dialogue tree locally + color the "..."
    // glyphs. Only non-empty conversations are sent; the node/choice lists are deep-cloned so the serialized
    // snapshot can't tear if the game thread re-authors a def.
    private SendConversationsPacket BuildSendConversations()
    {
        var convs = Enumerable.Range(1, _world.Limits.Conversations)
            .Where(i => _world.Conversations[i].TrimmedName.Length > 0)
            .Select(i =>
            {
                var c = _world.Conversations[i];
                return new SendConversationsPacket.ConvData
                {
                    Num = i,
                    Name = c.Name,
                    SpeakerNpc = c.SpeakerNpc,
                    RootNodeId = c.RootNodeId,
                    Nodes = c.Nodes.Select(n => n.Clone()).ToList(),
                };
            })
            .ToList();
        return new SendConversationsPacket { Conversations = convs };
    }

    // The MapGroup defs the client caches to resolve member maps' effective inheritable values.
    // Only existing groups are sent (sparse Dictionary); a map referencing an absent group resolves to no-group
    // (its own raw values / hard defaults), exactly as the server's GameWorld.*Of helpers do.
    private SendMapGroupsPacket BuildSendMapGroups()
    {
        var groups = _world.MapGroups.Values
            .Select(g => new SendMapGroupsPacket.GroupData(
                g.Index,
                g.DisplayName,
                g.Moral,
                g.Music,
                g.Indoors,
                g.AlwaysLit,
                g.AlwaysDark,
                g.BootMap,
                g.BootX,
                g.BootY))
            .ToArray();
        return new SendMapGroupsPacket { Groups = groups };
    }

    private SendSpellsPacket BuildSendSpells()
    {
        var spells = Enumerable.Range(1, _world.Limits.Spells)
            .Where(i => !string.IsNullOrEmpty(_world.Spells[i].Name))
            .Select(i => new SendSpellsPacket.SpellData(
                i,
                _world.Spells[i].Name,
                _world.Spells[i].AllowedClasses is null ? null : new List<short>(_world.Spells[i].AllowedClasses!),
                _world.Spells[i].Type,
                _world.Spells[i].VitalAmount,
                _world.Spells[i].ItemNum,
                _world.Spells[i].ItemQuantity,
                _world.Spells[i].IntReq,
                _world.Spells[i].LevelReq))
            .ToArray();
        return new SendSpellsPacket { Spells = spells };
    }

    private static SendInventoryPacket BuildSendInventory(PlayerRecord p)
    {
        // 1-based inventory slots (1..MaxInv)
        var slots = Enumerable.Range(1, Constants.MaxInv)
            .Select(i => new SendInventoryPacket.InvSlotData(
                i, p.Inv[i].Num, p.Inv[i].Quantity, p.Inv[i].Dur))
            .ToArray();
        return new SendInventoryPacket { Slots = slots };
    }

    // Max items per MapItemsPacket sent during a map snapshot.  At ~50 bytes of JSON per item this
    // caps individual packet payloads at ~3 KB, so a heavily-cluttered map (hundreds of drops) lands
    // as a sequence of bounded frames rather than one giant line.  Per-event broadcasts (single spawn
    // or remove) ignore this since they're already 1-item packets.
    private const int MapItemsSnapshotChunkSize = 50;

    private void SendMapItemsSnapshot(int index, int mapNum)
    {
        var list = _world.MapItems[mapNum];
        if (list.Count == 0)
        {
            // Always send at least one (empty) snapshot so the client clears any prior state.
            _dispatcher.SendTo(index, new MapItemsPacket { MapNum = mapNum, Items = [] });
            return;
        }

        var buffer = new List<MapItemsPacket.MapItemData>(Math.Min(list.Count, MapItemsSnapshotChunkSize));
        for (int i = 0; i < list.Count; i++)
        {
            var mi = list[i];
            buffer.Add(MapItemsPacket.MapItemData.From(mi, Environment.TickCount64));
            if (buffer.Count == MapItemsSnapshotChunkSize)
            {
                _dispatcher.SendTo(index, new MapItemsPacket { MapNum = mapNum, Items = buffer.ToArray() });
                buffer.Clear();
            }
        }
        if (buffer.Count > 0)
            _dispatcher.SendTo(index, new MapItemsPacket { MapNum = mapNum, Items = buffer.ToArray() });
    }

    // Static so the (already TimeOfDaySystem-dependent) JoinLeaveSystem and TimeOfDaySystem can both build
    // the snapshot without a DI cycle — TimeOfDaySystem re-broadcasts it on Night boundaries to re-scale
    // client HP bars. Reads only world state.
    public static MapNpcsPacket BuildMapNpcs(GameWorld world, int mapNum)
    {
        long now = Environment.TickCount64;
        // 1-based map NPC slots (1..MaxMapNpcs)
        var npcs = Enumerable.Range(1, Constants.MaxMapNpcs)
            .Where(i => world.MapNpcs[mapNum, i].Num > 0)
            .Select(i =>
            {
                var mn = world.MapNpcs[mapNum, i];
                var npc = world.Npcs[mn.Num];
                return new MapNpcsPacket.MapNpcData(
                    i, mn.Num,
                    mn.Hp, world.EffectiveNpcMaxHp(npc),
                    mn.Mp, world.EffectiveNpcMaxMp(npc),
                    mn.Sp, world.EffectiveNpcMaxSp(npc),
                    mn.X, mn.Y, mn.Dir,
                    PacketBuilder.MsSinceCombat(mn.CombatExpiresAt, now, CombatSystem.CombatDurationMs),
                    mn.Target > 0, mn.Layer);
            })
            .ToArray();
        return new MapNpcsPacket { MapNum = mapNum, Npcs = npcs };
    }
}

namespace Mirage.Shared.Protocol;

/// <summary>
/// String constants for the "cmd" discriminator field in every JSON packet.
/// </summary>
public static class PacketNames
{
    // ── C→S: pre-login ──────────────────────────────────────────────────────
    public const string GetClasses = "getclasses";
    public const string NewAccount = "newaccount";
    public const string DelAccount = "delaccount";
    public const string ChangePassword = "changepass";
    public const string Login = "login";
    public const string AddChar = "addchar";
    public const string DelChar = "delchar";
    public const string UseChar = "usechar";

    // ── C→S: in-game ────────────────────────────────────────────────────────
    public const string LogoutToCharSelect = "logouttocharselect";
    public const string SetLanguage = "setlanguage";
    public const string SayMsg = "saymsg";
    public const string EmoteMsg = "emotemsg";
    public const string YellMsg = "yellmsg";
    public const string BroadcastMsg = "broadcastmsg";
    public const string NoticeMsg = "noticemsg";
    public const string AdminMsg = "adminmsg";
    public const string PlayerMsg = "playermsg";
    public const string PlayerMove = "playermove";
    public const string PlayerDir = "playerdir";
    public const string UseItem = "useitem";
    public const string Attack = "attack";
    public const string Search = "search";
    public const string NpcInteract = "npcinteract";   // C→S: interact with an NPC (attack-key at a roled NPC, or right-click within r=5)
    public const string DropTarget = "droptarget";
    public const string TrainStats = "trainstats";
    public const string RequestNewMap = "requestnewmap";
    public const string MapData = "mapdata";
    public const string NeedMap = "needmap";
    public const string NeedNeighborMap = "needneighbormap";
    public const string RequestRegionSync = "reqregionsync";
    public const string MapGetItem = "mapgetitem";
    public const string MapPickUp = "mappickup";
    public const string MapPickUpAll = "mappickupall";
    public const string MapDropItem = "mapdropitem";
    public const string MapDropBulk = "mapdropbulk";
    public const string SortInventory = "sortinventory";
    public const string GetStats = "getstats";
    public const string ShopBarter = "shopbarter";                // C→S: take one row from a shop's BARTER table (item → item)
    public const string ShopBuy = "shopbuy";     // C→S: buy one entry from a shop's SALES list (gold → item)
    public const string ShopSell = "shopsell";   // C→S: sell an inventory slot to the open shop (item → gold)
    public const string FixItem = "fixitem";
    public const string Party = "party";
    public const string JoinParty = "joinparty";
    public const string LeaveParty = "leaveparty";
    public const string Spells = "spells";
    public const string Cast = "cast";
    public const string SetPreparedSpell = "setpreparedspell";
    public const string ForgetSpell = "forgetspell";
    public const string SetHotkey = "sethotkey";
    public const string RequestLocation = "requestlocation";
    public const string WhoIsOnline = "whosonline";
    public const string PlayerInfoRequest = "playerinforequest";
    public const string PlayedRequest = "playedrequest";
    public const string HomeRequest = "homerequest";
    public const string HomeCooldownRequest = "homecdrequest";
    public const string Roll = "roll";

    // ── C→S: guild ──────────────────────────────────────────────────────────
    public const string GuildCreate = "guildcreate";
    public const string GuildDisband = "guilddisband";
    public const string GuildOfferInitiate = "guildofferinit";
    public const string GuildOfferRespond = "guildofferrespond";
    public const string GuildSetOpen = "guildsetopen";
    public const string GuildSetShowRank = "guildsetshowrank";             // C→S: leader toggles overhead rank word
    public const string GuildLeave = "guildleave";
    public const string GuildKick = "guildkick";
    public const string GuildPromote = "guildpromote";
    public const string GuildDemote = "guilddemote";
    public const string GuildTransfer = "guildtransfer";
    public const string GuildSetMotd = "guildsetmotd";
    public const string GuildSetLabels = "guildsetlabels";
    public const string GuildSetColor = "guildsetcolor";
    public const string GuildDonate = "guilddonate";
    public const string GuildDonateValor = "guilddonatevalor";
    public const string GuildPayTax = "guildpaytax";
    public const string GuildQuestAcquire = "guildquestacquire";
    public const string GuildQuestAbandon = "guildquestabandon";
    public const string GuildChat = "guildchat";
    public const string GuildBrowseRequest = "guildbrowsereq";
    public const string GuildBrowse = "guildbrowse";                 // S→C: open-guild list
    public const string GuildApply = "guildapply";
    public const string GuildReviewApplication = "guildreviewapp";
    public const string GuildOfferNotify = "guildoffernotify";   // S→C: prompt the recipient
    public const string GuildInfo = "guildinfo";                 // S→C: guild identity + roster
    public const string GuildInfoRequest = "guildinforequest";
    public const string GuildWarDeclare = "guildwardeclare";
    public const string GuildWarDeclareByName = "guildwardeclarebyname";   // C→S: declare by guild name
    public const string GuildWarRetract = "guildwarretract";
    public const string GuildWarReviewRequest = "guildwarreview";
    public const string GuildWarPeace = "guildwarpeace";
    public const string GuildWarWager = "guildwarwager";                   // C→S: propose/accept/reject/withdraw an ante
    public const string GuildTerritoryChallenge = "guildterrchallenge";     // C→S: register a territory challenge
    public const string GuildTerritoryWithdraw = "guildterrwithdraw";       // C→S: withdraw a territory challenge
    public const string GuildWarAttrition = "guildwarattrition";           // S→C: live meter push
    public const string TerritoryContest = "territorycontest";             // S→C: live contest render state (participants)
    public const string AdminGuildReset = "adminguildreset";               // C→S: creator /guildreset day|week|season
    public const string AdminTerritoryWar = "adminterritorywar";           // C→S: creator /startwar //endwar //advancewar
    public const string GuildLeaderboard = "guildleaderboard";             // S→C: seasonal standings (all guilds)
    public const string GuildLeaderboardRequest = "guildleaderboardreq";   // C→S: request the seasonal standings
    public const string SeasonArchiveRequest = "seasonarchivereq";         // C→S: request an archived past season
    public const string SeasonArchive = "seasonarchive";                   // S→C: an archived season's standings

    // ── Social (friends / ignore) ─────────────────────────────────────────────
    public const string SocialList = "sociallist";               // S→C: full friends + ignore lists
    public const string SocialAddFriend = "socialaddfriend";
    public const string SocialAddIgnore = "socialaddignore";
    public const string SocialRemoveFriend = "socialremovefriend";
    public const string SocialRemoveIgnore = "socialremoveignore";

    // ── Mail ──────────────────────────────────────────────────────────────────
    public const string Mailbox = "mailbox";                     // S→C: full mailbox
    public const string MailMarkRead = "mailmarkread";
    public const string MailDelete = "maildelete";
    public const string MailClaim = "mailclaim";                 // C→S: collect a mail's attachments
    public const string MailSend = "mailsend";                   // C→S: compose + send P2P mail
    public const string MailPayCod = "mailpaycod";               // C→S: pay a CoD to unlock its attachments

    // ── Marketplace ─────────────────────────────────────────────────────────────
    public const string MarketList = "marketlist";               // S→C: current listings (+ open signal)
    public const string MarketOpen = "marketopen";               // C→S: open / browse (at an inn)
    public const string MarketCreate = "marketcreate";           // C→S: list an item stack
    public const string MarketBuy = "marketbuy";                 // C→S: buy a listing
    public const string MarketCancel = "marketcancel";           // C→S: cancel own listing
    public const string MarketRefresh = "marketrefresh";         // C→S: re-fetch listings (Refresh button)
    public const string MarketClose = "marketclose";             // C→S: market panel closed (stop broadcasts)

    // ── Direct trade ────────────────────────────────────────────────────────────
    public const string TradeInvite = "tradeinvite";               // C→S: invite a player to trade by name
    public const string TradeRespond = "traderespond";             // C→S: accept / decline an invite
    public const string TradeOfferAdd = "tradeofferadd";           // C→S: stage an item
    public const string TradeOfferRemove = "tradeofferremove";     // C→S: unstage an item
    public const string TradeConfirm = "tradeconfirm";             // C→S: set confirm flag
    public const string TradeCancel = "tradecancel";               // C→S: cancel the trade
    public const string TradeInviteNotify = "tradeinvitenotify";   // S→C: incoming invite prompt
    public const string TradeWindow = "tradewindow";               // S→C: live trade window state

    public const string QuestLog = "questlog";                     // S→C: player's quest state (log + overhead)
    public const string QuestAccept = "questaccept";               // C→S: accept a quest
    public const string QuestTurnIn = "questturnin";               // C→S: turn in a completed quest
    public const string QuestAbandon = "questabandon";             // C→S: abandon an in-progress quest
    public const string SendQuests = "sendquests";                 // S→C: quest DEFINITIONS (at join, like items/npcs)
    public const string OpenNpcQuestMenu = "npcquestmenu";         // S→C: open the client quest/context menu for an NPC

    public const string SendConversations = "sendconvs";           // S→C: conversation DEFINITIONS (at join, like quests)
    public const string ConversationLog = "convlog";               // S→C: this character's spoken-conversation set
    public const string OpenNpcConversation = "npcconv";           // S→C: open the client conversation panel for an NPC

    // ── C→S: bank ───────────────────────────────────────────────────────────
    public const string BankOpen = "bankopen";
    public const string BankDeposit = "bankdeposit";
    public const string BankWithdraw = "bankwithdraw";
    public const string BankDepositBulk = "bankdepositbulk";
    public const string BankWithdrawBulk = "bankwithdrawbulk";
    public const string BankSort = "banksort";

    // ── C→S: inn ────────────────────────────────────────────────────────────
    public const string ConfirmSetSpawn = "confirmsetspawn";

    // ── C→S: death & respawn ──────────────────────────────────────────────────
    public const string RespawnRequest = "respawnrequest";

    // ── C→S: admin ──────────────────────────────────────────────────────────
    public const string WarpMeTo = "warpmeto";
    public const string WarpToMe = "warptome";
    public const string GodMode = "godmode";
    public const string WarpTo = "warpto";
    public const string SetSprite = "setsprite";
    public const string MapRespawn = "maprespawn";
    public const string MapReport = "mapreport";
    public const string KickPlayer = "kickplayer";
    public const string BanPlayer = "banplayer";
    public const string MutePlayer = "muteplayer";
    public const string RefreshBanList = "refreshbanlist";
    public const string HwBanPlayer = "hwbanplayer";
    public const string UnbanPlayer = "unbanplayer";
    public const string UnkickPlayer = "unkickplayer";
    public const string UnmutePlayer = "unmuteplayer";
    public const string HwUnbanPlayer = "hwunbanplayer";
    public const string RequestModeration = "requestmoderation";
    public const string ModerationList = "moderationlist";
    public const string SetAccess = "setaccess";
    public const string SetMotd = "setmotd";

    // ── C→S: editor (game client path removed; sent by Mirage.Editor) ───────
    public const string RequestEditMap = "requesteditmap";
    public const string RequestEditItem = "requestedititem";
    public const string EditItem = "edititem";
    public const string SaveItem = "saveitem";
    public const string RequestEditNpc = "requesteditnpc";
    public const string EditNpc = "editnpc";
    public const string SaveNpc = "savenpc";
    public const string RequestEditShop = "requesteditshop";
    public const string EditShop = "editshop";
    public const string SaveShop = "saveshop";
    public const string RequestEditSpell = "requesteditspell";
    public const string EditSpell = "editspell";
    public const string SaveSpell = "savespell";

    // ── C→S: editor session auth (Mirage.Editor only) ───────────────────────
    public const string EditorLogin = "editorlogin";
    public const string EditorSaveItem = "editorsaveitem";
    public const string EditorSaveNpc = "editorsavenpc";
    public const string EditorSaveShop = "editorsaveshop";
    public const string EditorSaveQuest = "editorsavequest";
    public const string EditorSaveConversation = "editorsaveconv";
    public const string EditorSaveSpell = "editorsavespell";
    public const string EditorSaveMap = "editorsavemap";
    public const string EditorRequestItem = "editorreqitem";
    public const string EditorRequestNpc = "editorreqnpc";
    public const string EditorRequestShop = "editorreqshop";
    public const string EditorRequestQuest = "editorreqquest";
    public const string EditorRequestConversation = "editorreqconv";
    public const string EditorRequestSpell = "editorreqspell";
    public const string EditorRequestMap = "editorreqmap";
    public const string EditorRequestClass = "editorreqclass";
    public const string EditorSaveClass = "editorsaveclass";
    public const string EditorLock = "editorlock";
    public const string EditorUnlock = "editorunlock";
    public const string EditorRequestAllItems = "editorreqallitems";
    public const string EditorRequestAllNpcs = "editorreqallnpcs";
    public const string EditorRequestAllShops = "editorreqallshops";
    public const string EditorRequestAllQuests = "editorreqallquests";
    public const string EditorRequestAllConversations = "editorreqallconvs";
    public const string EditorRequestAllSpells = "editorreqallspells";
    public const string EditorRequestAllClasses = "editorreqallclasses";
    public const string EditorSaveMapGroup = "editorsavemapgroup";
    public const string EditorRequestMapGroup = "editorreqmapgroup";
    public const string EditorRequestAllMapGroups = "editorreqallmapgroups";
    public const string EditorRequestAllMaps = "editorreqallmaps";
    // Accounts — CREATOR only, and the only editor family that touches a person rather than content.
    public const string EditorRequestAccounts = "editorreqaccounts";
    public const string EditorAccountList = "editoraccountlist";
    public const string EditorRequestAccount = "editorreqaccount";
    public const string EditorAccount = "editoraccount";
    public const string EditorSaveAccount = "editorsaveaccount";
    public const string EditorRenameChar = "editorrenamechar";
    public const string EditorGiveItem = "editorgiveitem";
    public const string EditorTakeItem = "editortakeitem";
    public const string EditorLearnSpell = "editorlearnspell";
    public const string EditorForgetSpell = "editorforgetspell";
    public const string EditorBankGive = "editorbankgive";
    public const string EditorBankTake = "editorbanktake";
    public const string EditorSetQuestStatus = "editorsetqueststatus";
    public const string EditorNotice = "editornotice";
    public const string EditorLocks = "editorlocks";

    // ── S→C: bank ───────────────────────────────────────────────────────────
    public const string SendBank = "sendbank";
    public const string BankSlotUpdate = "bankslotupdate";

    // ── S→C ─────────────────────────────────────────────────────────────────
    public const string AlertMsg = "alertmsg";
    public const string ServerHello = "serverhello";
    public const string QueueUpdate = "queueupdate";
    public const string SendChars = "sendchars";
    public const string JoinMap = "joinmap";
    public const string LeaveMap = "leavemap";
    public const string SendPlayerData = "playerdata";
    public const string AggressorRefresh = "aggressorrefresh";
    public const string SendMap = "sendmap";
    public const string MapItems = "mapitems";
    public const string MapNpcs = "mapnpcs";
    public const string SendItems = "senditems";
    public const string SendNpcs = "sendnpcs";
    public const string SendMapGroups = "sendmapgroups";
    public const string SendInventory = "sendinventory";
    public const string InventoryUpdate = "inventoryupdate";
    public const string EquippedGear = "equippedgear";
    public const string SendHp = "sendhp";
    public const string SendMp = "sendmp";
    public const string SendSp = "sendsp";
    public const string SendStats = "sendstats";
    public const string Welcome = "welcome";
    public const string SendClasses = "sendclasses";
    public const string NewCharClasses = "newcharclasses";
    public const string LeftGame = "leftgame";
    public const string PlayerXY = "playerxy";
    public const string UpdateItem = "updateitem";
    public const string EditItemData = "edititemdata";
    public const string UpdateNpc = "updatenpc";
    public const string EditNpcData = "editnpcdata";
    public const string SendShops = "sendshops";
    public const string UpdateShop = "updateshop";
    public const string UpdateQuest = "updatequest";
    public const string UpdateConversation = "updateconv";
    public const string EditShopData = "editshopdata";
    public const string SendSpells = "sendspells";
    public const string UpdateSpell = "updatespell";
    public const string EditSpellData = "editspelldata";
    public const string ShopContents = "shopcontents";             // S→C: an open shop's barter rows AND its sales list
    public const string OpenInn = "openinn";                       // S→C: raise the client-local Inn panel (from an NPC interact)
    public const string PlayerSpells = "playerspells";
    public const string PlayerHotkeys = "playerhotkeys";
    public const string Weather = "weather";
    public const string TimeOfDay = "timeofday";
    public const string SetTimeOfDay = "settimeofday";
    public const string SetWeather = "setweather";
    public const string NpcSpawn = "npcspawn";
    public const string NpcMove = "npcmove";
    public const string TraversalNpc = "traversalnpc";
    public const string NpcDespawn = "npcdespawn";
    public const string NpcDir = "npcdir";
    public const string NpcAttack = "npcattack";
    public const string NpcCast = "npccast";
    public const string PlayerAttack = "playerattack";
    public const string PlayerCast = "playercast";
    public const string PlayerDeath = "playerdeath";
    public const string NpcDamage = "npcdamage";
    public const string CombatText = "combattext";
    public const string ChatMsg = "chatmsg";
    public const string ChatBubble = "chatbubble";
    public const string NpcChatBubble = "npcchatbubble";
    public const string PlayerInGame = "ingame";
    public const string PartyRequest = "partyrequest";
    public const string PartyVitals = "partyvitals";
    public const string PlayersOnline = "playersonline";

    // ── S→C: editor session ──────────────────────────────────────────────────
    public const string EditorLoginResponse = "editorloginresp";
    public const string EditorData = "editordata";
    public const string UpdateClass = "updateclass";
    public const string EditorAllItems = "editorallitems";
    public const string EditorAllNpcs = "editorallnpcs";
    public const string EditorAllShops = "editorallshops";
    public const string EditorAllQuests = "editorallquests";
    public const string EditorAllConversations = "editorallconvs";
    public const string EditorAllSpells = "editorallspells";
    public const string EditorAllClasses = "editorallclasses";
    public const string UpdateMapGroup = "updatemapgroup";
    public const string EditorAllMapGroups = "editorallmapgroups";
    public const string EditorAllMaps = "editorallmaps";

    // ── S→C: map / world events ───────────────────────────────────────────────
    public const string CheckForMap = "checkformap";
    public const string SeamlessCross = "seamlesscross";
    public const string MapKey = "mapkey";
    public const string BloodUpdate = "bloodupdate";
    public const string NpcDead = "npcdead";
    public const string NpcTarget = "npctarget";
    public const string SetTarget = "settarget";
    public const string ClearTarget = "cleartarget";
    public const string SendPlayerDir = "playerdir";
}

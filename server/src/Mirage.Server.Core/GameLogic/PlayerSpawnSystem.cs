using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Core.GameLogic;

/// <summary>Handles setting a player's respawn point at an inn.</summary>
public sealed class PlayerSpawnSystem : GameSystem
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly ItemSystem _items;
    private readonly PlayerSaver _saver;

    public PlayerSpawnSystem(GameWorld world, PlayerManager pm, IPacketDispatcher dispatcher, ItemSystem items, PlayerSaver saver)
        : base(dispatcher)
    {
        _world = world;
        _pm = pm;
        _items = items;
        _saver = saver;
    }

    /// <summary>Charge for and set the player's respawn point to where they stand.
    /// <para>Re-validates everything the client already checked — that an Inn is genuinely open for this
    /// player and that they can afford it — because the client's copy is only a preview. Cost scales with
    /// level (<c>EconomyFormulas.InnSpawnCost</c>), so a high-level respawn anchor is a real sink.</para>
    /// <para>Persisted immediately rather than left to the autosave: a spawn point the player paid for
    /// must not be lost to a hard disconnect.</para></summary>
    public void ConfirmSetSpawn(int index)
    {
        var sp = _pm[index];
        if (!sp.IsPlaying) return;
        var vp = sp.Char;

        int shopNum = sp.ActiveShop(_world, index);
        if (shopNum <= 0 || _world.Shops[shopNum].ShopType != ShopType.Inn)
        {
            SendMsg(index, ServerStrings.PlayerSpawnSystem_NotNearInn, GameColor.BrightRed);
            return;
        }

        long cost = EconomyFormulas.InnSpawnCost(vp.Level);
        long gold = ItemSystem.CountItem(vp, _world.Items, Constants.GoldItemIndex);
        if (gold < cost)
        {
            SendMsg(index, ServerStrings.PlayerSpawnSystem_InsufficientGold, GameColor.BrightRed, ("Cost", $"{cost:N0}"));
            return;
        }

        _items.TakeItem(index, Constants.GoldItemIndex, (int)cost);
        vp.SpawnMap = vp.Map;
        vp.SpawnX = vp.X;
        vp.SpawnY = vp.Y;
        _saver.SaveCharInBackground(sp.Login, sp.CharNum, vp.Clone(), sp.CloneBank());
        SendMsg(index, ServerStrings.PlayerSpawnSystem_SpawnSet, GameColor.BrightCyan);
    }
}

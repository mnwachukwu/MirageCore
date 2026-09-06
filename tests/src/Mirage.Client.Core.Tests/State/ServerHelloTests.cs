using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.State;

/// <summary>
/// A client compiles against the PROTOCOL ceiling — the largest slot the wire can carry — but a server
/// runs on its own, usually much smaller, limit. The pre-login hello is how the client learns it, and
/// <see cref="ClientState.PlayerSlots"/> is what every per-frame pass over players bounds itself by.
///
/// <para>Being wrong high is harmless: a few checks on slots that stay empty. Being wrong LOW would skip a
/// real player mid-step, so these lock the direction as much as the value.</para>
/// </summary>
[TestFixture]
public class ServerHelloTests
{
    private static readonly MethodInfo HandleHello = typeof(ClientPacketHandler)
        .GetMethod("HandleServerHello", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The handler touches neither sender nor mapCache, so both are null! (per the client-test convention).
    private static void Hello(ClientState state, int maxPlayers, string gameName = "", RecordLimits? records = null) =>
        HandleHello.Invoke(new ClientPacketHandler(state, null!, null!),
            [new ServerHelloPacket
            {
                MaxPlayers = maxPlayers,
                GameName = gameName,
                Records = records ?? RecordLimits.Default,
            }]);

    [Test]
    public void StartsAtTheProtocolCeilingBeforeAnyServerHasSpoken()
    {
        // Nothing has been heard yet, so assume the most the wire allows. The alternative — assuming a
        // small number — would silently skip players on a big server.
        Assert.That(new ClientState().PlayerSlots, Is.EqualTo(Constants.MaxPlayers));
    }

    [Test]
    public void TakesTheServersLimit()
    {
        var state = new ClientState();
        Hello(state, 20);

        Assert.That(state.PlayerSlots, Is.EqualTo(20));
    }

    [Test]
    public void RefusesToGoAboveTheCeiling()
    {
        // The player table is ceiling-sized. A server claiming more — misconfigured, or hostile — must not
        // be able to walk the client off the end of it.
        var state = new ClientState();
        Hello(state, Constants.MaxPlayers * 4);

        Assert.That(state.PlayerSlots, Is.EqualTo(Constants.MaxPlayers));
    }

    [Test]
    public void RefusesToGoBelowOne()
    {
        var state = new ClientState();
        Hello(state, 0);

        Assert.That(state.PlayerSlots, Is.EqualTo(1));
    }

    [Test]
    public void ASecondServerReplacesTheFirstsAnswer()
    {
        var state = new ClientState();
        Hello(state, 20);
        Hello(state, 400);

        Assert.That(state.PlayerSlots, Is.EqualTo(400), "the hello arrives on every connection");
    }

    // ── The world's name ──────────────────────────────────────────────────────
    // A client ships with no game identity. It wears the ENGINE's name until a server names the world,
    // and that handshake is documented as a known limitation rather than left to surprise anyone.

    [Test]
    public void WearsTheEngineNameUntilAServerNamesTheWorld()
    {
        Assert.That(new ClientState().GameName, Is.EqualTo(Constants.GameName));
    }

    [Test]
    public void TakesTheWorldsName()
    {
        var state = new ClientState();
        Hello(state, 20, "Brightwater");

        Assert.That(state.GameName, Is.EqualTo("Brightwater"));
    }

    [Test]
    public void AServerThatNamesNothingLeavesTheEngineName()
    {
        // An operator who never renamed anything, or an older server that does not send the field.
        var state = new ClientState();
        Hello(state, 20, "   ");

        Assert.That(state.GameName, Is.EqualTo(Constants.GameName));
    }

    [Test]
    public void ClearingItReturnsToTheEngineName()
    {
        // This is how the main menu drops a name on the way out of a world.
        var state = new ClientState();
        Hello(state, 20, "Brightwater");
        state.GameName = "";

        Assert.That(state.GameName, Is.EqualTo(Constants.GameName));
    }

    // ── Record ceilings ───────────────────────────────────────────────────────
    // The bug this exists to kill: Constants.Max* was const, so a client built against 1000 items
    // rejected item 1200 on a server that had authored it. The client is told, and sizes to match.

    [Test]
    public void SizesEveryRecordTableToWhatTheServerSaid()
    {
        var state = new ClientState();
        Hello(state, 20, records: new RecordLimits
        {
            Items = 40, Npcs = 30, Shops = 12, Spells = 25,
            Quests = 8, Conversations = 9, Maps = 20, MapGroups = 6,
        });

        Assert.Multiple(() =>
        {
            Assert.That(state.Limits.Items, Is.EqualTo(40));
            Assert.That(state.Items, Has.Length.EqualTo(41), "1-based, so one longer than the limit");
            Assert.That(state.NpcDefs, Has.Length.EqualTo(31));
            Assert.That(state.ShopDefs, Has.Length.EqualTo(13));
            Assert.That(state.SpellDefs, Has.Length.EqualTo(26));
            Assert.That(state.QuestDefs, Has.Length.EqualTo(9));
            Assert.That(state.ConvDefs, Has.Length.EqualTo(10));
            Assert.That(state.MapGroups, Has.Length.EqualTo(7));
        });
    }

    [Test]
    public void GrowsPastWhatTheClientWasBuiltWith()
    {
        // The whole point. A server that authored 5000 items must not have them rejected by a client
        // that happened to be compiled when the stock number was 1000.
        var state = new ClientState();
        Hello(state, 20, records: RecordLimits.Default with { Items = 5000 });

        Assert.That(state.Limits.Items, Is.EqualTo(5000));
        Assert.That(state.Items, Has.Length.EqualTo(5001));
    }

    [Test]
    public void RefusesAnAbsurdCeiling()
    {
        // Both ends allocate an array of this length, so a typo — or a hostile server — must not be able
        // to ask for an arbitrary amount of memory.
        var state = new ClientState();
        Hello(state, 20, records: RecordLimits.Default with { Items = int.MaxValue });

        Assert.That(state.Limits.Items, Is.EqualTo(RecordLimits.Ceiling));
    }

    [Test]
    public void RefusesAnEmptyFamily()
    {
        var state = new ClientState();
        Hello(state, 20, records: RecordLimits.Default with { Npcs = 0 });

        Assert.That(state.Limits.Npcs, Is.EqualTo(1), "a world with no NPC slots at all is not a world");
    }

    [Test]
    public void WorldPassesStopAtTheLimit()
    {
        // ClearMapState is the cheapest pass to observe: it clears every player slot it walks. A record
        // left populated above the limit proves the loop stopped where the server said it would.
        var state = new ClientState();
        Hello(state, 4);
        state.MyIndex = 1;
        for (int i = 1; i <= 10; i++) state.Players[i] = new PlayerRecord { Name = $"P{i}" };

        state.ClearMapState();

        Assert.That(state.Players[2].Name, Is.Empty, "inside the limit, cleared");
        Assert.That(state.Players[4].Name, Is.Empty, "the limit itself, cleared");
        Assert.That(state.Players[5].Name, Is.EqualTo("P5"), "past the limit, never walked");
    }
}

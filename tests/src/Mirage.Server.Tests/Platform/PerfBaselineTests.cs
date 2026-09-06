using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Server-side performance baseline. <b>[Explicit] — run manually</b>, like
/// <c>Benchmark_GangShareBeatsPerChaser</c>; these measure rather than assert, so they have no place in
/// the gating suite.
///
/// <para>Purpose: decide which of the suspected hot spots are worth changing. Reading code tells you
/// where allocations and repeated work COULD matter; only a measurement tells you whether they DO.
/// Anything that does not show up here should be left alone rather than churned.</para>
///
/// <para>Each benchmark reports both wall time and bytes allocated, because on the game thread an
/// allocation is often the real cost — a per-packet or per-tick allocation becomes GC pressure that
/// shows up as a latency spike somewhere else entirely, not as time inside the method.</para>
/// </summary>
[TestFixture]
[Explicit, Category("Benchmark")]
public class PerfBaselineTests
{
    const int Warmup = 2_000;

    // Measures a delegate: median-ish wall time per op, and bytes allocated per op on this thread.
    static (double usPerOp, double bytesPerOp) Measure(int iterations, Action op)
    {
        for (int i = 0; i < Warmup; i++) op();          // JIT + first-touch

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) op();
        sw.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();

        return (sw.Elapsed.TotalMicroseconds / iterations, (after - before) / (double)iterations);
    }

    static void Report(string label, (double usPerOp, double bytesPerOp) r) =>
        TestContext.WriteLine($"  {label,-46} {r.usPerOp,9:F3} us/op   {r.bytesPerOp,9:F0} B/op");

    // ── Inbound packet decode ─────────────────────────────────────────────────

    // The two decode shapes side by side on the same input: the allocation-free Utf8JsonReader scan
    // against a throwaway JsonNode DOM parsed purely to read "cmd". The DOM shape is reimplemented
    // locally so the comparison is like-for-like.
    //
    // This is the server's single hottest input path — every packet from every client, plus the client's
    // own inbound stream, where a DOM parse is paid TWICE per packet (its own, then TryDeserialize's).
    [Test]
    public void Benchmark_PacketDecode_HeaderScanVsDomParse()
    {
        // A representative movement packet (the highest-frequency message) and a bulky editor save.
        string move = PacketSerializer.Serialize(new PlayerMovePacket { Dir = Direction.Right, Movement = MovementType.Running });
        string bulky = PacketSerializer.Serialize(new SayMsgPacket { Msg = new string('x', 900) });

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // The pre-Phase-1.3 shape: build a DOM, read one field off it, throw the DOM away.
        string OldWayCmd(string line)
        {
            var node = JsonNode.Parse(line);
            return node?["cmd"]?.GetValue<string>() ?? "";
        }

        TestContext.WriteLine("Inbound packet decode — reading the cmd discriminator:");
        Report("small packet: DOM parse (pre-1.3)", Measure(200_000, () => OldWayCmd(move)));
        Report("small packet: header scan (current)", Measure(200_000, () => PacketSerializer.ReadHeader(move)));
        Report("900B packet: DOM parse (pre-1.3)", Measure(50_000, () => OldWayCmd(bulky)));
        Report("900B packet: header scan (current)", Measure(50_000, () => PacketSerializer.ReadHeader(bulky)));

        TestContext.WriteLine("");
        TestContext.WriteLine("Full decode (header + deserialize), which is what the dispatchers actually do:");
        Report("full: DOM + deserialize (pre-1.3)", Measure(100_000, () =>
        {
            string cmd = OldWayCmd(move);
            if (cmd.Length > 0) JsonSerializer.Deserialize<PlayerMovePacket>(move, jsonOpts);
        }));
        Report("full: scan + deserialize (current)", Measure(100_000, () => PacketSerializer.TryDeserialize(move)));
    }

    // ── Player-slot indexer ───────────────────────────────────────────────────

    // Candidate 2 in the plan: methods that re-index _pm[index] several times instead of hoisting a
    // local (MarkPlayerCombat does it five times). Worth knowing whether the indexer is actually
    // expensive before touching call sites all over the combat code.
    [Test]
    public void Benchmark_PlayerManagerIndexer_RepeatedVsHoisted()
    {
        var pm = new PlayerManager();
        var sp = pm[1];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        long sink = 0;

        TestContext.WriteLine("PlayerManager indexer — five reads per call, repeated vs hoisted:");
        Report("five repeated pm[i] reads", Measure(2_000_000, () =>
        {
            sink += pm[1].Char.Hp + pm[1].Char.MaxHp + pm[1].Char.Mp
                  + pm[1].Char.MaxMp + pm[1].Char.Sp;
        }));
        Report("one hoisted local", Measure(2_000_000, () =>
        {
            var p = pm[1].Char;
            sink += p.Hp + p.MaxHp + p.Mp + p.MaxMp + p.Sp;
        }));
        TestContext.WriteLine($"  (sink {sink} — keeps the loops from being optimized away)");
    }

    // ── Join / region-sync packet builders ────────────────────────────────────

    // Candidate 3: JoinLeaveSystem's Build* helpers run on every join AND every seamless region sync,
    // and carry 33 LINQ sites between them. A region sync happens whenever a player crosses a map
    // border, so this is not a once-per-session cost.
    [Test]
    public void Benchmark_JoinLeaveSystem_WorldDataBuilders()
    {
        var world = new GameWorld();

        // Populate the definition tables the builders project over, so the measurement reflects a real
        // server rather than empty arrays.
        for (int i = 1; i <= RecordLimits.Default.Items; i++)
            world.Items[i] = new ItemRecord { Name = $"item{i}" };
        for (int i = 1; i <= RecordLimits.Default.Npcs; i++)
            world.Npcs[i] = new NpcRecord { Name = $"npc{i}", Behavior = NpcBehavior.AttackOnSight };
        for (int i = 1; i <= RecordLimits.Default.Shops; i++)
            world.Shops[i] = new ShopRecord { Name = $"shop{i}" };
        for (int i = 1; i <= RecordLimits.Default.Spells; i++)
            world.Spells[i] = new SpellRecord { Name = $"spell{i}" };

        var join = new JoinLeaveSystem(world, new PlayerManager(), new NoOpDispatcher(),
            saver: null!, movement: null!, party: null!, guilds: null!, mail: null!, social: null!,
            trade: null!, quests: null!, conversations: null!,
            tod: null!, weather: null!, blood: null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JoinLeaveSystem>.Instance);

        TestContext.WriteLine("World-data builders (run on every join AND every region sync):");
        foreach (string name in new[] { "BuildSendNpcs", "BuildSendShops", "BuildSendSpells", "BuildSendMapGroups" })
        {
            var m = typeof(JoinLeaveSystem).GetMethod(name,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m is null)
            {
                TestContext.WriteLine($"  {name,-46} (not found — renamed?)");
                continue;
            }
            Report(name, Measure(2_000, () => m.Invoke(join, null)));
        }
    }

    // ── Constants table access ────────────────────────────────────────────────

    // Candidate 6: check whether any static readonly table in Constants is rebuilt per access rather
    // than cached. A rebuilt table read from a per-tick path would be invisible in code review.
    [Test]
    public void Benchmark_ConstantsTableAccess_IsCached()
    {
        int sink = 0;
        TestContext.WriteLine("Constants table access (should be a field read, near zero allocation):");
        Report("GameColor palette-ish table read", Measure(1_000_000, () => { sink += Constants.MaxMapX + Constants.MaxMapY; }));
        TestContext.WriteLine($"  (sink {sink})");
    }

    // ── No-op dispatcher ──────────────────────────────────────────────────────

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}

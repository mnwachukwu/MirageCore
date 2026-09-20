using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

// GUEST ↔ NATIVE NPC PARITY.
//
// A guest (traversal) NPC and a native (slot) NPC must be imperceptible to the player and treated
// identically by the engine; a cross-seam gap between the two is exactly the kind of divergence that
// breaks that. The strongest guarantee is STRUCTURAL: TraversalNpcRecord : MapNpcRecord, so a guest IS a native record
// (every combat/AI field inherited, never shadowed) and every method that takes a MapNpcRecord operates
// on both identically — data-level divergence is impossible. This fixture locks that inheritance, the
// shared chase decision (NpcWantsChaseRun), and the ONE intentional specialization
// (GetSpawnIdentity → permanent home identity).
//
// Scope note: running a full AI + movement TICK and diffing native vs guest outcomes would need the
// whole movement and dispatcher graph wired up (a much heavier harness). The structural
// guarantees below make the DATA side divergence-proof; the dispatch side (RunMovement iterates natives
// AND guests; the guest steppers cross seams) is exercised by the chase tests and by playing it.
[TestFixture]
public class GuestNativeNpcParityTests
{
    // ── A. STRUCTURAL INHERITANCE — a guest IS a native record ────────────────────

    [Test]
    public void Guest_IsSubclassOf_Native() =>
        Assert.That(typeof(TraversalNpcRecord).IsSubclassOf(typeof(MapNpcRecord)), Is.True,
            "a traversal guest must inherit the full native record so shared code can't perceive a difference");

    // The guest adds ONLY home-identity metadata. If a combat/AI STATE field were declared here instead of on
    // the base, a native couldn't represent it — a parity hole. Lock the set of guest-declared properties.
    [Test]
    public void Guest_DeclaresOnly_HomeIdentityFields()
    {
        var declared = typeof(TraversalNpcRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .ToHashSet();
        var allowed = new HashSet<string> { "SpawnMapNum", "SpawnSlot", "CurrentMapNum", "LastAiTick" };
        Assert.That(declared, Is.SubsetOf(allowed),
            "a guest may add ONLY home-identity fields; every combat/AI state field must live on MapNpcRecord (inherited)");
    }

    // Every AI state field the player can perceive is declared on the BASE — inherited by the guest, never
    // shadowed — so a guest and a native carry it identically: action timers and chase latches.
    [TestCase("Target")]
    [TestCase("Dir")]
    [TestCase("AttackTimer")]
    [TestCase("HasMadeContact")]
    [TestCase("ChaseSprinting")]
    [TestCase("RushCommitted")]
    [TestCase("LastReachedTargetMs")]
    public void CombatState_DeclaredOnBase_NotShadowedByGuest(string member)
    {
        var prop = typeof(TraversalNpcRecord).GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(prop, Is.Not.Null, $"{member} must exist on the guest (inherited)");
        Assert.That(prop!.DeclaringType, Is.EqualTo(typeof(MapNpcRecord)),
            $"{member} must be declared on MapNpcRecord so a guest inherits (not shadows) it — else it could diverge");
    }

    // ── B. SHARED CHASE DECISION — identical for a guest and a native at identical state ──
    // NpcWantsChaseRun takes the BASE MapNpcRecord, so a guest (subclass) runs the identical code. This sweep
    // locks that contract across every axis (behavior, contact, latch, gap): any future guest-specific
    // branch that changed the run/walk decision would break it.
    [Test]
    public void ChaseRunDecision_IdenticalForGuestAndNative()
    {
        var decide = typeof(NpcAiSystem).GetMethod("NpcWantsChaseRun", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(decide, Is.Not.Null, "NpcWantsChaseRun must exist (shared chase-run decision)");

        foreach (var beh in new[] { NpcBehavior.Pursue, NpcBehavior.Flee, NpcBehavior.Wander })
        {
            foreach (bool contact in new[] { false, true })
            {
                foreach (bool sprinting in new[] { false, true })
                {
                    foreach (int gap in new[] { 1, 3, 6, 9 })
                    {
                        var npc = new NpcRecord { Behavior = beh};
                        var native = new MapNpcRecord { HasMadeContact = contact, ChaseSprinting = sprinting};
                        var guest = new TraversalNpcRecord { HasMadeContact = contact, ChaseSprinting = sprinting};

                        bool nativeRun = (bool)decide!.Invoke(null, new object[] { native, npc, gap })!;
                        bool guestRun = (bool)decide.Invoke(null, new object[] { guest, npc, gap })!;

                        string ctx = $"beh={beh} contact={contact} sprint={sprinting} gap={gap}";
                        Assert.That(guestRun, Is.EqualTo(nativeRun), $"chase run/walk decision diverged: {ctx}");
                        // The ChaseSprinting latch side-effect must land identically too.
                        Assert.That(guest.ChaseSprinting, Is.EqualTo(native.ChaseSprinting), $"ChaseSprinting latch diverged: {ctx}");
                    }
                }
            }
        }
    }

    // ── C. THE ONE INTENTIONAL SPECIALIZATION — permanent home identity ──
    // A guest reports its permanent HOME (SpawnMap, SpawnSlot) identity rather than its transient (map, slot),
    // so NPC-vs-NPC contributor/target references stay stable as it hops seams. This is the sole by-design
    // guest/native behavioral difference — and it is invisible to the player.
    [Test]
    public void GetSpawnIdentity_GuestUsesHome_NativeUsesCurrentSlot()
    {
        var native = new MapNpcRecord();
        Assert.That(native.GetSpawnIdentity(mapNum: 3, slot: 7), Is.EqualTo((3, 7)),
            "a native's identity is its current (map, slot)");

        var guest = new TraversalNpcRecord { SpawnMapNum = 1, SpawnSlot = 4, CurrentMapNum = 3 };
        Assert.That(guest.GetSpawnIdentity(mapNum: 3, slot: 7), Is.EqualTo((1, 4)),
            "a guest's identity is its permanent home, regardless of where it currently stands");
    }
}

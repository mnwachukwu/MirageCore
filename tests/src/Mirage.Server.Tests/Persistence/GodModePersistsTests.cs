using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Serialization;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.Persistence;

/// <summary>
/// Observer mode belongs to the character and survives a logout.
///
/// <para>An admin who set it, went to a high-level zone and logged out came back to a character standing in
/// that zone with nothing protecting it. The flag lives on <see cref="PlayerRecord"/> — the record every
/// save path clones — so it rides along with the save rather than needing each of those paths to remember
/// it, and access is re-checked on join so a demotion taken offline still lands.</para>
/// </summary>
[TestFixture]
public class GodModePersistsTests
{
    static PlayerRecord RoundTrip(PlayerRecord p) =>
        JsonSerializer.Deserialize<PlayerRecord>(JsonSerializer.Serialize(p, RecordJson.Options), RecordJson.Options)!;

    [Test]
    public void GodMode_SurvivesTheSaveFormat()
    {
        var saved = RoundTrip(new PlayerRecord { Name = "admin", GodMode = true });
        Assert.That(saved.GodMode, Is.True, "observer mode was dropped by the save format — is it [JsonIgnore] again?");
    }

    [Test]
    public void GodModeOff_SurvivesTheSaveFormat()
    {
        var saved = RoundTrip(new PlayerRecord { Name = "admin", GodMode = false });
        Assert.That(saved.GodMode, Is.False, "a character not in observer mode must not come back in it");
    }

    /// <summary>A save written before observer mode persisted has no key for it, and must read as off rather
    /// than throwing or defaulting to on.</summary>
    [Test]
    public void ASaveWithNoGodModeKey_ReadsAsOff()
    {
        var loaded = JsonSerializer.Deserialize<PlayerRecord>("""{"name":"old","level":10}""", RecordJson.Options)!;
        Assert.That(loaded.GodMode, Is.False);
    }

    /// <summary>Access is per-account and never stored on the character, so a persisted observer flag can
    /// outlive the access that allowed it. The join path drops it; this pins the rule that gate applies.</summary>
    [TestCase(AdminLevel.Player, false, TestName = "A demoted character loses observer mode on join")]
    [TestCase(AdminLevel.Monitor, false, TestName = "A Monitor is below the observer-mode bar")]
    [TestCase(AdminLevel.Developer, true, TestName = "A Developer keeps observer mode across a login")]
    [TestCase(AdminLevel.Creator, true, TestName = "A Creator keeps observer mode across a login")]
    public void PersistedGodMode_IsRecheckedAgainstAccess(AdminLevel access, bool keeps)
    {
        var p = new PlayerRecord { Name = "admin", GodMode = true, Access = access };

        // The real rule both the toggle and the join path ask. Re-stating the threshold here instead would
        // assert a copy of the condition and pass whatever production did.
        if (p.GodMode && !p.MayUseGodMode) p.GodMode = false;

        Assert.That(p.GodMode, Is.EqualTo(keeps));
    }

    /// <summary>Access itself is NOT persisted per-character, so the recheck above cannot be skipped by
    /// trusting a saved access level.</summary>
    [Test]
    public void Access_IsNotPersistedOnTheCharacter()
    {
        var saved = RoundTrip(new PlayerRecord { Name = "admin", Access = AdminLevel.Developer });
        Assert.That(saved.Access, Is.EqualTo(AdminLevel.Player),
            "access is per-account; a character carrying its own would let a demoted admin keep observer mode");
    }
}

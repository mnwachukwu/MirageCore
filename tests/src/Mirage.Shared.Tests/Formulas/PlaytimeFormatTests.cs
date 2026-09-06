using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The pure playtime duration formatter for /played + /info.</summary>
[TestFixture]
public class PlaytimeFormatTests
{
    [Test]
    public void HoursMinutes_UnderAnHour_DropsHours()
    {
        Assert.That(PlaytimeFormat.HoursMinutes(0), Is.EqualTo("0m"));
        Assert.That(PlaytimeFormat.HoursMinutes(59), Is.EqualTo("0m"));   // sub-minute rounds down
        Assert.That(PlaytimeFormat.HoursMinutes(60), Is.EqualTo("1m"));
        Assert.That(PlaytimeFormat.HoursMinutes(3599), Is.EqualTo("59m"));
    }

    [Test]
    public void HoursMinutes_AtOrAboveAnHour_ShowsBoth()
    {
        Assert.That(PlaytimeFormat.HoursMinutes(3600), Is.EqualTo("1h 0m"));
        Assert.That(PlaytimeFormat.HoursMinutes(3661), Is.EqualTo("1h 1m"));
        Assert.That(PlaytimeFormat.HoursMinutes(90000), Is.EqualTo("25h 0m"));   // uncapped past a day
    }

    [Test]
    public void HoursMinutes_Negative_IsZero()
    {
        Assert.That(PlaytimeFormat.HoursMinutes(-5), Is.EqualTo("0m"));
    }
}

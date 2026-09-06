using Mirage.Server.Shell.Localization;
using NUnit.Framework;
using System.Globalization;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// The Show tick keeps meaning what it says.
///
/// <para>🔴 Masking used to ride <c>TextBox.RevealPassword</c>, which the control clears itself when it
/// loses focus. The binding is one-way, so the view-model stayed true, no notification followed, and the
/// box re-masked on the first click elsewhere with Show still ticked. Driving <c>PasswordChar</c> instead
/// puts the state somewhere focus does not touch.</para>
/// </summary>
[TestFixture]
public class MaskConverterTests
{
    const char Bullet = '•';

    static object Convert(object? reveal) =>
        new MaskConverter().Convert(reveal, typeof(char), null, CultureInfo.InvariantCulture);

    /// <summary>NUL is how a TextBox is told not to mask at all.</summary>
    [Test]
    public void Revealed_MasksWithNothing() => Assert.That(Convert(true), Is.EqualTo('\0'));

    [Test]
    public void Hidden_MasksWithABullet() => Assert.That(Convert(false), Is.EqualTo(Bullet));

    /// <summary>Anything that is not an explicit reveal MASKS. The failure worth avoiding is a secret
    /// shown by accident, so a null or an unexpected type hides rather than reveals.</summary>
    [TestCase(null)]
    [TestCase("true")]
    [TestCase(1)]
    public void AnythingThatIsNotTrue_Masks(object? value) =>
        Assert.That(Convert(value), Is.EqualTo(Bullet));
}

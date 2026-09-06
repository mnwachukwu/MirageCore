using Mirage.Client.Core.Net;
using Mirage.Client.Core.Tests.Input;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests.World;

/// <summary>
/// The session locale is carried on packets, not inferred, and this pins that contract.
///
/// <para>A locale the server has to guess is a locale the server gets wrong. The player can change
/// language at any point — main menu, character select, mid-game — and every one of those moments
/// sits at a different distance from a packet that could carry the new value. The hazard is a
/// handler that emits localized text before a <see cref="SetLanguagePacket"/> could possibly reach
/// it: <see cref="UseCharPacket"/> announces entry to the world (welcome, MOTD, join broadcast) a
/// full round trip before the client even learns it is in-game, so the locale has to arrive with
/// the packet or not at all.</para>
///
/// <para>So the rule is mechanical: if a packet declares a locale, the sender fills it in. Nothing
/// here names a packet, so a new one is covered from the moment it exists.</para>
/// </summary>
[TestFixture]
public class PacketLocaleTests
{
    // Deliberately not a real locale and not the "en" that every Locale property defaults to, so a
    // packet that merely kept its default cannot pass by looking plausible.
    private const string Sentinel = "zz";

    private static bool CarriesLocale(Type t) => t.GetProperty("Locale", typeof(string)) is not null;

    private static string LocaleOf(IPacket p) =>
        (string)p.GetType().GetProperty("Locale", typeof(string))!.GetValue(p)!;

    /// <summary>Every C→S packet the game client can send that declares a locale, sent through the
    /// real sender. Editor packets are excluded: the editor is a separate front end with its own
    /// sender, so this one is not expected to reach <c>EditorLoginPacket</c>.</summary>
    private static List<string> ExpectedLocaleCarryingPackets() =>
        typeof(IPacket).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPacket).IsAssignableFrom(t))
            .Where(CarriesLocale)
            .Where(t => !t.Name.StartsWith("Editor", StringComparison.Ordinal))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    // Synthetic arguments, enough to get each Send method to build its packet. A string parameter
    // NAMED "locale" gets the sentinel: SendSetLanguage takes the locale explicitly rather than
    // reading the provider, and it should be held to the same contract as the rest.
    private static object? ArgFor(ParameterInfo p)
    {
        Type t = p.ParameterType;
        if (t == typeof(string)) return p.Name == "locale" ? Sentinel : "";
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }

    private static FakeTransport SendEverything()
    {
        var transport = new FakeTransport();
        var sender = new ClientPacketSender(transport);
        sender.SetLocaleProvider(() => Sentinel);

        foreach (var m in typeof(ClientPacketSender)
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.DeclaringType == typeof(ClientPacketSender))
                     .Where(m => m.Name.StartsWith("Send", StringComparison.Ordinal)))
        {
            try { m.Invoke(sender, m.GetParameters().Select(ArgFor).ToArray()); }
            catch (TargetInvocationException)
            {
                // A method that rejects the synthetic arguments simply sends nothing. That is not
                // silently forgiven: if it was the only route to a locale-carrying packet, the
                // coverage assertion below reports that packet as unreachable.
            }
        }

        return transport;
    }

    [Test]
    public void EveryLocaleCarryingPacket_IsReachableFromTheSender()
    {
        var reached = SendEverything().Sent
            .Where(p => CarriesLocale(p.GetType()))
            .Select(p => p.GetType().Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.That(reached, Is.EquivalentTo(ExpectedLocaleCarryingPackets()),
            "A packet declaring a locale that no ClientPacketSender method produces means the server "
            + "will fall back to that property's default rather than the player's actual language.");
    }

    [Test]
    public void EveryLocaleCarryingPacket_IsSentWithTheCurrentLocale()
    {
        var carriers = SendEverything().Sent.Where(p => CarriesLocale(p.GetType())).ToList();

        Assert.That(carriers, Is.Not.Empty, "sanity: the client should send locale-carrying packets");
        Assert.Multiple(() =>
        {
            foreach (var p in carriers)
            {
                Assert.That(LocaleOf(p), Is.EqualTo(Sentinel),
                    $"{p.GetType().Name} went out with the wrong locale — its sender is missing "
                    + "'Locale = CurrentLocale'.");
            }
        });
    }

    /// <summary>Setting the property is only half of it: the value has to survive the wire. A missing
    /// or misspelled <c>JsonPropertyName</c> would leave the server reading the default and every
    /// test above still passing.</summary>
    [Test]
    public void LocaleSurvivesTheWire()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in SendEverything().Sent.Where(p => CarriesLocale(p.GetType())))
            {
                var round = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(p).TrimEnd('\n'));
                Assert.That(round, Is.Not.Null, $"{p.GetType().Name} failed to round-trip");
                Assert.That(LocaleOf(round!), Is.EqualTo(Sentinel),
                    $"{p.GetType().Name} lost its locale in serialization — check its JsonPropertyName.");
            }
        });
    }
}

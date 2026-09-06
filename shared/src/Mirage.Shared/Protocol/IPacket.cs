namespace Mirage.Shared.Protocol;

/// <summary>Every wire message. <see cref="Cmd"/> is the discriminator the serializer reads to pick
/// the concrete type, so each implementation returns its own constant from
/// <see cref="PacketNames"/>.</summary>
public interface IPacket
{
    /// <summary>The packet's wire name — a constant from <see cref="PacketNames"/>, never computed.</summary>
    string Cmd { get; }
}

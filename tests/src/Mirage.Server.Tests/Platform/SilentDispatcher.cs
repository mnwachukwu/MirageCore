using Mirage.Server.Core.Net;
using Mirage.Shared.Protocol;
using System.Collections.Generic;

namespace Mirage.Server.Tests.Platform;

/// <summary>An <see cref="IPacketDispatcher"/> that swallows everything — for tests whose subject is world
/// state rather than what went out on the wire.
///
/// <para>Namespace-level rather than nested, so a fixture that needs one does not carry its own copy of
/// every member. Fixtures that nest their own <c>NoOpDispatcher</c> shadow this inside themselves and are
/// unaffected.</para></summary>
public sealed class SilentDispatcher : IPacketDispatcher
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
    public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
    public void SendToEditor(int editorIndex, IPacket packet) { }
    public void SendToAllEditors(IPacket packet) { }
    public void Disconnect(int index) { }
    public void DisconnectEditor(int editorIndex) { }
    public void GracefulDisconnect(int index) { }
    public void GracefulDisconnectEditor(int editorIndex) { }
}

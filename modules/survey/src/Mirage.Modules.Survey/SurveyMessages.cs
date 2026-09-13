using System.Text.Json.Serialization;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;

namespace Mirage.Modules.Survey;

/// <summary>
/// C→S: the surveyor is writing down what is in front of them.
///
/// <para>A module's packet is an ordinary <see cref="IPacket"/> whose command Core has never heard of.
/// The command is namespaced because the registry is flat and one name is owned by one module — a game
/// picking <c>note</c> would eventually meet another that did.</para>
/// </summary>
public sealed record SurveyNotePacket : IPacket
{
    public const string Command = "survey.note";

    [JsonPropertyName("cmd")] public string Cmd => Command;

    /// <summary>Which species they believe it is, by its record number. Zero means "something, not sure
    /// what" — a real thing to record on a survey, and the reason this is not validated away.</summary>
    [JsonPropertyName("species")] public int Species { get; init; }
}

/// <summary>
/// Where <see cref="SurveyNotePacket"/> goes.
///
/// <para><b>Registering the command and routing it are two separate declarations</b>, and a module needs
/// both: the first makes the line deserialize, the second delivers it. This module makes both in
/// <c>SurveyModule.Configure</c>, a line apart.</para>
/// </summary>
public sealed class SurveyRoute : IPacketRoute
{
    private IWorld? _world;

    public string Name => "Survey notes";

    public IReadOnlyCollection<string> Commands { get; } = [SurveyNotePacket.Command];

    public void Begin(IWorld world) => _world = world;

    public void Handle(EntityHandle from, IPacket packet)
    {
        if (_world is not { } world || packet is not SurveyNotePacket note) return;

        var bag = world.AttributesOf(from);
        if (bag is null) return;

        // A note is only worth anything if the species is one this world authored. An unknown number is
        // a client saying something the world cannot support, which is refused by doing nothing.
        if (note.Species > 0 && world.RecordAt(Survey.Species, note.Species) is null) return;

        long found = (bag.TryGet(Survey.Specimens, out var seen) ? seen.AsLong() : 0) + 1;
        world.SetAttributes(from,
        [
            new(Survey.Specimens, found),
            new(Survey.Rank, Survey.RankFor(found)),
        ]);
    }
}

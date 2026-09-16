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
public sealed class SurveyRoute : IPacketRoute, IActionHandler
{
    /// <summary>The action id a stock client sends back when the player picks "Note this down".</summary>
    public const string NoteAction = "survey.note";

    /// <summary>Naming a creature the surveyor is pointing at. Offered on every NPC, which is what makes
    /// a plain body worth right-clicking in this game.</summary>
    public const string IdentifyAction = "survey.identify";

    /// <summary>Comparing findings with another surveyor. Offered on a player rather than on a square,
    /// so the body it is about arrives with it.</summary>
    public const string CompareAction = "survey.compare";

    private IWorld? _world;

    public string Name => "Survey notes";

    public IReadOnlyCollection<string> Commands { get; } = [SurveyNotePacket.Command];

    /// <summary>Opening the book is a client-side act, so this owns the id only to keep the invoke that
    /// accompanies it from naming a handler nothing declared.</summary>
    public const string OpenBookAction = "survey.openbook";

    public IReadOnlyCollection<string> Actions { get; } =
        [NoteAction, OpenBookAction, IdentifyAction, CompareAction];

    public void Begin(IWorld world) => _world = world;

    /// <summary>A client that compiled this module sent the typed packet.</summary>
    public void Handle(EntityHandle from, IPacket packet)
    {
        if (packet is SurveyNotePacket note) Note(from, note.Species);
    }

    /// <summary>A stock client picked the menu item. Same outcome, no species named: a client that was
    /// never compiled against this game cannot know one, and a note saying "something, here" is a real
    /// thing to write on a survey.</summary>
    public void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at,
                       string picked)
    {
        if (actionId == NoteAction) Note(from, species: 0);
        else if (actionId == IdentifyAction) Identify(from, on);
        else if (actionId == CompareAction) Compare(from, on);
        // Opening the book does nothing here: the client already holds every value it shows.
    }

    /// <summary>Say what the surveyor is looking at, and count it.
    ///
    /// <para>The body arrives as a handle and is asked its name through <see cref="IWorld"/> — a game
    /// never receives a slot or an index, so a body that walked off between the click and the read
    /// answers blank rather than naming whoever took its place.</para></summary>
    private void Identify(EntityHandle from, EntityHandle on)
    {
        if (_world is not { } world) return;

        string name = world.NameOf(on);
        if (name.Length == 0)
        {
            world.Tell(from, "There is nothing there to identify.");
            return;
        }

        world.Tell(from, $"You note down: {name}.");
        Note(from, species: 0);
    }

    /// <summary>Two surveyors comparing counts. Both are told, which is the point of it.</summary>
    private void Compare(EntityHandle from, EntityHandle on)
    {
        if (_world is not { } world) return;

        string them = world.NameOf(on);
        if (them.Length == 0)
        {
            world.Tell(from, "There is nobody there to compare notes with.");
            return;
        }

        long mine = world.AttributesOf(from)?[Survey.Specimens].AsLong() ?? 0;
        long theirs = world.AttributesOf(on)?[Survey.Specimens].AsLong() ?? 0;

        world.Tell(from, $"You compare notes with {them}: {mine} against {theirs}.");
        world.Tell(on, $"{world.NameOf(from)} compares notes with you: {theirs} against {mine}.");
    }

    private void Note(EntityHandle from, int species)
    {
        if (_world is not { } world) return;

        var bag = world.AttributesOf(from);
        if (bag is null) return;

        // A note is only worth anything if the species is one this world authored. An unknown number is
        // a client saying something the world cannot support, which is refused by doing nothing.
        if (species > 0 && world.RecordAt(Survey.Species, species) is null) return;

        // Tiring: a note costs what a step costs, so a surveyor cannot stand still and catalog forever.
        long stamina = bag.TryGet(Survey.Stamina, out var left) ? left.AsLong() : 0;
        if (stamina <= 0) return;

        long found = (bag.TryGet(Survey.Specimens, out var seen) ? seen.AsLong() : 0) + 1;
        world.SetAttributes(from,
        [
            new(Survey.Specimens, found),
            new(Survey.Rank, Survey.RankFor(found)),
            new(Survey.Stamina, stamina - Survey.StepCost),
        ]);
    }
}

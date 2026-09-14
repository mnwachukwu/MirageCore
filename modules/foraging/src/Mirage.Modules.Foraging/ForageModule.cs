using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Modules.Foraging;

/// <summary>
/// Foraging — the smallest complete game on this engine, written in C#.
///
/// <para>You walk around picking things. Each pick adds a basket, the count sits on your sidebar, and
/// Q picks without opening a menu. The world also gets a kind of record it did not have: berries,
/// with a name, a ripeness, and a note about where they grow.</para>
///
/// <para>The same game exists as a script at <c>../../world/scripts/rules.cm</c>, and one test holds
/// both to the same assertions. Neither is loaded by the shipped server: they declare the same keys,
/// so loading both would collide.</para>
///
/// <para>Survey is the same engine used properly — several files, a panel, two policies, a message on
/// the wire. This is the one you read first.</para>
/// </summary>
public sealed class ForageModule : ICoreModule, IWorldObserver, IActionHandler
{
    /// <summary>How many baskets they have picked. Owner-visible: it is nobody else's business.</summary>
    public const string Baskets = "forage.baskets";

    /// <summary>The verb, and the id a stock client sends back when the player picks it.</summary>
    public const string Pick = "forage.pick";

    /// <summary>The records this game authors. A folder on disk and a section in the editor.</summary>
    public const string Berries = "berry";

    private IWorld? _world;

    public string Name => "Foraging";

    /// <summary>Runs once, before the world exists. Everything here is about what the game is.</summary>
    public void Configure(ICoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Attributes.Declare(Baskets, AttributeVisibility.Owner);

        builder.AddChoiceSet(new ChoiceSet
        {
            Id = "berry.ripeness",
            Members =
            [
                new KindDescriptor { Id = "green", LabelKey = "Green" },
                new KindDescriptor { Id = "ripe", LabelKey = "Ripe" },
                new KindDescriptor { Id = "overripe", LabelKey = "Overripe" },
            ],
        });

        // The one place the two routes differ in shape. A script reads these fields off a model it
        // already wrote; here the field list is written out, and the choice set above is its own
        // declaration rather than something a type implies.
        builder.AddFamily(new RecordFamily
        {
            Id = Berries,
            LabelKey = "Berries",
            SingularLabelKey = "Berry",
            DefaultLimit = 50,
            Fields =
            [
                new FieldDescriptor
                {
                    Key = "name", LabelKey = "Name", Kind = FieldKind.Text, MaxLength = 40,
                },
                new FieldDescriptor
                {
                    Key = "ripeness", LabelKey = "Ripeness", Kind = FieldKind.Choice,
                    ChoiceSetId = "berry.ripeness",
                },
                new FieldDescriptor { Key = "where", LabelKey = "Found where", Kind = FieldKind.Text },
            ],
        });

        builder.AddDisplayField(new DisplayField
        {
            LabelKey = "Foraging", Style = DisplayStyle.Heading, Ordinal = 0,
        });
        builder.AddDisplayField(new DisplayField
        {
            ValueKey = Baskets, LabelKey = "Baskets", Ordinal = 1,
            Rgb = GameColor.Pack(200, 200, 160),
        });

        builder.AddAction(new GameAction
        {
            Id = Pick,
            LabelKey = "Pick here",
            GroupKey = "Foraging",
            Surface = ActionSurface.Tile,
            Key = "Q",
        });

        // Both halves, or neither works. Implement the interface without registering and the engine
        // never calls it; register without declaring an action id and it sits in a list nothing
        // reaches.
        builder.AddObserver(this);
        builder.AddActionHandler(this);
    }

    /// <summary>The engine is built and the world is loaded.</summary>
    public void Start(IWorld world) => _world = world;

    public IReadOnlyCollection<string> Actions { get; } = [Pick];

    /// <summary>Somebody who has been here before keeps what they had, because their values were
    /// saved with their character.</summary>
    public void OnPlayerJoined(EntityHandle who)
    {
        if (_world is not { } world) return;
        if (world.AttributesOf(who) is not { } bag || bag.Has(Baskets)) return;

        world.SetAttribute(who, Baskets, AttributeValue.From(0L));
        world.Tell(who, "You have a basket and an afternoon.");
    }

    /// <summary>They picked the menu item, or pressed Q.</summary>
    public void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at)
    {
        if (_world is not { } world || actionId != Pick) return;
        if (world.AttributesOf(from) is not { } bag) return;

        long picked = (bag.TryGet(Baskets, out var had) ? had.AsLong() : 0) + 1;

        world.SetAttribute(from, Baskets, AttributeValue.From(picked));
        world.Tell(from, $"You pick what is here. That is {picked} so far.");
    }
}

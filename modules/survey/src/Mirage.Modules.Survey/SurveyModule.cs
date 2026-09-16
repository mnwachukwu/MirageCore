using Mirage.Shared;
using Mirage.Shared.Extensibility;

namespace Mirage.Modules.Survey;

/// <summary>
/// A small game built on Core, and the worked example of how one is built.
///
/// <para><b>It is deliberately not an RPG.</b> Nothing fights, nothing levels, nothing dies. You walk a
/// world writing down what grows there, and walking is tiring. The point of choosing that is to make the
/// engine's neutrality checkable rather than claimed: every seam below is one Core offers, and none of
/// them had to be bent to describe a game about plants.</para>
///
/// <para><b>Everything this game is, is declared in <see cref="Configure"/>.</b> Nothing in Core names
/// this module, nothing in Core was edited to make room for it, and this assembly references
/// <c>Mirage.Shared</c> and nothing else — not the server it runs inside, not the client that draws it,
/// not the editor that authors its records.</para>
/// </summary>
public sealed class SurveyModule : ICoreModule, IConsoleHandler
{
    private readonly SurveyObserver _observer = new();
    private readonly SurveyTick _recovery = new();
    private readonly SurveyRoute _notes = new();
    private readonly TheFindersSpecimen _finds = new();
    private readonly TooTiredToRun _legs = new();

    // Held from Start, because the console asks about the world rather than about a body and there is
    // nothing else on the call to ask.
    private IWorld? _world;

    public string Name => "Survey";

    public void Configure(ICoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DeclareWhatASurveyorCarries(builder);
        DeclareWhatTheWorldHolds(builder);
        DeclareWhatThePlayerSees(builder);

        // Both halves of a module's own message: the command so a line naming it deserializes, and the
        // route so the packet it becomes reaches this game rather than nobody.
        builder.Packets.Register<SurveyNotePacket>(SurveyNotePacket.Command);
        builder.AddPacketRoute(_notes);

        // The same thing a stock client can offer, with no packet type to compile against: a caption in
        // the square's menu, and the id it sends back.
        builder.AddAction(new GameAction
        {
            Id = SurveyRoute.NoteAction,
            LabelKey = "Note this down",
            GroupKey = "Survey",
            Surface = ActionSurface.Tile,
            Key = "Q",
        });
        builder.AddActionHandler(_notes);

        // A screen of this game's own, opened from the same menu. It costs a declaration: the body is
        // display fields on its own surface, the button is an action that already exists.
        builder.AddPanel(new GamePanel
        {
            Id = Survey.FieldBook,
            TitleKey = "Field Book",
            Surface = Survey.BookSurface,
            Width = 240,
            Height = 180,
            Buttons = [new PanelButton("Note this down", SurveyRoute.NoteAction)],
            Key = "B",
        });
        // On the HUD rather than in a square's menu: a book is about the surveyor, not about the
        // ground they happen to be standing on.
        builder.AddAction(new GameAction
        {
            Id = SurveyRoute.OpenBookAction,
            LabelKey = "Field Book",
            GroupKey = "Survey",
            Surface = ActionSurface.Hud,
            Ordinal = 1,
            OpensPanel = Survey.FieldBook,
        });

        // And a verb about a BODY, which is what a survey is mostly for. Offered on any NPC, including
        // one with nothing else to say — declaring this is what gives a plain creature a menu at all.
        builder.AddAction(new GameAction
        {
            Id = SurveyRoute.IdentifyAction,
            LabelKey = "Identify",
            GroupKey = "Survey",
            Surface = ActionSurface.Npc,
            Ordinal = 2,
        });

        // The same verb's shape aimed at a person. One id per surface rather than one id offered on
        // several: what a game does about a colleague is rarely what it does about a creature, and an id
        // that meant both would have to ask which it got.
        //
        // And the one verb here with a CONDITION: comparing notes needs notes. A surveyor who has written
        // nothing down sees the entry grayed rather than missing, so they can tell the verb exists and
        // that they are not ready for it — and it lights up the moment they record something.
        builder.AddAction(new GameAction
        {
            Id = SurveyRoute.CompareAction,
            LabelKey = "Compare notes",
            GroupKey = "Survey",
            Surface = ActionSurface.Player,
            Ordinal = 3,
            When = ActionCondition.AtLeast(Survey.Specimens, 1),
        });

        builder.AddTickWork(_recovery);
        builder.AddObserver(_observer);
        builder.AddDeathPolicy(new NothingDiesHere());
        builder.AddLingerPolicy(new StayWhileSurveying());
        builder.AddLootPolicy(_finds);
        builder.AddMovePolicy(_legs);
        builder.AddConsoleHandler(this);
    }

    /// <summary>A census, from the server's own console.
    ///
    /// <para>The operator's console and nowhere else — a surveyor counting their own world's species
    /// from inside it would be a different game. What belongs here is what somebody RUNNING the world
    /// wants: how much of it has been authored, answered without a client.</para></summary>
    public string? Console(string command, string rest)
    {
        if (!string.Equals(command, "/species", StringComparison.OrdinalIgnoreCase)) return null;
        if (_world is null) return "The world is not up yet.";

        int authored = _world.RecordCount(Survey.Species);
        int named = _world.RecordsOf(Survey.Species)
            .Count(r => r.TryGet("name", out var n) && n.AsText().Length > 0);

        return $"{named} species named, of {authored} slot(s).";
    }

    /// <summary>The engine is built and the world is loaded. This is where the module stops describing
    /// itself and starts holding the thing it acts through.</summary>
    public void Start(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;

        // The authored species, read once. They are AttributeBags rather than a type this assembly
        // compiled, because the editor that wrote them never referenced this assembly either.
        var catalog = world.RecordsOf(Survey.Species)
            .Select(r => r.TryGet("name", out var n) ? n.AsText() : "")
            .Where(n => n.Length > 0)
            .ToArray();

        _observer.Begin(world, catalog);
        _recovery.Begin(world);
        _notes.Begin(world);
        _finds.Begin(world);
        _legs.Begin(world);
    }

    // ── What a surveyor carries ───────────────────────────────────────────────

    private static void DeclareWhatASurveyorCarries(ICoreBuilder builder)
    {
        // Viewport: an onlooker sees how tired somebody is and what they have earned, because both are
        // drawn over their head or beside their name. Owner would hide them from everyone but the
        // player themselves, which is the right answer for a purse and the wrong one for these.
        builder.Attributes
            .Declare(Survey.Stamina, AttributeVisibility.Viewport)
            .Declare(Survey.StaminaMax, AttributeVisibility.Viewport)
            .Declare(Survey.Rank, AttributeVisibility.Viewport)
            .Declare(Survey.Specimens, AttributeVisibility.Owner);

        builder.AddEquipSlot(new EquipSlot { Key = Survey.Satchel, LabelKey = "Satchel", Ordinal = 0 });
    }

    // ── What the world holds ──────────────────────────────────────────────────

    private static void DeclareWhatTheWorldHolds(ICoreBuilder builder)
    {
        builder.AddChoiceSet(new ChoiceSet
        {
            Id = Survey.Habitats,
            Members =
            [
                new KindDescriptor { Id = "shore", LabelKey = "Shore" },
                new KindDescriptor { Id = "wood", LabelKey = "Woodland" },
                new KindDescriptor { Id = "meadow", LabelKey = "Meadow" },
            ],
        });

        // One registration buys the folder on disk, the blank padding, load and save, the editor's rail
        // section and list, row locking, hot reload, world transfer and the world check — for a family
        // whose name Core will never contain.
        builder.AddFamily(new RecordFamily
        {
            Id = Survey.Species,
            Directory = "species",
            FilePrefix = "species",
            LabelKey = "Species",
            SingularLabelKey = "Species",
            DefaultLimit = 200,
            Fields =
            [
                new FieldDescriptor
                {
                    Key = "name", LabelKey = "Common name", Kind = FieldKind.Text,
                    MaxLength = 40, Required = true,
                },
                new FieldDescriptor
                {
                    Key = "habitat", LabelKey = "Habitat", Kind = FieldKind.Choice,
                    ChoiceSetId = Survey.Habitats,
                },
                new FieldDescriptor
                {
                    Key = "notes", LabelKey = "Field notes", Kind = FieldKind.Text, MaxLength = 240,
                    HintKey = "What to look for. Shown to nobody yet; authored so it is there when something reads it.",
                },
            ],
        });
    }

    // ── What the player sees ──────────────────────────────────────────────────

    private static void DeclareWhatThePlayerSees(ICoreBuilder builder)
    {
        builder.AddOverheadBar(new OverheadBar
        {
            ValueKey = Survey.Stamina,
            MaxKey = Survey.StaminaMax,
            Rgb = GameColor.Pack(120, 190, 90),
        });

        // What this module finds and notes down, kept off the main tab. A surveyor walking a meadow
        // fills the log with specimens, and somebody who wanted to talk to them would never see it.
        builder.AddChatChannel(new ChatChannelSpec
        {
            Id = Survey.Findings,
            LabelKey = "Findings",
            Rgb = GameColor.Pack(120, 190, 90),
            OwnTabKey = "Field notes",
        });

        // The sidebar, in order. Each row reads an attribute this module declared above, so a number
        // changing is the whole of what it takes to change what the player reads.
        builder.AddDisplayField(new DisplayField
        {
            LabelKey = "Survey", Style = DisplayStyle.Heading, Ordinal = 0,
        });
        builder.AddDisplayField(new DisplayField
        {
            ValueKey = Survey.Rank, LabelKey = "Rank", Ordinal = 1,
            Rgb = GameColor.Pack(200, 200, 160),
        });
        builder.AddDisplayField(new DisplayField
        {
            ValueKey = Survey.Specimens, LabelKey = "Specimens", Ordinal = 2,
            Rgb = GameColor.Pack(200, 200, 160),
        });
        builder.AddDisplayField(new DisplayField
        {
            ValueKey = Survey.Stamina, MaxKey = Survey.StaminaMax, LabelKey = "Stamina",
            Style = DisplayStyle.Meter, Ordinal = 3, Rgb = GameColor.Pack(120, 190, 90),
        });

        // The field book shows the same values at more length, on a surface of its own. One declaration
        // per row, exactly as the sidebar takes — a surface is a name, not a kind of thing.
        builder.AddDisplayField(new DisplayField
        {
            Surface = Survey.BookSurface, LabelKey = "Field record", Style = DisplayStyle.Heading, Ordinal = 0,
        });
        builder.AddDisplayField(new DisplayField
        {
            Surface = Survey.BookSurface, ValueKey = Survey.Rank, LabelKey = "Rank", Ordinal = 1,
            Rgb = GameColor.Pack(200, 200, 160),
        });
        builder.AddDisplayField(new DisplayField
        {
            Surface = Survey.BookSurface, ValueKey = Survey.Specimens, LabelKey = "Specimens cataloged",
            Ordinal = 2, Rgb = GameColor.Pack(200, 200, 160),
        });
        builder.AddDisplayField(new DisplayField
        {
            Surface = Survey.BookSurface, ValueKey = Survey.Stamina, MaxKey = Survey.StaminaMax,
            LabelKey = "Stamina", Style = DisplayStyle.Meter, Ordinal = 3,
            Rgb = GameColor.Pack(120, 190, 90),
        });
    }
}

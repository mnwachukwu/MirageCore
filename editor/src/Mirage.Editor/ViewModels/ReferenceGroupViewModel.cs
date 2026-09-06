namespace Mirage.Editor.ViewModels;

/// <summary>
/// One relationship's worth of inbound references — a heading and the records under it, e.g.
/// "Dropped by" over three NPCs.
///
/// <para>Grouped rather than one flat list because the same record can be reached several different ways: an
/// item is dropped by mobs, stocked by shops, rewarded by quests and burned as a reagent, and a flat column of
/// names would not say which is which.</para>
/// </summary>
public sealed class ReferenceGroupViewModel
{
    public string Header { get; }
    public IReadOnlyList<ReferenceLinkViewModel> Links { get; }

    public ReferenceGroupViewModel(string header, IReadOnlyList<ReferenceLinkViewModel> links)
    {
        Header = header;
        Links = links;
    }
}

using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Mirage.Editor.Localization;

/// <summary>
/// Base for the editor's long-lived section views. Captions in these views are assigned in code
/// rather than bound, and a section view is constructed once and reused for the life of the
/// window — so without a re-apply hook a language switch leaves the view in the old language
/// until the editor restarts.
///
/// <para>The subscription lives here, not in each view, so a new view cannot forget it: the only
/// thing a derived view owns is <see cref="ApplyStrings"/> — WHAT to re-apply, never WHEN.
/// <c>LocalizationConventionTests</c> enforces that every view under <c>Views/</c> derives from
/// this type.</para>
///
/// <para>Subscribing on attach rather than in the constructor keeps this leak-free (a detached
/// view holds no reference from the static event), and re-applying on attach covers the case
/// where the language changed while this section was switched away.</para>
/// </summary>
public abstract class LocalizedUserControl : UserControl
{
    /// <summary>Push the current language into every caption, tooltip, and placeholder this view
    /// sets in code. Must be idempotent: it runs on construction, on every attach, and on every
    /// language change.</summary>
    protected abstract void ApplyStrings();

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        ApplyStrings();   // catch up on any switch made while this section was detached
        EditorStrings.LanguageChanged += ApplyStrings;
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        EditorStrings.LanguageChanged -= ApplyStrings;
        base.OnDetachedFromLogicalTree(e);
    }
}

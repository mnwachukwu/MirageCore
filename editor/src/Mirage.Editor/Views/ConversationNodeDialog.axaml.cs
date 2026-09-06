using Avalonia.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor.Views;

/// <summary>One dialogue node, opened by clicking its box on the conversation graph.
///
/// <para>Edits land on the node straight away, the same as typing into the text view does — Discard on the
/// conversation is what takes them back, so the dialog needs no Cancel and carries no copy of the node to
/// merge back in. Nothing refuses the close either: a modal that can be opened but not dismissed is what
/// froze this editor once already.</para></summary>
public partial class ConversationNodeDialog : Window
{
    public ConversationNodeDialog() { InitializeComponent(); }

    public ConversationNodeDialog(ConversationRowViewModel conversation, ConversationNodeRowViewModel node)
        : this()
    {
        DataContext = node;
        Title = EditorStrings.WindowTitle(EditorStrings.Format(EditorStrings.ConversationEditor_NodeDialogTitle, ("Id", node.NodeId)));

        var deleteBtn = this.FindControl<Button>("DeleteButton")!;
        deleteBtn.Content = EditorStrings.Get(EditorStrings.ConversationEditor_DeleteNode);
        deleteBtn.Click += (_, _) =>
        {
            conversation.RemoveNodeCommand.Execute(node);
            this.CloseDeferred();
        };

        var closeBtn = this.FindControl<Button>("CloseButton")!;
        closeBtn.Content = EditorStrings.Get(EditorStrings.Common_Close);
        closeBtn.Click += (_, _) => this.CloseDeferred();
    }
}

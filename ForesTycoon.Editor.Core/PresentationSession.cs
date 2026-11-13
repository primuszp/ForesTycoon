using System;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor.Core;

/// <summary>Presentation edits participate in the document's transaction and undo boundary.</summary>
internal sealed class PresentationSession(EditorDocument document)
{
    internal PresentationProfile Read() => new(document.Presentation);
    internal void Edit(Action<PresentationBindings> edit)
    {
        var next = document.Presentation.Clone(); edit(next); next.Validate();
        document.History.Execute("animáció- és hangkötés módosítása", () => {
            document.Presentation = next.Clone(); document.Invalidate();
        });
    }
}

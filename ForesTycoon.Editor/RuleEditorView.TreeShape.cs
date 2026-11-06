namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    internal void ShowSpeciesAppearance(int index, int phase = 0) { ShowWorldTypes(TypeTab.Species, index); ui.Species.Tab = 1; ui.Tree.Phase = phase; }
    internal void ApplyEditsForTest(GameWorld world) => ApplyProject(world, false);
    internal void ShowSpeciesColoursForTest(int index, int season, float zoom) {
        ShowWorldTypes(TypeTab.Species, index); ui.Species.Tab = 2; ui.Tree.Season = season; ui.Tree.Zoom = zoom; ui.Tree.Pitch = zoom > 2 ? .12f : .28f;
    }
    internal void SetTreePreviewForTest(int mode, int level, bool seeThrough) { ui.Tree.Mode = mode; ui.Tree.TestLevel = level; ui.Tree.SeeThrough = seeThrough; }
    internal string TreePreviewError => treeEditor.TreePreviewError;
    internal bool TreePreviewReady => treeEditor.TreePreviewReady;
}

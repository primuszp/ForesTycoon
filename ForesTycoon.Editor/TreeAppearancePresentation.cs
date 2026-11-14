using System;
using ForesTycoon.TreeModels;

namespace ForesTycoon.Editor;

internal static class TreeAppearancePresentation
{
    internal static readonly string[] PhaseLabels = { "Magonc", "Csemete", "Fiatal", "Érett", "Idős", "Öreg" };
    internal const int DeadTab = 7;
    /// <summary>What the preview builds from: the species' authored form and colours.</summary>
    internal static string LookRevision(object context, ForestSpecies species) => ForestSpeciesBook.WithContext(context,
        () => (ForestSpeciesBook.Shape(species)?.Revision ?? "") + "#" + (ForestSpeciesBook.Look(species)?.Revision ?? ""));
    // The dead tree is shown as an old tree that died.
    internal static TreeLifePhase Phase(int selection) => selection == DeadTab ? TreeLifePhase.Old : (TreeLifePhase)Math.Clamp(selection - 1, 0, 5);

    /// <summary>The branch level a parameter shapes, from its name ("1Length" → 1) or its group; -1 for the whole tree.</summary>
    internal static int BranchLevel(TreeShapeParameters.Entry e) =>
        char.IsDigit(e.Name[0]) ? e.Name[0] - '0' : e.Group switch { "Törzs" => 0, "Vázágak" => 1, "Mellékágak" => 2, _ => -1 };

}

using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal static class WorkshopDraftStore
{
    /// <summary>Saves a repairable draft next to the published project, which it never changes.</summary>
    internal static string Save(string publishedPath, RuleProject document)
    {
        string destination = RuleWorkshopModel.DraftPath(RuleWorkshopModel.PublishedPath(publishedPath));
        new ProjectPersistence().Save(destination, document);
        return destination;
    }
}

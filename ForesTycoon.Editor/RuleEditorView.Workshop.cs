using System;
using ForesTycoon.Rules;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private WorkshopVersions workshopVersions => workflow.Versions;
    private string publishedProjectRevision => workflow.PublishedProjectRevision;
    private RuleProjectStatus gameProjectStatus;
    private string gameStatusPath;
    private DateTimeOffset nextGameStatusRead;

    private void MarkDocumentChanged()
    {
        document.Invalidate();
        statusFrame = -1000;
    }
    private string AuthoringRevision(GameWorld world) => document.Revision();
    private void RefreshWorkshopRevision(GameWorld world)
    {
        workshopVersions.Observe(AuthoringRevision(world));
        var now = DateTimeOffset.UtcNow;
        if (now >= nextGameStatusRead || gameStatusPath != projectPath) {
            gameStatusPath = projectPath;
            nextGameStatusRead = now.AddSeconds(1);
            gameProjectStatus = RuleProjectStatus.Read(gameStatusPath, now);
        }
        workshopVersions.Acknowledge(gameProjectStatus?.AppliedRevision);
    }
    internal void ShowWorkshopComparison(RuleTrialResult result)
    {
        workshopVersions.CompleteTrial(result.DraftRevision);
    }
}

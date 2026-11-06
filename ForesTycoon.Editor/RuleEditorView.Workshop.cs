using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class RuleEditorView
{
    private WorkshopVersions workshopVersions => workflow.Versions;
    private string publishedProjectRevision => workflow.PublishedProjectRevision;

    private void MarkDocumentChanged()
    {
        document.Invalidate();
        statusFrame = -1000;
    }
    private string AuthoringRevision(GameWorld world) => document.Revision();
    private void RefreshWorkshopRevision(GameWorld world)
    {
        workshopVersions.Observe(AuthoringRevision(world));
        bool fresh = liveObjects != null && DateTime.TryParse(liveObjects.PublishedUtc, out var date)
            && DateTime.UtcNow - date.ToUniversalTime() <= TimeSpan.FromSeconds(15);
        workshopVersions.Acknowledge(fresh ? liveObjects.ProjectRevision : null);
    }
    internal void ShowWorkshopComparison(RuleTrialResult result)
    {
        workshopVersions.CompleteTrial(result.DraftRevision);
    }
}

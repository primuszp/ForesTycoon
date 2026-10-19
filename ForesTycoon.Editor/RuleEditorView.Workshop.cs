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
    private readonly WorkshopVersions workshopVersions = new();
    private string publishedProjectRevision;

    private string AuthoringRevision(GameWorld world)
    {
        var project = CollectProject(world); project.Algorithms = null;
        return project.ContentRevision() + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(authoring)))) + ":" + algorithms.ChangeVersion + ":" + algorithms.Workspace;
    }
    private void RefreshWorkshopRevision(GameWorld world)
    {
        workshopVersions.Observe(AuthoringRevision(world));
        bool fresh = liveObjects != null && DateTime.TryParse(liveObjects.PublishedUtc, out var date)
            && DateTime.UtcNow - date.ToUniversalTime() <= TimeSpan.FromSeconds(15);
        workshopVersions.Acknowledge(fresh ? liveObjects.ProjectRevision : null);
    }
    private void SaveProjectDraft(GameWorld world)
    {
        if (algorithms.IsLoading) throw new InvalidOperationException("A gráfok beolvasása még tart. A befejezés után a teljes munkapéldány menthető.");
        draftGuard.RequireUnchanged(RuleWorkshopModel.DraftPath(projectPath));
        ProjectHistory.Archive(RuleWorkshopModel.DraftPath(projectPath));
        string destination = WorkshopDraftStore.Save(projectPath, CollectProject(world), algorithms.Document, algorithms.Workspace);
        draftGuard.Observe(destination);
        RefreshWorkshopRevision(world); workshopVersions.MarkSaved();
        message = "Munkapéldány mentve. A játékban futó szabályokat nem módosította. " + destination;
    }
    private void WorkshopStatus()
    {
        ImGui.Separator();
        string status = message.Split('\n')[0];
        EditorStyle.Text(HudTheme.Muted, status.Length > 115 ? status[..115] + "…" : status);
        if (message.Length > 0) { ImGui.SameLine(); if (ImGui.SmallButton("Részletek")) ImGui.OpenPopup("workshop-message"); }
        if (ImGui.BeginPopupModal("workshop-message", ImGuiWindowFlags.AlwaysAutoResize)) {
            ImGui.InputTextMultiline("##workshop-diagnostic", ref message, 30000, new Vec2(820, 420), ImGuiInputTextFlags.ReadOnly);
            if (ImGui.Button("Bezárás")) ImGui.CloseCurrentPopup(); ImGui.EndPopup();
        }
    }
    internal void ShowWorkshopComparison(RuleTrialResult result)
    {
        trialResult = result; workshopVersions.CompleteTrial(result.DraftRevision);
    }
}

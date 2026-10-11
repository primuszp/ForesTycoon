using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ForesTycoon.Rules;
using ForesTycoon.RuleCompiler;

namespace ForesTycoon.Editor;

internal static class WorkshopDraftStore
{
    /// <summary>Save repairable drafts separately, with immutable graph files so a failed/later save cannot change an older snapshot.</summary>
    internal static string Save(string publishedPath, RuleProject document, AlgorithmBundle algorithms, string workspace)
    {
        string published = RuleWorkshopModel.PublishedPath(publishedPath);
        var snapshot = document.Clone();
        if (algorithms != null) {
            string json = algorithms.ToJson();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            string bundlePath = Path.Combine(published + ".draft.algorithms", hash + ".json");
            if (!File.Exists(bundlePath)) RuleProject.WriteAtomic(bundlePath, json);
            snapshot.Algorithms = new() { BundlePath = bundlePath, WorkspaceRoot = Path.GetFullPath(workspace), Revision = "draft" };
        }
        string destination = RuleWorkshopModel.DraftPath(published);
        RuleProject.WriteAtomic(destination, snapshot.ToJson());
        return destination;
    }
}

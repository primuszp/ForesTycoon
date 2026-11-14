#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace ForesTycoon.Rules;

/// <summary>Small, versioned game status message independent of the world object catalog.</summary>
internal sealed record RuleProjectStatus(Guid SessionId, string? AppliedRevision, string? AlgorithmRevision,
    ulong? AppliedTick, string? RejectedRevision, string? Error, DateTimeOffset ObservedUtc)
{
    public int Version { get; init; } = 1;

    internal static RuleProjectStatus? Read(string projectPath, DateTimeOffset now)
    {
        try {
            using var file = new FileStream(PathFor(projectPath), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var status = JsonSerializer.Deserialize<RuleProjectStatus>(file);
            return status is { Version: 1 } && status.SessionId != Guid.Empty
                && now - status.ObservedUtc <= TimeSpan.FromSeconds(15) && status.ObservedUtc <= now ? status : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal void Write(string projectPath) => RuleProject.WriteAtomic(PathFor(projectPath), JsonSerializer.Serialize(this));

    private static string PathFor(string projectPath) => projectPath + ".project-status.json";
}

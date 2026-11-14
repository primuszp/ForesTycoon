using System;
using System.IO;
using System.Text.Json;

namespace ForesTycoon;

internal sealed record RuntimeHandoffAcknowledgement(string ProjectRevision, string AlgorithmRevision, ulong Tick)
{
    internal void Write(string path) => RuleProject.WriteAtomic(path, JsonSerializer.Serialize(this));

    internal bool Matches(string path)
    {
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return this == JsonSerializer.Deserialize<RuntimeHandoffAcknowledgement>(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
}

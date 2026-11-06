#nullable enable
using System;
using System.IO;

namespace ForesTycoon.Editor.Core;

internal static class ProjectPaths
{
    internal static string PublishedPath(string file)
    {
        string full = Path.GetFullPath(file);
        return full.EndsWith(".draft.json", StringComparison.OrdinalIgnoreCase) ? full[..^11] : full;
    }
    internal static string DraftPath(string published) => PublishedPath(published) + ".draft.json";
}

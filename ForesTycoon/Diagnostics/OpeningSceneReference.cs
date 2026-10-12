using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForesTycoon;

internal static class OpeningSceneReference
{
    internal static SortedDictionary<string, string> Fingerprints(GameWorld world)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var checkpoint = JsonDocument.Parse(JsonSerializer.Serialize(world.CaptureCheckpoint()));
        foreach (var field in checkpoint.RootElement.EnumerateObject())
        {
            // The compiled source revision changes when the loader is refactored, without changing the scene.
            if (field.Name == "Algorithms") continue;
            result[field.Name] = Hash(field.Value.GetRawText());
        }
        result["Graphics"] = Hash(JsonSerializer.Serialize(typeof(GraphicsSettings)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToDictionary(f => f.Name, f => f.GetValue(world.Graphics))));
        return result;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

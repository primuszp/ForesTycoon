#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using ForesTycoon.Rules;
namespace ForesTycoon.Editor.Core;

internal sealed partial class EditorSceneCommands
{
    internal void Resize(int size) => EditWorld(world => {
        world.Size = size; world.SoilPaint.Clear(); world.HeightPaint.Clear(); world.ForestPaint.Clear(); world.ForestFelling.Clear(); world.Springs.Clear();
    });
    internal void SetRelief(int relief) => EditWorld(world => {
        if (JsonSerializer.Serialize(world.ElevationZones) == JsonSerializer.Serialize(ElevationZoneDefinition.Defaults(world.MaxHeight)))
            world.ElevationZones = ElevationZoneDefinition.Defaults(relief);
        world.MaxHeight = relief;
    });
    internal void PaintSoil(IEnumerable<int> tiles, string soil) => EditWorld(world => {
        foreach (int tile in tiles) world.SoilPaint[tile] = soil;
    });
    internal void PaintForest(IEnumerable<int> eligibleTiles, string species) => EditWorld(world => {
        foreach (int tile in eligibleTiles) {
            if (species.Length == 0) { if (!world.ForestFelling.Contains(tile)) world.ForestFelling.Add(tile); }
            else { world.ForestFelling.Remove(tile); world.ForestPaint[tile] = species; }
        }
    });
    internal void PaintHeights(IEnumerable<KeyValuePair<int, int>> heights) => EditWorld(world => {
        foreach (var (node, height) in heights) world.HeightPaint[node] = height;
    });
    internal void PaintSprings(IEnumerable<int> tiles, double? rate) => EditWorld(world => {
        foreach (int tile in tiles) {
            if (rate.HasValue) world.Springs[tile] = Math.Round(rate.Value);
            else world.Springs.Remove(tile);
        }
    });
}

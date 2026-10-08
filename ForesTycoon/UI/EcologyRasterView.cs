using System;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>Read-only top-down view. Opening, selecting and drawing never advance ecology.</summary>
    internal static class EcologyRasterView
    {
        internal static void Draw(SoilLandscape soils, EnvironmentSystem water, ref int layer, ref int selected)
        {
            ImGui.Combo("Réteg", ref layer, "Talajtípus\0Elérhető gyökérzónavíz\0Hőmérséklet\0Csapadékszorzó\0Páratartalom\0");
            var grid = soils.Grid;
            float width = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var size = new Vector2(width, width * grid.Rows / grid.Columns);
            // At most 4096 rectangles; large maps use representative centre samples.
            int columns = Math.Min(64, grid.Columns), rows = Math.Min(64, grid.Rows);
            var start = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##ecology-raster", size);
            bool hovered = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            for (int x = 0; x < columns; x++)
                for (int y = 0; y < rows; y++)
                {
                    int column = (int)((x + .5) * grid.Columns / columns);
                    int row = (int)((y + .5) * grid.Rows / rows);
                    int id = grid.TileId(column, row);
                    uint color = soils.Profile(id).Color;
                    if (layer == 1)
                    {
                        float available = (float)soils.Profile(id).Properties.Availability(water.Cell(id).Soil);
                        color = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(new(.65f, .31f, .15f, 1), new(.15f, .55f, .85f, 1), available));
                    }
                    else if (layer >= 2)
                    {
                        var climate = water.ClimateAt(id);
                        float value = layer == 2 ? (float)((climate.Forcing.Temperature + 10) / 40) :
                            layer == 3 ? (float)((climate.RainMultiplier - .65) / .7) : (float)climate.Forcing.Humidity;
                        color = ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(new(.2f, .4f, .85f, 1), new(.9f, .35f, .15f, 1), Math.Clamp(value, 0, 1)));
                    }
                    draw.AddRectFilled(start + new Vector2(x * size.X / columns, y * size.Y / rows),
                        start + new Vector2((x + 1) * size.X / columns, (y + 1) * size.Y / rows), color);
                }
            if (hovered)
            {
                var position = ImGui.GetMousePos() - start;
                int x = Math.Clamp((int)(position.X / size.X * grid.Columns), 0, grid.Columns - 1);
                int y = Math.Clamp((int)(position.Y / size.Y * grid.Rows), 0, grid.Rows - 1);
                int id = grid.TileId(x, y);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selected = id;
                ImGui.BeginTooltip();
                ImGui.Text($"Csempe {id} ({x}, {y}) · {soils.Profile(id).Name}");
                ImGui.Text($"Elérhető víz: {soils.Profile(id).Properties.Availability(water.Cell(id).Soil):P0}");
                var local = water.ClimateAt(id);
                ImGui.Text($"Hőmérséklet: {local.Forcing.Temperature:0.0} °C · páratartalom: {local.Forcing.Humidity:P0}");
                ImGui.Text($"Csapadékszorzó: {local.RainMultiplier:0.00}× · eső: {water.RainRate * local.RainMultiplier:0.0} mm/óra");
                ImGui.EndTooltip();
            }
            if ((uint)selected < (uint)grid.Count)
            {
                var p = start + new Vector2(selected / grid.Rows * size.X / grid.Columns, selected % grid.Rows * size.Y / grid.Rows);
                draw.AddRect(p, p + new Vector2(size.X / grid.Columns, size.Y / grid.Rows), 0xffffffff, 0, ImDrawFlags.None, 2);
            }
            if (layer == 0)
                foreach (var profile in soils.Definition.Catalog.Profiles)
                {
                    ImGui.ColorButton("##legend-" + profile.Id, ImGui.ColorConvertU32ToFloat4(profile.Color),
                        ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(12, 12));
                    ImGui.SameLine(); ImGui.TextUnformatted(profile.Name);
                }
            else ImGui.TextUnformatted(layer switch {
                1 => "Barna: 0% · kék: 100% elérhető gyökérzónavíz",
                2 => "Kék: -10 °C · piros: 30 °C (a skálán kívüli érték telített)",
                3 => "Kék: 0,65× · piros: 1,35× csapadék (az egér pontos értéket mutat)",
                _ => "Kék: 0% · piros: 100% páratartalom"
            });
            ImGui.TextDisabled($"{grid.Columns} × {grid.Rows} csempe · idő: {water.Time:0.0} s");
            if (grid.Columns > columns || grid.Rows > rows) ImGui.TextDisabled("Áttekintő mintavétel; az egér pontos csempeadatot mutat.");
            if ((uint)selected < (uint)grid.Count)
            {
                var profile = soils.Profile(selected); var properties = profile.Properties;
                ImGui.SeparatorText($"Csempe {selected} · {profile.Name}");
                HudTheme.KeyValue("Telítési vízkészlet", $"{properties.Saturation:0} mm");
                HudTheme.KeyValue("Szabadföldi vízkapacitás", $"{properties.FieldCapacity:0} mm");
                HudTheme.KeyValue("Hervadáspont", $"{properties.WiltingPoint:0} mm");
                HudTheme.KeyValue("Beszivárgási korlát", $"{properties.InfiltrationPerHour:0.0} mm/óra");
                HudTheme.KeyValue("Termékenység", $"{properties.Fertility:P0}");
                var local = water.ClimateAt(selected);
                HudTheme.KeyValue("Hőmérséklet", $"{local.Forcing.Temperature:0.0} °C");
                HudTheme.KeyValue("Csapadékszorzó", $"{local.RainMultiplier:0.00}×");
                HudTheme.KeyValue("Páratartalom", $"{local.Forcing.Humidity:P0}");
                if (water.Climate.Uniform) ImGui.TextDisabled("Történeti egységes klímamodell");
            }
        }
    }
}

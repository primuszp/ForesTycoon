using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;
using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace ForesTycoon
{
    /// <summary>
    /// Giving orders on the map, Transport Tycoon style: the vehicle's "Utasítás" button turns the cursor into a target
    /// picker. Valid targets glow on the ground; the tile under the cursor is green or red with the reason beside the
    /// cursor; a route preview runs from the vehicle (or the picked source) to the cursor. Vehicles at work show their
    /// routes while the vehicles window is open, the one under the mouse in the window strongly. Every stack carries a
    /// label with its number and volume.
    /// </summary>
    sealed partial class Viewport
    {
        private static readonly Color ProcessorColour = Color.FromArgb(240, 176, 48);
        private static readonly Color ForwarderColour = Color.FromArgb(120, 200, 96);
        private static readonly Color TruckColour = Color.FromArgb(110, 176, 236);
        // The vehicle row under the mouse in the vehicles window ("m12" / "t3"), to draw its route strongly.
        private string hoveredFleetRow;
        // Route previews are searched when their ends change, not every frame.
        private readonly Dictionary<(int, int, bool), int[]> routeCache = new();
        private ulong routeCacheRevision;
        private (int Tile, string Reason) hoverCheck = (-1, null);

        private int[] CachedRoute(int from, int to, bool truck, ForestMachine machine = null, FleetTruck unit = null)
        {
            var logistics = world.Logistics;
            if (logistics.Behaviors != null) return truck ? logistics.PreviewTruckRoute(from, to, unit ?? SendTruckUnit)
                : logistics.PreviewMachinePath(from, to, machine ?? SendMachine);
            ulong revision = (ulong)world.RoadCount * 7919 + (ulong)world.SkidTrailCount * 31 + (ulong)logistics.Stacks.Count;
            if (revision != routeCacheRevision) { routeCache.Clear(); routeCacheRevision = revision; }
            if (!routeCache.TryGetValue((from, to, truck), out var route))
                routeCache[(from, to, truck)] = route = truck ? logistics.PreviewTruckRoute(from, to) : logistics.PreviewMachinePath(from, to);
            return route;
        }

        private ForestMachine SendMachine => sendTruck ? null : world.Logistics?.Machines.Find(m => m.Id == sendVehicleId);
        private FleetTruck SendTruckUnit => sendTruck ? world.Logistics?.Trucks.Find(t => t.Id == sendVehicleId) : null;

        private int SendVehicleTile()
        {
            if (SendMachine is { } m) return m.Tile;
            if (SendTruckUnit is { } t)
            {
                if (t.Vehicle != null) { t.Vehicle.GetSegment(t.Vehicle.RoutePosition, out int from, out _, out _); return from; }
                var docks = world.Logistics.Depots.Count > 0 ? t.Home.TileId : -1;
                return docks;
            }
            return -1;
        }

        /// <summary>Why the tile under the cursor is not a valid pick for the present step, or null.</summary>
        private string CheckPick(int tile)
        {
            var logistics = world.Logistics;
            if (tile < 0) return "";
            if (!sendNeedsDestination) return SendMachine is { } m ? logistics.CheckProcessorStack(m, tile) : "";
            if (sendSource < 0) return logistics.CheckSource(tile, sendTruck, SendVehicleTile(), SendMachine);
            return logistics.CheckDestination(sendSource, tile, sendTruck, SendMachine, SendTruckUnit);
        }

        /// <summary>Rebuilds what the ground shows for orders; called once per frame before the world is drawn.</summary>
        private void UpdateOrderOverlay()
        {
            var o = world.Orders;
            o.Clear();
            var logistics = world.Logistics;
            if (logistics == null) return;
            if (interaction.ActiveTool == TerrainEditTool.SendVehicle && sendVehicleId >= 0)
            {
                // Valid kinds of target glow: stacks always; mills and other stacks as destinations.
                foreach (var stack in logistics.Stacks) if (stack.Tile != sendSource) o.Candidates.Add(stack.Tile);
                if (sendNeedsDestination && sendSource >= 0) foreach (var mill in logistics.Mills) o.Candidates.AddRange(mill.Footprint);
                int hover = world.HoveredTileId;
                if (hover != hoverCheck.Tile || logistics.Behaviors != null) hoverCheck = (hover, CheckPick(hover));
                o.Hover = hover; o.HoverValid = hover >= 0 && hoverCheck.Reason == null;
                Color colour = sendTruck ? TruckColour : SendMachine?.Kind == ForestMachineKind.Harvester ? ProcessorColour : ForwarderColour;
                if (sendSource >= 0)
                {
                    int[] toSource = CachedRoute(SendVehicleTile(), sendSource, sendTruck && SendTruckUnit?.Vehicle == null ? false : sendTruck);
                    if (toSource != null) o.Routes.Add((toSource, Color.FromArgb(150, 150, 150), false));
                }
                if (hover >= 0)
                {
                    int from = sendSource >= 0 ? sendSource : SendVehicleTile();
                    int[] preview = from >= 0 ? CachedRoute(from, hover, sendTruck && sendSource >= 0) : null;
                    if (preview != null) o.Routes.Add((preview, o.HoverValid ? colour : Color.FromArgb(235, 90, 80), true));
                }
                return;
            }
            if (!showVehicles) return;
            // Orders at work: processors to their stack, forwarders and trucks from source to destination.
            foreach (var m in logistics.Machines)
            {
                if (!m.Working) continue;
                bool strong = hoveredFleetRow == "m" + m.Id;
                if (m.Kind == ForestMachineKind.Harvester && m.Target != null)
                {
                    if (m.Path != null && m.Path.Length > 1) o.Routes.Add((m.Path, ProcessorColour, strong));
                    if (strong) o.Candidates.Add(m.Target.Tile);
                }
                else if (m.Source != null)
                {
                    var route = CachedRoute(m.Source.Tile, m.Destination, false, machine: m);
                    if (route != null) o.Routes.Add((route, ForwarderColour, strong));
                }
            }
            foreach (var t in logistics.Trucks)
            {
                if (t.Source == null) continue;
                var route = CachedRoute(t.Source.Tile, t.Destination, true, unit: t);
                if (route != null) o.Routes.Add((route, TruckColour, hoveredFleetRow == "t" + t.Id));
            }
        }

        /// <summary>Beside the cursor while picking: the step, what is under it, and why a red tile cannot be chosen.</summary>
        private void DrawOrderTooltip()
        {
            if (interaction.ActiveTool != TerrainEditTool.SendVehicle || sendVehicleId < 0 || imgui.WantCaptureMouse) return;
            var logistics = world.Logistics;
            int hover = world.HoveredTileId;
            ImGui.BeginTooltip();
            string who = sendTruck ? $"Rönkszállító #{sendVehicleId}" : SendMachine?.Kind == ForestMachineKind.Harvester ? $"Processzor #{sendVehicleId}" : $"Forwarder #{sendVehicleId}";
            string step = !sendNeedsDestination ? "Fogadó sarang" : sendSource < 0 ? "1/2 · Forrás sarang" : "2/2 · Cél: sarang vagy fűrészmalom";
            ImGui.TextColored(HudTheme.AmberAccent, $"{who} — {step}");
            if (hover >= 0)
            {
                var stack = logistics.StackAt(hover);
                if (stack != null) ImGui.Text($"Sarang #{stack.Id}: {stack.Volume:F1} m³, {stack.Value:N0} eFt");
                else if (logistics.MillAt(hover) != null) ImGui.Text("Fűrészmalom: itt eladja a fát.");
                if (hoverCheck.Tile == hover && hoverCheck.Reason != null) ImGui.TextColored(HudTheme.Bad, hoverCheck.Reason);
                else if (hoverCheck.Tile == hover) ImGui.TextColored(HudTheme.Good, sendNeedsDestination && sendSource >= 0 && stack == null && logistics.MillAt(hover) == null
                    ? "Kattints: itt új sarang kezdődik." : "Kattints a kiválasztáshoz.");
            }
            ImGui.TextDisabled("Jobb gomb vagy Esc: mégse");
            ImGui.EndTooltip();
        }

        /// <summary>Screen position of a world point, or false when it is behind the camera.</summary>
        private bool WorldToScreen(Vector3 point, out NVec2 screen)
        {
            Vector4 clip = new Vector4(point, 1) * (modelView * projection);
            screen = default;
            if (clip.W <= 1e-5f) return false;
            float x = clip.X / clip.W, y = clip.Y / clip.W;
            if (x < -1.2f || x > 1.2f || y < -1.2f || y > 1.2f) return false;
            screen = new NVec2((x * 0.5f + 0.5f) * Width, (0.5f - y * 0.5f) * Height);
            return true;
        }

        /// <summary>A small label over every stack: its number and volume (and value when hovered).</summary>
        private void DrawStackLabels()
        {
            var logistics = world.Logistics;
            if (logistics == null || logistics.Stacks.Count == 0 || captureDirectory != null) return;
            var dl = ImGui.GetBackgroundDrawList();
            foreach (var stack in logistics.Stacks)
            {
                if (!world.TryGetTileCenter(stack.Tile, out var centre) || !WorldToScreen(centre + new Vector3(0, 0, 2.2f), out var at)) continue;
                string text = stack.Volume > 0.05f ? $"#{stack.Id} · {stack.Volume:F0} m³" : $"#{stack.Id} · üres";
                var size = ImGui.CalcTextSize(text);
                var min = at - new NVec2(size.X / 2 + 6, size.Y + 6);
                var max = at + new NVec2(size.X / 2 + 6, 0);
                dl.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(new NVec4(0.12f, 0.1f, 0.07f, 0.82f)), 5f);
                dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new NVec4(0.75f, 0.52f, 0.25f, 0.9f)), 5f);
                dl.AddText(min + new NVec2(6, 3), ImGui.ColorConvertFloat4ToU32(new NVec4(0.96f, 0.9f, 0.78f, 1)), text);
            }
        }
    }
}

using System;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

/// <summary>Orbit, pan, zoom and reset for the last drawn preview surface.</summary>
internal sealed class TreePreviewCamera(TreePreviewUiState state)
{
    private ref float previewYaw => ref state.Yaw;
    private ref float previewPitch => ref state.Pitch;
    private ref float previewZoom => ref state.Zoom;
    private ref Vec2 previewPan => ref state.Pan;
    internal Vec2 PanPerPixel { get; set; }
    internal void HandleInput()
    {
        bool hovered = ImGui.IsItemHovered(), active = ImGui.IsItemActive();
        var io = ImGui.GetIO();
        bool panning = active && (ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 0)
            || (io.KeyShift && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0)));
        if (panning)
        {
            previewPan += new Vec2(-io.MouseDelta.X * PanPerPixel.X, io.MouseDelta.Y * PanPerPixel.Y);
            previewPan = Vec2.Clamp(previewPan, new Vec2(-3), new Vec2(3));
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }
        else if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
        {
            previewYaw -= io.MouseDelta.X * .01f; previewPitch = Math.Clamp(previewPitch + io.MouseDelta.Y * .006f, -.15f, 1.45f);
        }
        // Smooth, proportional zoom so a trackpad and a wheel feel alike.
        if (hovered && io.MouseWheel != 0) previewZoom = Math.Clamp(previewZoom * MathF.Exp(io.MouseWheel * .12f), .3f, 30);
        if (hovered)
        {
            float turn = io.DeltaTime * 1.6f;
            if (ImGui.IsKeyDown(ImGuiKey.LeftArrow)) previewYaw += turn;
            if (ImGui.IsKeyDown(ImGuiKey.RightArrow)) previewYaw -= turn;
            if (ImGui.IsKeyDown(ImGuiKey.UpArrow)) previewPitch = Math.Clamp(previewPitch + turn * .6f, -.15f, 1.45f);
            if (ImGui.IsKeyDown(ImGuiKey.DownArrow)) previewPitch = Math.Clamp(previewPitch - turn * .6f, -.15f, 1.45f);
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { previewYaw = .6f; previewPitch = .28f; previewZoom = 1; previewPan = Vec2.Zero; }
        if (hovered && !active) ImGui.SetTooltip("Bal húzás: forgatás · jobb/középső vagy Shift+bal húzás: mozgatás · görgő: nagyítás · nyilak: forgatás · dupla kattintás: alaphelyzet");
    }

}

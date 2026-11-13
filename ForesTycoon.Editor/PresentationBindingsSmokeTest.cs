using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ForesTycoon.Rules;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal static class PresentationBindingsSmokeTest
{
    internal static void Run()
    {
        using var window = new NativeWindow(new NativeWindowSettings { StartVisible = false, ClientSize = new(1360, 1100),
            API = ContextAPI.OpenGL, APIVersion = new(3, 3), Profile = ContextProfile.Core });
        window.Context.MakeCurrent(); RenderDevice.Initialize();
        string output = Path.GetFullPath("artifacts/presentation-bindings"); Directory.CreateDirectory(output);
        string oldAudioRoot = Environment.GetEnvironmentVariable("FORESTYCOON_AUDIO_DIR");
        Environment.SetEnvironmentVariable("FORESTYCOON_AUDIO_DIR", output);
        try {
            using (var wav = new BinaryWriter(File.Create(Path.Combine(output, "preview.wav")))) {
                void Tag(string value) => wav.Write(System.Text.Encoding.ASCII.GetBytes(value));
                Tag("RIFF"); wav.Write(36 + 9600); Tag("WAVE"); Tag("fmt "); wav.Write(16);
                wav.Write((ushort)1); wav.Write((ushort)1); wav.Write(48000); wav.Write(96000);
                wav.Write((ushort)2); wav.Write((ushort)16); Tag("data"); wav.Write(9600);
                for (int i = 0; i < 4800; i++) wav.Write((short)(200 * Math.Sin(i * 2 * Math.PI * 440 / 48000)));
            }
            var player = new WildlifeAnimationPlayer(PresentationProfile.Default);
            using (var renderer = new AnimationPreviewRenderer(player)) {
                byte[] Frame(double time) {
                    renderer.Render(time, 1);
                    GL.BindTexture(TextureTarget.Texture2D, renderer.Texture);
                    byte[] data = new byte[640 * 400 * 4]; GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
                    return data;
                }
                byte[] before = Frame(.1), after = Frame(.6);
                Require(!before.AsSpan().SequenceEqual(after), "Animation preview did not animate.");
                Require(after.AsSpan().SequenceEqual(Frame(.6)), "Paused animation preview is not repeatable.");
                Require(after.Where((_, i) => i % 4 == 0).Distinct().Count() > 20, "Animation preview is blank.");
            }
            using (var terrain = new Terrain(TerrainSettings.Default.WithNodeSize(9, 42), (_, _) => 0)) {
                var rig = new VehicleKinematics(VehicleGeometryProfiles.Truck(GameTuning.Default), default, 0);
                rig.Advance(2, 0, .37);
                var visual = new RigVisualPose(rig, new(.32f, .32f, .32f, .32f));
                var data = new PresentationBindings();
                byte[] Wheels(bool enabled) {
                    data.ActorAnimations["truck.wheels"] = new() { Enabled = enabled };
                    RenderDevice.SetCamera(OpenTK.Mathematics.Matrix4.CreateRotationZ(-.4f) * OpenTK.Mathematics.Matrix4.CreateRotationX(-.8f)
                        * OpenTK.Mathematics.Matrix4.CreateOrthographic(8, 6.47f, -100, 100));
                    GL.Viewport(0, 0, 1360, 1100); GL.Enable(EnableCap.DepthTest); GL.ClearColor(.1f, .15f, .1f, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    RigRenderer.Wheels(rig, terrain.Map, visual, animationScale: ActorAnimationEvents.Time(new(data), "truck.wheels", 1));
                    GL.Finish(); byte[] pixels = new byte[1360 * 1100 * 4];
                    GL.ReadPixels(0, 0, 1360, 1100, PixelFormat.Rgba, PixelType.UnsignedByte, pixels); return pixels;
                }
                byte[] moving = Wheels(true), stopped = Wheels(false);
                Require(!moving.AsSpan().SequenceEqual(stopped), "Actor animation binding did not change rendered wheels.");
                Require(stopped.AsSpan().SequenceEqual(Wheels(false)), "Disabled actor animation was not stable.");
                Require(GL.GetError() == ErrorCode.NoError, "Actor wheel renderer leaked errors.");
            }
            using var world = new GameWorld(TerrainSettings.Default.WithNodeSize(9, 42), enableRendering: false);
            var document = new EditorDocument(); document.Load(world.RuleProjectDocument);
            var session = new PresentationSession(document);
            var context = new EditorPageContext(new(document.Scene), new(), (_, _) => { },
                (w, _) => { w.QueueRuleProject(document.Capture()); w.ExecutePendingCommands(); },
                action => { action(); return true; }, _ => { }, _ => { }, () => true, document.Capture, document.Revision);
            using var ui = new ImGuiController(ImGuiController.Authoring); EditorStyle.Apply();
            using var view = new PresentationEditorView(session, context);
            var controls = new Dictionary<string, Vec2>(); view.ObserveControl = (key, min, max) => controls[key] = (min + max) / 2;
            void Draw(Action input = null) {
                input?.Invoke(); ui.Update(1360, 1100, 1360, 1100, Vec2.One, 1f / 60);
                ImGui.SetNextWindowPos(new(0, 0)); ImGui.SetNextWindowSize(new(1360, 1100));
                ImGui.Begin("Animációk és hangesemények", ImGuiWindowFlags.NoSavedSettings);
                view.Draw(world); ImGui.End();
                GL.Viewport(0, 0, 1360, 1100); GL.ClearColor(.05f, .07f, .05f, 1);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); ui.Render(); GL.Finish();
            }
            void Click(string key) {
                Vec2 at = controls[key];
                Draw(() => ImGui.GetIO().AddMousePosEvent(at.X, at.Y));
                Draw(() => ImGui.GetIO().AddMouseButtonEvent(0, true));
                Draw(() => ImGui.GetIO().AddMouseButtonEvent(0, false));
            }
            Draw(); Draw();
            Click("deer.walk.enabled");
            Require(!document.Presentation.Animations["deer.walk"].Enabled, "Native checkbox did not edit the document.");
            Require(document.History.Undo() != null && document.Presentation.Animations["deer.walk"].Enabled, "Native edit did not undo.");
            Draw(); Click("deer.walk.clip");
            var selectModifier = ImGui.GetIO().ConfigMacOSXBehaviors ? ImGuiKey.ModSuper : ImGuiKey.ModCtrl;
            Draw(() => { ImGui.GetIO().AddKeyEvent(selectModifier, true); ImGui.GetIO().AddKeyEvent(ImGuiKey.A, true); });
            Draw(() => { ImGui.GetIO().AddKeyEvent(ImGuiKey.A, false); ImGui.GetIO().AddKeyEvent(selectModifier, false); });
            foreach (char c in "Stand_Eating_01") Draw(() => ImGui.GetIO().AddInputCharacter(c));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, true));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, false));
            FramebufferCapture.SavePng(Path.Combine(output, "input-debug.png"), 1360, 1100);
            Require(document.Presentation.Animations["deer.walk"].Clip == "Stand_Eating_01", "Native text edit was lost between frames: " + document.Presentation.Animations["deer.walk"].Clip);
            Click("sound.Wind"); Draw(); Click("sound.Wind.file");
            foreach (char c in "preview.wav") Draw(() => ImGui.GetIO().AddInputCharacter(c));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, true));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, false));
            Require(document.Presentation.Sounds["Wind"].File == "preview.wav", "Native WAV edit did not persist.");
            Click("sound.Wind.audition");
            Require(view.IsAuditioning, "Authored WAV could not be auditioned through OpenAL.");
            FramebufferCapture.SavePng(Path.Combine(output, "editor-sounds.png"), 1360, 1100);
            Click("wildlife.header"); Click("ambience.header"); Click("actors.header");
            Click("actor.truck.wheels");
            Require(!document.Presentation.ActorAnimations["truck.wheels"].Enabled, "Actor checkbox did not persist.");
            Click("actors.header"); Click("spatial.header"); Click("spatial.truck.driving");
            Click("spatial.truck.driving.file");
            foreach (char c in "preview.wav") Draw(() => ImGui.GetIO().AddInputCharacter(c));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, true));
            Draw(() => ImGui.GetIO().AddKeyEvent(ImGuiKey.Enter, false));
            Require(document.Presentation.MachineSounds["truck.driving"].File == "preview.wav", "Spatial WAV edit lost.");
            Click("spatial.truck.driving.audition");
            Require(view.IsSpatialAuditioning, "Spatial audition did not start.");
            view.Poll(); FramebufferCapture.SavePng(Path.Combine(output, "editor-spatial.png"), 1360, 1100);
            Click("spatial.header"); Click("wildlife.header");
            PresentationEditorView.ValidateAssets(session.Read());
            context.ApplyProject(world, false);
            Require(world.Presentation.Sounds["Wind"].File == "preview.wav", "Editor WAV binding did not reach runtime.");
            Require(world.Presentation.Animations["deer.walk"].Clip == "Stand_Eating_01", "Editor binding did not reach runtime.");
            view.LoadPreview(); Draw(); Draw();
            Require(GL.GetError() == ErrorCode.NoError, "Preview leaked OpenGL errors.");
            FramebufferCapture.SavePng(Path.Combine(output, "editor-bindings.png"), 1360, 1100);
            using var save = new MemoryStream(); world.Save(save); save.Position = 0; world.Load(save);
            Require(world.Presentation.Animations["deer.walk"].Clip == "Stand_Eating_01", "Checkpoint lost the binding.");
            Require(world.Presentation.Sounds["Wind"].File == "preview.wav", "Checkpoint lost sound binding.");
            Require(world.Presentation.MachineSounds["truck.driving"].File == "preview.wav" && !world.Presentation.ActorAnimations["truck.wheels"].Enabled, "Checkpoint lost actor/spatial binding.");
            Console.WriteLine("Presentation bindings smoke passed: spatial UI/audition and actor wheel rendering, authored WAV and native audition, native checkbox/text, Undo, apply, checkpoint, animated preview and repeatable pause.");
        } finally { Environment.SetEnvironmentVariable("FORESTYCOON_AUDIO_DIR", oldAudioRoot); RenderDevice.Dispose(); }
    }
    private static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
}

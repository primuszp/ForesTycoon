using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ForesTycoon.Audio;
using ForesTycoon.Models;
using ForesTycoon.Rules;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace ForesTycoon.Editor;

internal sealed partial class PresentationEditorView(PresentationSession session, EditorPageContext context) : IDisposable
{
    internal Action<string, Vec2, Vec2> ObserveControl { get; set; }
    private void Observe(string id) => ObserveControl?.Invoke(id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    private PresentationProfile snapshot;
    private string revision, modelPath, rootName;
    private string[] clips = Array.Empty<string>();
    private AnimationPreviewRenderer preview;
    private AmbientSoundscape audition;
    internal bool IsAuditioning => audition != null;
    private long auditionEnd;
    private readonly Dictionary<string, string> clipDrafts = new(), soundDrafts = new();
    private bool playing = true;
    private float time, blend;
    private string error;
    private static string AudioRoot => AudioContent.Root;

    internal void Poll()
    {
        PollSpatial();
        if (audition != null && Stopwatch.GetTimestamp() >= auditionEnd) StopAudio();
    }
    private void StopAudio() { audition?.Dispose(); audition = null; spatialAudition?.Dispose(); spatialAudition = null; }
    private void Edit(Action<PresentationBindings> change)
    {
        context.Try(() => session.Edit(change)); revision = null;
    }
    internal void Draw(GameWorld world)
    {
        string current = context.Revision();
        if (current != revision) {
            snapshot = session.Read(); revision = current; modelPath = snapshot.WildlifeModel; rootName = snapshot.MotionRoot;
            foreach (var p in snapshot.Animations) clipDrafts[p.Key] = p.Value.Clip;
            foreach (var p in snapshot.MachineSounds) machineDrafts[p.Key] = p.Value.File;
            foreach (var p in snapshot.Sounds) soundDrafts[p.Key] = p.Value.File;
        }
        ImGui.TextWrapped("Az események a játék állapotát követik. A kötések a projektben és a játékmentésben is megmaradnak; az előnézet nem lépteti a szimulációt.");
        if (ImGui.Button("Kötések alkalmazása a tesztvilágban")) context.Try(() => context.ApplyProject(world, false));
        bool wildlifeOpen = ImGui.CollapsingHeader("Szarvasanimációk", ImGuiTreeNodeFlags.DefaultOpen); Observe("wildlife.header");
        if (wildlifeOpen) {
            ImGui.InputText("GLB-modell", ref modelPath, 1024);
            ImGui.InputText("Mozgási gyökér (üres: nincs)", ref rootName, 256);
            if (ImGui.Button("Modellkötés rögzítése")) Edit(p => { p.WildlifeModel = modelPath; p.MotionRoot = rootName; });
            ImGui.SameLine();
            if (ImGui.Button("Klipek és előnézet betöltése")) LoadPreview();
            foreach (var (id, value) in snapshot.Animations) {
                ImGui.PushID(id); ImGui.SeparatorText(id == "deer.idle" ? "Nyugalom / legelés" : "Járás");
                bool enabled = value.Enabled;
                if (ImGui.Checkbox("Aktív", ref enabled)) Edit(p => p.Animations[id] = value with { Enabled = enabled });
                Observe(id + ".enabled");
                string clip = clipDrafts[id];
                bool commitClip = ImGui.InputText("Klip neve (Enter: rögzítés)", ref clip, 256, ImGuiInputTextFlags.EnterReturnsTrue);
                Observe(id + ".clip");
                clipDrafts[id] = clip;
                if (commitClip) Edit(p => p.Animations[id] = value with { Clip = clip });
                if (ImGui.BeginCombo("Modell klipjei", value.Clip)) {
                    foreach (string name in clips) if (ImGui.Selectable(name, name == value.Clip)) Edit(p => p.Animations[id] = value with { Clip = name });
                    ImGui.EndCombo();
                }
                float speed = (float)value.Speed;
                if (ImGui.SliderFloat("Lejátszási sebesség", ref speed, .01f, 20)) Edit(p => p.Animations[id] = value with { Speed = speed });
                ImGui.PopID();
            }
            ImGui.TextDisabled("Módosítás után töltsd újra az előnézetet. A játék az Alkalmazás után veszi át.");
            if (preview != null) {
                ImGui.Checkbox("Lejátszás", ref playing); ImGui.SameLine();
                if (ImGui.Button("Elejére")) time = 0;
                ImGui.SliderFloat("Idő (s)", ref time, 0, 60);
                ImGui.SliderFloat("Nyugalom → járás", ref blend, 0, 1);
                if (playing) time += Math.Min(ImGui.GetIO().DeltaTime, .1f);
                try {
                    preview.Render(time, blend);
                    float width = Math.Min(640, ImGui.GetContentRegionAvail().X);
                    ImGui.Image((IntPtr)preview.Texture, new Vec2(width, width * 400 / 640), new Vec2(0, 1), new Vec2(1, 0));
                } catch (Exception e) { error = e.Message; preview.Dispose(); preview = null; }
            }
        }
        bool ambienceOpen = ImGui.CollapsingHeader("Hangfájlok és hangesemények", ImGuiTreeNodeFlags.DefaultOpen); Observe("ambience.header");
        if (ambienceOpen) {
            ImGui.TextWrapped("WAV-útvonalak a hangcsomagon belül. Üres útvonal: a telepített manifest kötése. Letiltás: az esemény nem szólal meg.");
            ImGui.TextDisabled(AudioRoot);
            foreach (var (id, value) in snapshot.Sounds) {
                ImGui.PushID(id);
                bool expanded = ImGui.TreeNode(id);
                Observe("sound." + id);
                if (expanded) {
                    bool enabled = value.Enabled;
                    if (ImGui.Checkbox("Aktív", ref enabled)) Edit(p => p.Sounds[id] = value with { Enabled = enabled });
                    string file = soundDrafts[id];
                    bool commitFile = ImGui.InputText("WAV-fájl (Enter: rögzítés)", ref file, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
                    Observe("sound." + id + ".file");
                    soundDrafts[id] = file;
                    if (commitFile) Edit(p => p.Sounds[id] = value with { File = file });
                    float gain = (float)value.Gain;
                    if (ImGui.SliderFloat("Esemény hangereje", ref gain, 0, 1)) Edit(p => p.Sounds[id] = value with { Gain = gain });
                    if (id is "Thunder" or "Deer") {
                        float delay = (float)value.MinimumDelay, range = (float)value.DelayRange;
                        if (ImGui.SliderFloat("Legalább ennyi idő (valós s)", ref delay, .1f, 600)) Edit(p => p.Sounds[id] = value with { MinimumDelay = delay });
                        if (ImGui.SliderFloat("Véletlen többletidő (s)", ref range, 0, 600)) Edit(p => p.Sounds[id] = value with { DelayRange = range });
                    }
                    if (ImGui.Button("Előhallgatás (5 s)")) Audition(id);
                    Observe("sound." + id + ".audition");
                    ImGui.SameLine(); if (ImGui.Button("Leállítás")) StopAudio();
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
        DrawActors();
        if (error != null) ImGui.TextWrapped("Előnézet: " + error);
    }
    internal void LoadPreview()
    {
        try {
            snapshot = session.Read();
            // Read the clip list even when the current authored clip is invalid, so it can be repaired.
            var asset = AnimatedGlbModel.Load(Path.Combine(AppContext.BaseDirectory, snapshot.WildlifeModel));
            clips = asset.Clips.Keys.Order(StringComparer.Ordinal).ToArray();
            var player = new WildlifeAnimationPlayer(snapshot, asset);
            var next = new AnimationPreviewRenderer(player);
            preview?.Dispose(); preview = next; time = 0; error = null;
        } catch (Exception e) { error = e.Message; }
    }
    private void Audition(string id)
    {
        StopAudio(); error = null;
        var data = session.Read().Capture();
        foreach (string key in data.Sounds.Keys.ToArray()) if (key != id) data.Sounds[key] = data.Sounds[key] with { Enabled = false };
        audition = OpenAlAudioOutput.TryCreate(AudioRoot, text => error = text, new(data));
        if (audition == null) { error ??= "Ehhez az eseményhez nincs betölthető hangfájl vagy hangeszköz."; return; }
        audition.Audition(Enum.Parse<AmbientCue>(id)); auditionEnd = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
    }
    internal static void ValidateAssets(PresentationProfile profile)
    {
        _ = new WildlifeAnimationPlayer(profile);
        foreach (var binding in profile.MachineSounds.Values.Where(b => b.Enabled && b.File.Length > 0)) {
            _ = AudioContent.Read(AudioRoot, binding.File);
        }
        foreach (var binding in profile.Sounds.Values.Where(b => b.Enabled && b.File.Length > 0)) {
            _ = AudioContent.Read(AudioRoot, binding.File);
        }
    }
    public void Dispose() { StopAudio(); preview?.Dispose(); preview = null; }
}

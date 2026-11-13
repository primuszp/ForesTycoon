using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ForesTycoon.Audio;
using ForesTycoon.Rules;
using ImGuiNET;
using OpenTK.Mathematics;

namespace ForesTycoon.Editor;

internal sealed partial class PresentationEditorView
{
    private readonly Dictionary<string, string> machineDrafts = new();
    private SpatialMachineAudio spatialAudition;
    private readonly List<ActorSoundSample> auditionEmitters = new(1);
    private string auditionEvent;
    private float auditionDistance = 10, auditionAngle;
    internal bool IsSpatialAuditioning => spatialAudition != null;
    private void PollSpatial()
    {
        if (spatialAudition == null) return;
        if (Stopwatch.GetTimestamp() >= auditionEnd) { StopAudio(); return; }
        auditionEmitters.Clear();
        auditionEmitters.Add(new(1, auditionEvent, new Vector3(MathF.Sin(auditionAngle), 0, -MathF.Cos(auditionAngle)) * auditionDistance * Engine.WorldScale.MetresToWorld));
        try { spatialAudition.Update(auditionEmitters, Vector3.Zero, Matrix4.Identity, .7f, true); }
        catch (InvalidOperationException e) { error = e.Message; StopAudio(); }
    }
    private void AuditionSpatial(string id)
    {
        StopAudio(); error = null;
        var data = session.Read().Capture();
        foreach (string key in new List<string>(data.MachineSounds.Keys)) if (key != id) data.MachineSounds[key] = data.MachineSounds[key] with { Enabled = false };
        spatialAudition = OpenAlAudioOutput.TryCreateSpatial(AudioRoot, text => error = text, new(data));
        if (spatialAudition == null) { error ??= "Nincs betölthető géphang vagy hangeszköz."; return; }
        auditionEvent = id; auditionEnd = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency; PollSpatial();
    }
    private void DrawActors()
    {
        bool actorsOpen = ImGui.CollapsingHeader("Gépek animációeseményei"); Observe("actors.header");
        if (actorsOpen) {
            ImGui.TextWrapped("A kerék a megtett utat, a munkagép a munkafázist követi. A forwarder rakodásának sebessége rögzített, hogy a faanyagátadással együtt maradjon. Letiltáskor a daru alaphelyzetben marad; a szimuláció nem változik.");
            foreach (var (id, b) in snapshot.ActorAnimations) {
                ImGui.PushID(id); bool enabled = b.Enabled;
                if (ImGui.Checkbox(id, ref enabled)) Edit(p => p.ActorAnimations[id] = b with { Enabled = enabled });
                Observe("actor." + id);
                if (!ActorBindingDefaults.Synchronized(id)) {
                    float speed = (float)b.Speed;
                    if (ImGui.SliderFloat("Animációsebesség", ref speed, .01f, 4)) Edit(p => p.ActorAnimations[id] = b with { Speed = speed });
                }
                ImGui.PopID();
            }
        }
        bool spatialOpen = ImGui.CollapsingHeader("Térbeli géphangok"); Observe("spatial.header");
        if (spatialOpen) {
            ImGui.TextWrapped("Állapothoz kötött, ismétlődő WAV-hangok. Üres fájl: néma esemény. A sztereó felvételt mono jellé keverjük a térbeli elhelyezéshez. A távolság méterben értendő.");
            int limit = snapshot.SpatialVoiceLimit;
            if (ImGui.SliderInt("Egyidejű géphangok", ref limit, 1, 32)) Edit(p => p.SpatialVoiceLimit = limit);
            ImGui.SliderFloat("Próbatávolság (m)", ref auditionDistance, .1f, 300);
            ImGui.SliderFloat("Próba iránya (radián)", ref auditionAngle, -MathF.PI, MathF.PI);
            foreach (var (id, b) in snapshot.MachineSounds) {
                ImGui.PushID("spatial." + id);
                bool open = ImGui.TreeNode(id); Observe("spatial." + id);
                if (open) {
                    bool enabled = b.Enabled;
                    if (ImGui.Checkbox("Aktív", ref enabled)) Edit(p => p.MachineSounds[id] = b with { Enabled = enabled });
                    string file = machineDrafts.TryGetValue(id, out var draft) ? draft : b.File;
                    bool commit = ImGui.InputText("WAV-fájl (Enter: rögzítés)", ref file, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
                    machineDrafts[id] = file; Observe("spatial." + id + ".file");
                    if (commit) Edit(p => p.MachineSounds[id] = b with { File = file });
                    float gain = (float)b.Gain, reference = (float)b.ReferenceDistance, maximum = (float)b.MaxDistance;
                    if (ImGui.SliderFloat("Hangerő", ref gain, 0, 1)) Edit(p => p.MachineSounds[id] = b with { Gain = gain });
                    if (ImGui.SliderFloat("Referencia-távolság (m)", ref reference, .1f, Math.Min(1000, maximum - .1f))) Edit(p => p.MachineSounds[id] = b with { ReferenceDistance = reference });
                    if (ImGui.SliderFloat("Hallhatósági határ (m)", ref maximum, reference + .1f, 5000)) Edit(p => p.MachineSounds[id] = b with { MaxDistance = maximum });
                    if (ImGui.Button("Térhangpróba (5 s)")) AuditionSpatial(id);
                    Observe("spatial." + id + ".audition");
                    ImGui.SameLine(); if (ImGui.Button("Leállítás")) StopAudio();
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
    }
}

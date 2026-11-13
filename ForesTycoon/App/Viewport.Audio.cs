using System;
using ForesTycoon.Audio;
using ImGuiNET;

namespace ForesTycoon;

sealed partial class Viewport
{
    private AmbientSoundscape soundscape;
    private SpatialMachineAudio machineAudio;
    private string machineAudioRevision;
    private bool audioAttempted;
    private string audioProfileRevision;
    private float audioMaster = .7f, audioAmbience = .7f, audioWildlife = .6f;
    private bool audioMuted;

    private void UpdateAudio(double realSeconds, bool ready = true)
    {
        if (!ready && soundscape == null && machineAudio == null) return;
        var profile = world?.Presentation ?? ForesTycoon.Rules.PresentationProfile.Default;
        if (!audioAttempted || audioProfileRevision != profile.SoundRevision) {
            soundscape?.Dispose(); soundscape = null;
            audioProfileRevision = profile.SoundRevision;
            audioAttempted = true;
            soundscape = OpenAlAudioOutput.TryCreate(AudioContent.Root, Console.Error.WriteLine, profile);
        }
        // The viewport owns player preferences; outputs receive them and never own a second UI state.
        if (soundscape != null) {
            soundscape.MasterVolume = audioMaster; soundscape.AmbienceVolume = audioAmbience;
            soundscape.WildlifeVolume = audioWildlife; soundscape.Muted = audioMuted;
        }
        if (machineAudioRevision != profile.SpatialRevision) {
            machineAudio?.Dispose(); machineAudio = null; machineAudioRevision = profile.SpatialRevision;
            machineAudio = OpenAlAudioOutput.TryCreateSpatial(AudioContent.Root, Console.Error.WriteLine, profile);
        }
        if (machineAudio != null) {
            var orientation = camera.CreateViewMatrix();
            var inverse = orientation.Inverted();
            var centre = new OpenTK.Mathematics.Vector3((float)screenX + (Width - 1) / (2 * zoom), (float)screenY + (Height - 1) / (2 * zoom), 0);
            var origin = OpenTK.Mathematics.Vector3.TransformPosition(centre, inverse);
            var direction = OpenTK.Mathematics.Vector3.TransformVector(OpenTK.Mathematics.Vector3.UnitZ, inverse);
            var listener = origin - direction * (origin.Z / direction.Z);
            if (world != null && world.Map.TryGetDrivingSurfaceZ(listener.X, listener.Y, out float height))
                listener = origin + direction * ((height - origin.Z) / direction.Z);
            try { machineAudio.Update(world?.MachineSoundSnapshot ?? Array.Empty<ActorSoundSample>(), listener, orientation, audioMaster * audioAmbience,
                ready && IsFocused && !audioMuted && !simulationClock.IsPaused); }
            catch (InvalidOperationException e) { Console.Error.WriteLine(e.Message); machineAudio.Dispose(); machineAudio = null; }
        }
        var environment = world?.Environment;
        if (soundscape == null) return;
        var state = !ready || environment == null ? default : new AmbientState(environment.WindSpeed, environment.LiquidRainRate,
            environment.Preset == WeatherPreset.Storm, environment.Time / environment.ForestYearSeconds,
            world.Wildlife.Animals.Exists(a => a is WildlifeSystem.Deer && !a.Removed), simulationClock.IsPaused);
        try { soundscape.Update(state, realSeconds, ready && environment != null && IsFocused, world?.Tuning); }
        catch (InvalidOperationException e) {
            Console.Error.WriteLine($"Ambient audio stopped: {e.Message}");
            soundscape.Dispose(); soundscape = null;
        }
    }

    private void DrawAudioControls()
    {
        if (!ImGui.CollapsingHeader("Környezeti hangok")) return;
        if (soundscape == null && machineAudio == null) { ImGui.TextDisabled("Nincs telepített hangcsomag vagy hangeszköz."); return; }
        ImGui.Checkbox("Némítás", ref audioMuted);
        ImGui.SliderFloat("Fő hangerő", ref audioMaster, 0, 1, "%.2f");
        ImGui.SliderFloat("Környezet és gépek hangereje", ref audioAmbience, 0, 1, "%.2f");
        ImGui.SliderFloat("Vadállatok hangereje", ref audioWildlife, 0, 1, "%.2f");
    }
}

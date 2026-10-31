using System;
using System.IO;
using ForesTycoon.Audio;
using ImGuiNET;

namespace ForesTycoon;

sealed partial class Viewport
{
    private AmbientSoundscape soundscape;
    private bool audioAttempted;

    private void UpdateAudio(double realSeconds, bool ready = true)
    {
        if (!ready && soundscape == null) return;
        if (!audioAttempted) {
            audioAttempted = true;
            string root = Environment.GetEnvironmentVariable("FORESTYCOON_AUDIO_DIR");
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(AppContext.BaseDirectory, "Assets", "Licensed", "Audio");
            soundscape = OpenAlAmbientOutput.TryCreate(root, Console.Error.WriteLine);
        }
        var environment = world?.Environment;
        if (soundscape == null) return;
        var state = !ready || environment == null ? default : new AmbientState(environment.WindSpeed, environment.LiquidRainRate,
            environment.Preset == WeatherPreset.Storm, environment.Time / environment.ForestYearSeconds,
            world.Wildlife.Animals.Exists(a => a is WildlifeSystem.Deer && !a.Removed), simulationClock.IsPaused);
        try { soundscape.Update(state, realSeconds, ready && environment != null && IsFocused); }
        catch (InvalidOperationException e) {
            Console.Error.WriteLine($"Ambient audio stopped: {e.Message}");
            soundscape.Dispose(); soundscape = null;
        }
    }

    private void DrawAudioControls()
    {
        if (!ImGui.CollapsingHeader("Környezeti hangok")) return;
        if (soundscape == null) { ImGui.TextDisabled("Nincs telepített hangcsomag vagy hangeszköz."); return; }
        ImGui.Checkbox("Némítás", ref soundscape.Muted);
        ImGui.SliderFloat("Fő hangerő", ref soundscape.MasterVolume, 0, 1, "%.2f");
        ImGui.SliderFloat("Környezet hangereje", ref soundscape.AmbienceVolume, 0, 1, "%.2f");
        ImGui.SliderFloat("Vadállatok hangereje", ref soundscape.WildlifeVolume, 0, 1, "%.2f");
    }
}

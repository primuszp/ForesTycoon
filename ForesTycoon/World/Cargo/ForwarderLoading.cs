using System;
using OpenTK.Mathematics;

namespace ForesTycoon
{
    // One physical log_001: five metres long, forty centimetres in diameter.
    internal static class ForwarderLoading
    {
        internal const float LogVolume = MathF.PI * .2f * .2f * 5;
        internal const double Grip = .32, Release = .77;
        internal static int Count(float volume) => volume <= .0001f ? 0 : (int)Math.Ceiling((volume - .00001f) / LogVolume);
        internal static Vector3 Slot(int index, bool cargo)
        {
            index = Math.Max(0, index);
            int pile = cargo ? 0 : index / 21;
            if (!cargo) index %= 21;
            int layer = 0, width = cargo ? 5 : 6;
            while (index >= width) { index -= width; layer++; width = cargo ? (layer % 2 == 0 ? 5 : 4) : Math.Max(1, 6 - layer); }
            return new Vector3(cargo ? -5.9f : pile * 5.4f, -.06f + (index - (width - 1) * .5f) * .4f,
                (cargo ? 2.2f : .2f) + layer * .2f * MathF.Sqrt(3));
        }
        internal static double Duration(ForestMachine m) => m.LogCycleDuration > 0 ? m.LogCycleDuration :
            LogVolume / m.Tuning[m.State == ForestMachineState.Unloading ? Tune.ForwarderUnloadRate : Tune.ForwarderLoadRate];
        internal static double Phase(ForestMachine m) => Math.Clamp(m.WorkTime / Duration(m), 0, 1);
        private static double Rule(ForestMachine m, string hook, double native, double dt = 0, bool departure = false) =>
            m.Behaviors?.Evaluate(new(hook, "forwarder", (ulong)m.Id, native, dt,
                State: departure ? m.CargoFill : m.State == ForestMachineState.Unloading ? 1 : 0, Amount: m.Cargo,
                Capacity: m.Capacity, Available: m.State == ForestMachineState.Unloading ? m.Cargo : m.Source?.Volume ?? 0)) ?? native;
        internal static float AnimationPhase(ForestMachine m)
        {
            double phase = Phase(m), grip = m.LogGripPhase, release = m.LogReleasePhase;
            return (float)(phase <= grip ? phase / grip * Grip : phase <= release ? Grip + (phase - grip) / (release - grip) * (Release - Grip)
                : Release + (phase - release) / (1 - release) * (1 - Release));
        }

        // Reserve at grasp, deliver at release; volume and value in the grapple are part of the save state.
        internal static bool Step(ForestMachine m, double dt, Action<float, double> receive)
        {
            bool unloading = m.State == ForestMachineState.Unloading;
            if (!unloading && m.Control == BehaviorAction.DepartLoaded && m.Cargo > .0001f && m.WorkTime == 0 && m.LogTransferVolume == 0) return true;
            if (m.LogCycleDuration == 0) {
                m.LogCycleDuration = Rule(m, "loading.cycleSeconds", Duration(m)); m.WorkTime = 0;
                m.LogGripPhase = Rule(m, "loading.gripPhase", Grip);
                m.LogReleasePhase = Math.Max(m.LogGripPhase + .01, Rule(m, "loading.releasePhase", Release));
            }
            if (m.WorkTime == 0 && (unloading ? m.Cargo <= .0001f : m.Source.Volume <= .0001f || m.Cargo >= m.Capacity - .0001f)) return true;
            double before = Phase(m);
            m.WorkTime = Math.Min(m.LogCycleDuration, m.WorkTime + dt);
            if (m.LogCycleDuration - m.WorkTime < .000001) m.WorkTime = m.LogCycleDuration;
            double after = Phase(m);
            if (before < m.LogGripPhase && after >= m.LogGripPhase)
            {
                float amount = unloading ? Math.Min(LogVolume, m.Cargo - Math.Max(0, (Count(m.Cargo)-1)*LogVolume)) :
                    Math.Min(LogVolume, m.Capacity - m.Cargo);
                amount = Math.Min(amount, (float)Rule(m, unloading ? "unloading.amount" : "loading.amount", amount, dt));
                if (unloading) {
                    m.LogTransferVolume = amount; m.LogTransferValue = m.Cargo > 0 ? m.CargoValue * amount / m.Cargo : 0;
                    m.Cargo -= amount; m.CargoValue -= m.LogTransferValue;
                } else (m.LogTransferVolume, m.LogTransferValue) = m.Source.Take(amount);
            }
            if (before < m.LogReleasePhase && after >= m.LogReleasePhase)
            {
                if (unloading) receive(m.LogTransferVolume, m.LogTransferValue);
                else { m.Cargo += m.LogTransferVolume; m.CargoValue += m.LogTransferValue; }
                m.LogTransferVolume = 0; m.LogTransferValue = 0;
            }
            if (after < 1) return false;
            m.WorkTime = 0; m.LogCycleDuration = 0;
            if (unloading) return m.Cargo <= .0001f;
            bool finished = m.HomeRequested || m.Cargo >= m.Capacity - .0001f || m.Source.Volume <= .0001f;
            // Forced completion and stock/capacity constraints cannot strand a held log or suppress a home request.
            return finished || m.Cargo > .0001f && (m.Control == BehaviorAction.DepartLoaded || Rule(m, "loading.depart", 0, dt, true) >= .5);
        }
        internal static Vector3 Target(Vector3 pick, Vector3 drop, float phase, out float jaw)
        {
            Vector3 home = new(-4, 0, 4.45f);
            Vector3 highPick = new(pick.X, pick.Y, Math.Max(4.45f, pick.Z + 1)), highDrop = new(drop.X, drop.Y, Math.Max(4.45f, drop.Z + 1));
            float[] times = { 0, .15f, .27f, .32f, .44f, .58f, .71f, .77f, .89f, 1 };
            Vector3[] points = { home, highPick, pick, pick, highPick, highDrop, drop, drop, highDrop, home };
            float[] jaws = { 35, 35, 35, 4, 4, 4, 4, 35, 35, 35 };
            int i = 0; while (i < times.Length-2 && phase > times[i+1]) i++;
            float t = Math.Clamp((phase-times[i])/(times[i+1]-times[i]),0,1); t=t*t*(3-2*t);
            jaw = MathHelper.DegreesToRadians(jaws[i]+(jaws[i+1]-jaws[i])*t);
            return Vector3.Lerp(points[i], points[i+1], t);
        }
    }
}

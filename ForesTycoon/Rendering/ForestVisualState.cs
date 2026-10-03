using System;

namespace ForesTycoon
{
    // Quantization belongs to rendering: simulation and save data retain full precision.
    internal readonly record struct ForestVisualState(
        ForestSpecies Species, byte Maturity, byte Stocking, byte Health, byte Crowding)
    {
        internal static ForestVisualState From(ForestStand stand, float crowding)
        {
            if (stand.IsEmpty) return default;
            return new ForestVisualState(stand.Species, Quantize(stand.Maturity),
                Quantize(stand.Biomass / ForestSpeciesProfile.For(stand.Species).MaximumBiomass),
                Quantize(stand.Health), Quantize(crowding));
        }

        internal ForestStand Stand => Species == ForestSpecies.None ? default : new ForestStand(
            Species, Maturity / 32f * ForestSpeciesProfile.For(Species).MatureAgeYears,
            Stocking / 32f * ForestSpeciesProfile.For(Species).MaximumBiomass, Health / 32f);
        internal float CanopyPressure => Crowding / 32f;
        private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 32f), 0, 32);
    }

    internal enum ForestLod { Far, Medium, Near }

    internal static class ForestLodPolicy
    {
        internal static (ForestLod Low,ForestLod High,float Blend) Transition(float pixels)
        {
            if (!float.IsFinite(pixels)||pixels<=0)throw new ArgumentOutOfRangeException(nameof(pixels));
            if(pixels<=2.5f)return(ForestLod.Far,ForestLod.Far,0);
            if(pixels<4.5f)return(ForestLod.Far,ForestLod.Medium,Smooth((pixels-2.5f)/2));
            if(pixels<=7)return(ForestLod.Medium,ForestLod.Medium,0);
            if(pixels<11)return(ForestLod.Medium,ForestLod.Near,Smooth((pixels-7)/4));
            return(ForestLod.Near,ForestLod.Near,0);
        }
        private static float Smooth(float value)=>value*value*(3-2*value);

        internal static ForestLod Select(float pixelsPerWorldUnit, ForestLod? previous)
        {
            if (!float.IsFinite(pixelsPerWorldUnit) || pixelsPerWorldUnit <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerWorldUnit));
            // Different enter/leave thresholds prevent repeated rebuilds near a boundary.
            if (pixelsPerWorldUnit >= (previous == ForestLod.Near ? 7f : 9f)) return ForestLod.Near;
            if (pixelsPerWorldUnit >= (previous == ForestLod.Far || previous == null ? 3.5f : 2.5f))
                return ForestLod.Medium;
            return ForestLod.Far;
        }
    }

    internal sealed class ForestChunkVisualState
    {
        private ForestSystem source;
        private ulong revision;
        private bool initialized;
        internal ForestVisualState[] Tiles { get; }

        internal ForestChunkVisualState(int tileCount) => Tiles = new ForestVisualState[tileCount];

        internal bool Refresh(ForestSystem forest, int[] tileIds)
        {
            if (tileIds.Length != Tiles.Length) throw new ArgumentException("Chunk size changed.", nameof(tileIds));
            if (initialized && ReferenceEquals(source, forest) && revision == forest.Revision) return false;
            bool changed = !initialized;
            for (int i = 0; i < tileIds.Length; i++)
            {
                forest.TryGetStand(tileIds[i], out ForestStand stand);
                var visual = ForestVisualState.From(stand, stand.IsEmpty ? 0 : forest.GetCrowding(tileIds[i]));
                changed |= visual != Tiles[i];
                Tiles[i] = visual;
            }
            source = forest;
            revision = forest.Revision;
            initialized = true;
            return changed;
        }
    }
}

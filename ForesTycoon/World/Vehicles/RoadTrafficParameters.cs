namespace ForesTycoon
{
    /// <summary>Shared native inputs to the editable traffic equation; the graph controls their combination.</summary>
    internal static class RoadTrafficParameters
    {
        internal const float ReferenceWear = 0.0015f;
        internal const float ReferenceMass = 36000f;
        internal const float AsphaltFactor = 0.2f;
        internal const float MacadamFactor = 1f;
        internal const float TrailLoadMultiplier = 20f;
        internal static float Load(float grossMass, GameTuning t = null)
        { t ??= GameTuning.Default; return t.F(Tune.ReferenceWear) * grossMass / t.F(Tune.ReferenceMass); }
        internal static float SurfaceFactor(RoadPaving surface, GameTuning t = null) =>
            (t ?? GameTuning.Default).F(surface == RoadPaving.Macadam ? Tune.MacadamFactor : Tune.AsphaltFactor);
    }
}

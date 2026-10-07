namespace ForesTycoon.Map
{
    // Csempe-alapú úthálózat: a kapcsolatok a szomszédos út-csempékből adódnak.
    internal enum RoadPlacementKind
    {
        Invalid,
        NaturalSurface,
        FoundationSurface
    }

    internal readonly struct RoadPlacement
    {
        public readonly RoadPlacementKind Kind;
        public readonly int W;
        public readonly int S;
        public readonly int E;
        public readonly int N;

        public RoadPlacement(RoadPlacementKind kind, int w, int s, int e, int n)
        {
            Kind = kind;
            W = w;
            S = s;
            E = e;
            N = n;
        }

        public bool IsValid => Kind != RoadPlacementKind.Invalid;
        public static readonly RoadPlacement Invalid = new RoadPlacement(RoadPlacementKind.Invalid, 0, 0, 0, 0);
    }


}

using System;

namespace ForesTycoon.Map
{
    enum TileShapeKind
    {
        Flat,
        OneHigh,
        Ramp,
        Saddle,
        ThreeHigh,
        Steep
    }

    readonly struct TileShapeInfo
    {
        public readonly TileShapeKind Kind;
        public readonly int W;
        public readonly int S;
        public readonly int E;
        public readonly int N;
        public readonly int Min;
        public readonly int Max;
        public readonly bool WRaised;
        public readonly bool SRaised;
        public readonly bool ERaised;
        public readonly bool NRaised;

        private TileShapeInfo(TileShapeKind kind, int w, int s, int e, int n, int min, int max)
        {
            Kind = kind;
            W = w;
            S = s;
            E = e;
            N = n;
            Min = min;
            Max = max;
            WRaised = w > min;
            SRaised = s > min;
            ERaised = e > min;
            NRaised = n > min;
        }

        public bool IsFlat => Kind == TileShapeKind.Flat;
        public bool IsRamp => Kind == TileShapeKind.Ramp;
        public bool IsPlanar => W + E == S + N;

        public string RelativeCodeNESW =>
            (N - Min).ToString() + (E - Min).ToString() + (S - Min).ToString() + (W - Min).ToString();

        public static TileShapeInfo FromCorners(int w, int s, int e, int n)
        {
            int min = Math.Min(Math.Min(w, s), Math.Min(e, n));
            int max = Math.Max(Math.Max(w, s), Math.Max(e, n));

            if (max - min > 1)
                return new TileShapeInfo(TileShapeKind.Steep, w, s, e, n, min, max);

            bool wr = w > min;
            bool sr = s > min;
            bool er = e > min;
            bool nr = n > min;
            int raised = (wr ? 1 : 0) + (sr ? 1 : 0) + (er ? 1 : 0) + (nr ? 1 : 0);

            TileShapeKind kind;
            if (raised == 0) kind = TileShapeKind.Flat;
            else if (raised == 1) kind = TileShapeKind.OneHigh;
            else if (raised == 3) kind = TileShapeKind.ThreeHigh;
            else if ((wr && er) || (sr && nr)) kind = TileShapeKind.Saddle;
            else kind = TileShapeKind.Ramp;

            return new TileShapeInfo(kind, w, s, e, n, min, max);
        }
    }
}

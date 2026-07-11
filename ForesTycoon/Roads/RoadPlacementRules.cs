using System;

namespace ForesTycoon
{
    enum LockedRoadSurfaceResult
    {
        Invalid,
        NaturalSurface,
        FoundationSurface
    }

    static class RoadPlacementRules
    {
        public static bool IsSimple(RoadEdge edges)
        {
            int count = 0;
            int value = (int)edges;
            while (value != 0) { count += value & 1; value >>= 1; }
            if (count <= 1) return true;
            return edges == (RoadEdge.WS | RoadEdge.EN) || edges == (RoadEdge.SE | RoadEdge.NW);
        }

        public static bool IsRampAligned(TileShapeInfo shape, RoadEdge edges)
        {
            bool hasWsEn = (edges & (RoadEdge.WS | RoadEdge.EN)) != 0;
            bool hasSeNw = (edges & (RoadEdge.SE | RoadEdge.NW)) != 0;
            if (hasWsEn && !hasSeNw)
                return shape.WRaised == shape.SRaised && shape.ERaised == shape.NRaised;
            if (hasSeNw && !hasWsEn)
                return shape.SRaised == shape.ERaised && shape.NRaised == shape.WRaised;
            return false;
        }

        public static LockedRoadSurfaceResult ValidateLockedSurface(RoadEdge edges,
            int terrainW, int terrainS, int terrainE, int terrainN,
            int surfaceW, int surfaceS, int surfaceE, int surfaceN)
        {
            if (surfaceW < terrainW || surfaceS < terrainS || surfaceE < terrainE || surfaceN < terrainN)
                return LockedRoadSurfaceResult.Invalid;
            if (surfaceW - terrainW > 1 || surfaceS - terrainS > 1 || surfaceE - terrainE > 1 || surfaceN - terrainN > 1)
                return LockedRoadSurfaceResult.Invalid;

            TileShapeInfo surface = TileShapeInfo.FromCorners(surfaceW, surfaceS, surfaceE, surfaceN);
            if (!surface.IsPlanar || (!surface.IsFlat && !IsSimple(edges)))
                return LockedRoadSurfaceResult.Invalid;

            return surfaceW == terrainW && surfaceS == terrainS && surfaceE == terrainE && surfaceN == terrainN
                ? LockedRoadSurfaceResult.NaturalSurface
                : LockedRoadSurfaceResult.FoundationSurface;
        }
    }
}

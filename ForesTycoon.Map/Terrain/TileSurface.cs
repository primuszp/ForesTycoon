using System;
using OpenTK.Mathematics;

namespace ForesTycoon.Map
{
    internal enum TileSurfaceMaterial
    {
        Grass,
        Foundation,
        MixedFoundation
    }

    internal enum TileCorner
    {
        W,
        S,
        E,
        N
    }

    internal enum TileDiagonalDirection
    {
        Natural,
        WE,
        NS
    }

    internal readonly struct TileSurface
    {
        public TileSurface(
            TileSurfaceMaterial surfaceMaterial,
            TileSurfaceMaterial firstTriangleMaterial,
            TileSurfaceMaterial secondTriangleMaterial,
            TileDiagonalDirection splitDirection,
            bool drawFoundationDiagonal,
            TileSurfaceMaterial edgeWS,
            TileSurfaceMaterial edgeSE,
            TileSurfaceMaterial edgeEN,
            TileSurfaceMaterial edgeNW)
        {
            SurfaceMaterial = surfaceMaterial;
            FirstTriangleMaterial = firstTriangleMaterial;
            SecondTriangleMaterial = secondTriangleMaterial;
            SplitDirection = splitDirection;
            DrawFoundationDiagonal = drawFoundationDiagonal;
            EdgeWS = edgeWS;
            EdgeSE = edgeSE;
            EdgeEN = edgeEN;
            EdgeNW = edgeNW;
        }

        public TileSurfaceMaterial SurfaceMaterial { get; }
        public TileSurfaceMaterial FirstTriangleMaterial { get; }
        public TileSurfaceMaterial SecondTriangleMaterial { get; }
        public TileDiagonalDirection SplitDirection { get; }
        public bool DrawFoundationDiagonal { get; }
        public TileSurfaceMaterial EdgeWS { get; }
        public TileSurfaceMaterial EdgeSE { get; }
        public TileSurfaceMaterial EdgeEN { get; }
        public TileSurfaceMaterial EdgeNW { get; }
    }

    internal readonly struct FoundationFaceData
    {
        public FoundationFaceData(int tileId, Vector3 topA, Vector3 topB, Vector3 bottomB, Vector3 bottomA)
        {
            TileId = tileId;
            TopA = topA;
            TopB = topB;
            BottomB = bottomB;
            BottomA = bottomA;
        }

        public int TileId { get; }
        public Vector3 TopA { get; }
        public Vector3 TopB { get; }
        public Vector3 BottomB { get; }
        public Vector3 BottomA { get; }
    }
}

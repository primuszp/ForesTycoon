using System;

namespace ForesTycoon
{
    sealed class TerrainSettings
    {
        public static readonly TerrainSettings Default = new TerrainSettings(
            nodeColumns: 65,
            nodeRows: 65,
            tileWidth: 5,
            tileHeight: 5,
            heightScale: 2,
            minimumWaterDepth: 0.04f,
            riverWaterHeight: 0.55f,
            seaLevel: 3.0f);

        public int NodeColumns { get; }
        public int NodeRows { get; }
        public int TileWidth { get; }
        public int TileHeight { get; }
        public int HeightScale { get; }
        public float MinimumWaterDepth { get; }
        public float RiverWaterHeight { get; }
        public float SeaLevel { get; }

        public int TileColumns => NodeColumns - 1;
        public int TileRows => NodeRows - 1;
        public int OffsetX => TileWidth * NodeColumns / 2;
        public int OffsetY => TileHeight * NodeRows / 2;

        public TerrainSettings(
            int nodeColumns,
            int nodeRows,
            int tileWidth,
            int tileHeight,
            int heightScale,
            float minimumWaterDepth,
            float riverWaterHeight,
            float seaLevel)
        {
            if (nodeColumns < 2) throw new ArgumentOutOfRangeException(nameof(nodeColumns));
            if (nodeRows < 2) throw new ArgumentOutOfRangeException(nameof(nodeRows));
            if (tileWidth <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth));
            if (tileHeight <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeight));
            if (heightScale <= 0) throw new ArgumentOutOfRangeException(nameof(heightScale));
            if (minimumWaterDepth <= 0f) throw new ArgumentOutOfRangeException(nameof(minimumWaterDepth));
            if (riverWaterHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(riverWaterHeight));

            NodeColumns = nodeColumns;
            NodeRows = nodeRows;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            HeightScale = heightScale;
            MinimumWaterDepth = minimumWaterDepth;
            RiverWaterHeight = riverWaterHeight;
            SeaLevel = seaLevel;
        }
    }
}

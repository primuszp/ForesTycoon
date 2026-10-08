using System;

namespace ForesTycoon.Ecology
{
    internal sealed class RasterGrid
    {
        internal int Columns { get; }
        internal int Rows { get; }
        internal int Count { get; }
        internal double CellWidthMeters { get; }
        internal double CellHeightMeters { get; }
        internal double CellAreaSquareMeters => CellWidthMeters * CellHeightMeters;

        internal RasterGrid(int columns, int rows, double cellWidthMeters, double cellHeightMeters)
        {
            if (columns <= 0 || rows <= 0 || (long)columns * rows > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(columns));
            if (!double.IsFinite(cellWidthMeters) || cellWidthMeters <= 0 ||
                !double.IsFinite(cellHeightMeters) || cellHeightMeters <= 0 ||
                !double.IsFinite(cellWidthMeters * cellHeightMeters))
                throw new ArgumentOutOfRangeException(nameof(cellWidthMeters));
            Columns = columns; Rows = rows; Count = columns * rows;
            CellWidthMeters = cellWidthMeters; CellHeightMeters = cellHeightMeters;
        }
        internal int TileId(int column, int row)
        {
            if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
                throw new ArgumentOutOfRangeException(nameof(column));
            return column * Rows + row;
        }
    }

    internal readonly record struct RasterFieldDescriptor(string Id, string Name, string Unit, string Owner, bool Categorical);

    /// <summary>Immutable published field with owned storage and explicit geometry/units.</summary>
    internal sealed class RasterField<T> where T : unmanaged
    {
        private readonly T[] values;
        internal RasterGrid Grid { get; }
        internal RasterFieldDescriptor Descriptor { get; }
        internal ReadOnlySpan<T> Values => values;
        internal T this[int tileId] => values[tileId];

        internal RasterField(RasterGrid grid, RasterFieldDescriptor descriptor, T[] values)
        {
            ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(values);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Unit);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Owner);
            if (values.Length != grid.Count) throw new ArgumentException("Raster values do not match grid geometry.");
            Grid = grid; Descriptor = descriptor; this.values = (T[])values.Clone();
        }
    }
}

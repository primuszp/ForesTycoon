#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ForesTycoon.TreeModels.Generation.Dendro.Geom;

namespace ForesTycoon.TreeModels.Generation.Dendro.Tree;

internal sealed class SubsegmentImpl
{
    public Vector3d    Position { get; }
    public double      Radius   { get; }
    public double      Pos      { get; }  // distance from segment base
    public SegmentImpl Segment  { get; }

    public SubsegmentImpl? Next { get; set; }
    public SubsegmentImpl? Prev { get; set; }

    public SubsegmentImpl(Vector3d position, double radius, double pos, SegmentImpl segment)
    {
        Position = position;
        Radius   = radius;
        Pos      = pos;
        Segment  = segment;
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ForesTycoon.TreeModels.Generation.Dendro.Geom;

namespace ForesTycoon.TreeModels.Generation.Dendro.Mesh;

public sealed class MeshSection
{
    private readonly List<Vertex> _vertices = new();

    public MeshSection? Next     { get; set; }
    public MeshSection? Previous { get; set; }
    public bool         IsFirst  { get; set; }

    public int Count => _vertices.Count;

    public void AddVertex(Vertex v) => _vertices.Add(v);

    public Vertex PointAt(int i) => _vertices[i % _vertices.Count];

    public IReadOnlyList<Vertex> AllVertices() => _vertices;

    public void SetNormalsUp()
    {
        if (Next == null || _vertices.Count != Next._vertices.Count) return;
        for (int i = 0; i < _vertices.Count; i++)
        {
            var v = _vertices[i];
            var vn = Next._vertices[i];
            v.Normal = vn.Point.Sub(v.Point).Normalize();
        }
    }

    public void SetNormalsUpDown()
    {
        if (Previous == null || Next == null) return;
        for (int i = 0; i < _vertices.Count; i++)
        {
            var vprev = Previous.PointAt(i);
            var vnext = Next.PointAt(i);
            var v     = _vertices[i];
            var dir   = vnext.Point.Sub(vprev.Point);
            v.Normal  = dir.Abs() > 1e-10 ? dir.Normalize() : Vector3d.ZAxis;
        }
    }
}

using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Drawing;

namespace ForesTycoon.Tests;

[CollectionDefinition("Primitive geometry capture")]
public sealed class PrimitiveGeometryCaptureCollection { }

// The production emitter deliberately shares one batch on the render thread.
[Collection("Primitive geometry capture")]
public class ForestRenderingTests
{
    [Fact]
    public void LodFadePreservesCoverageWhenReversingIncludingAcrossASkippedLevel()
    {
        foreach (var (low, high) in new[] {
            (ForestLod.Far, ForestLod.Medium), (ForestLod.Medium, ForestLod.Near), (ForestLod.Far, ForestLod.Near)
        }) {
            var start = ForestLodFade.Start(low, high);
            Assert.Equal(low, start.Endpoint);
            Assert.Equal(high, ForestLodFade.Start(high, low).Endpoint);
            var first = start.Advance(high, 1f / 60);
            var second = first.Advance(high, 1f / 60);
            Assert.Null(first.Endpoint);
            Assert.InRange(second.HighCoverage, first.HighCoverage, .1f);
            Assert.Equal(first.HighCoverage, second.Advance(low, 1f / 60).HighCoverage, 6);
            Assert.Equal(second, second.Advance(low, 0));
        }
    }

    [Fact]
    public void LodFadeTakesMultipleFramesEvenAfterAHitch()
    {
        var fade = ForestLodFade.Start(ForestLod.Near, ForestLod.Far);
        for (int i = 0; i < 6; i++) {
            float previous = fade.HighCoverage;
            fade = fade.Advance(ForestLod.Far, .25f);
            Assert.Null(fade.Endpoint);
            Assert.InRange(previous - fade.HighCoverage, 0, .24f);
        }
        Assert.Equal(ForestLod.Far, fade.Advance(ForestLod.Far, .25f).Endpoint);
        Assert.Equal(ForestLod.Far, fade.Advance(ForestLod.Medium, .25f).Endpoint);
    }

    [Fact]
    public void Lod_HasHysteresisAndSupportsLargeZoomJumps()
    {
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.Select(10, null));
        Assert.Equal(ForestLod.Near, ForestLodPolicy.Select(14, ForestLod.Near));
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.Select(14, ForestLod.Medium));
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.Select(3, ForestLod.Medium));
        Assert.Equal(ForestLod.Far, ForestLodPolicy.Select(3, ForestLod.Far));
        Assert.Equal(ForestLod.Far, ForestLodPolicy.Select(1, ForestLod.Near));
        Assert.Equal(ForestLod.Near, ForestLodPolicy.Select(20, ForestLod.Far));
        Assert.Equal(ForestLod.Far, ForestLodPolicy.SelectShadow(4));
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.SelectShadow(12));
    }

    [Fact]
    public void GeometryCapture_TriangulatesAndRecoversAfterFailureWithoutGL()
    {
        Vertex[] mesh = DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Quads, () =>
        {
            DynamicPrimitiveBatch.Color4(Color.FromArgb(128, 20, 40, 60));
            DynamicPrimitiveBatch.Vertex3(0, 0, 0);
            DynamicPrimitiveBatch.Vertex3(1, 0, 0);
            DynamicPrimitiveBatch.Vertex3(1, 1, 0);
            DynamicPrimitiveBatch.Vertex3(0, 1, 0);
        });
        Assert.Equal(6, mesh.Length);
        Assert.Equal(new Vector3(0, 0, 0), mesh[3].Position);
        Assert.Equal(new Vector3(0, 1, 0), mesh[5].Position);
        Assert.All(mesh, vertex => Assert.Equal(0x803c2814u, vertex.Color));
        Assert.Throws<InvalidOperationException>(() => DynamicPrimitiveBatch.BuildGeometry(
            PrimitiveTopology.Quads, () => DynamicPrimitiveBatch.Vertex3(0, 0, 0)));
        Assert.Empty(DynamicPrimitiveBatch.BuildGeometry(PrimitiveTopology.Triangles, () => { }));
    }

    [Fact]
    public void MonthlyGrowthChangesRevisionButDoesNotRequestImmediateEditRebuild()
    {
        var forest = new ForestSystem(new Habitat());
        forest.Clear();
        forest.Plant(0, ForestSpecies.Oak);
        ulong edit = forest.EditRevision, revision = forest.Revision;
        forest.Update(ForestSystem.DefaultSecondsPerYear / 12);
        Assert.True(forest.Revision > revision);
        Assert.Equal(edit, forest.EditRevision);
        forest.Harvest(0, out _);
        Assert.True(forest.EditRevision > edit);
    }

    private sealed class Habitat : IForestHabitat
    {
        public bool Supported = true;
        public int Queries;
        public int TileCount => 2;
        public int Seed => 42;
        public bool CanSupportForest(int tileId) => Supported;
        public float GetMoisture(int tileId) => 0.65f;
        public float GetNormalizedElevation(int tileId) => 0.5f;
        public int GetAdjacentTileIds(int tileId, Span<int> destination)
        {
            Queries++;
            destination[0] = 1 - tileId;
            return 1;
        }
    }
}

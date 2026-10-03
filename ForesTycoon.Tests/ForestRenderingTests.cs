using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Drawing;

namespace ForesTycoon.Tests;

public class ForestRenderingTests
{
    [Fact]
    public void LodTransitionIsContinuousAcrossBothZoomBands()
    {
        foreach(var band in new[]{(2.5f,4.5f),(7f,11f)}) {
            float previous=0;
            for(int step=1;step<100;step++) {
                var transition=ForestLodPolicy.Transition(band.Item1+(band.Item2-band.Item1)*step/100);
                Assert.NotEqual(transition.Low,transition.High);
                Assert.InRange(transition.Blend,previous,previous+0.02f);
                previous=transition.Blend;
            }
            Assert.InRange(previous,0.99f,1);
        }
        Assert.Equal(ForestLod.Far,ForestLodPolicy.Transition(1).Low);
        Assert.Equal(ForestLod.Near,ForestLodPolicy.Transition(12).Low);
    }

    [Fact]
    public void Lod_HasHysteresisAndSupportsLargeZoomJumps()
    {
        Assert.Equal(ForestLod.Near, ForestLodPolicy.Select(10, null));
        Assert.Equal(ForestLod.Near, ForestLodPolicy.Select(8, ForestLod.Near));
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.Select(8, ForestLod.Medium));
        Assert.Equal(ForestLod.Medium, ForestLodPolicy.Select(3, ForestLod.Medium));
        Assert.Equal(ForestLod.Far, ForestLodPolicy.Select(3, ForestLod.Far));
        Assert.Equal(ForestLod.Far, ForestLodPolicy.Select(1, ForestLod.Near));
        Assert.Equal(ForestLod.Near, ForestLodPolicy.Select(20, ForestLod.Far));
    }

    [Fact]
    public void VisualState_QuantizesGrowthWithoutChangingSimulation()
    {
        var stand = new ForestStand(ForestSpecies.Oak, 20, 0.5f, 0.8f);
        var slightlyOlder = stand with { AgeYears = stand.AgeYears + 0.001f };
        Assert.Equal(ForestVisualState.From(stand, 0.5f), ForestVisualState.From(slightlyOlder, 0.5f));
        Assert.NotEqual(stand, slightlyOlder);
        Assert.NotEqual(ForestVisualState.From(stand, 0.5f), ForestVisualState.From(stand, 1));
        Assert.Equal(default, ForestVisualState.From(default, 1));
    }

    [Fact]
    public void ChunkCache_TracksPlantHarvestResetAndHabitatRemoval()
    {
        var habitat = new Habitat();
        var forest = new ForestSystem(habitat);
        forest.Clear();
        var state = new ForestChunkVisualState(1);
        int[] ids = { 0 };
        Assert.True(state.Refresh(forest, ids));
        Assert.False(state.Refresh(forest, ids));
        Assert.Equal(ForestryActionResult.Planted, forest.Plant(0, ForestSpecies.Oak));
        Assert.True(state.Refresh(forest, ids));
        Assert.Equal(ForestSpecies.Oak, state.Tiles[0].Species);
        Assert.False(state.Refresh(forest, ids));
        forest.Harvest(0, out _);
        Assert.True(state.Refresh(forest, ids));
        forest.Plant(0, ForestSpecies.Birch);
        state.Refresh(forest, ids);
        habitat.Supported = false;
        forest.RefreshHabitat();
        Assert.True(state.Refresh(forest, ids));
        Assert.Equal(default, state.Tiles[0]);
        habitat.Supported = true;
        forest.Plant(0, ForestSpecies.Beech);
        state.Refresh(forest, ids);
        forest.Reset(habitat);
        Assert.True(state.Refresh(forest, ids));
    }

    [Fact]
    public void ChunkCache_SeesNeighbourChangesAcrossChunkBoundary()
    {
        var forest = new ForestSystem(new Habitat());
        forest.Clear();
        forest.Plant(0, ForestSpecies.Oak);
        var state = new ForestChunkVisualState(1);
        int[] ids = { 0 };
        state.Refresh(forest, ids);
        forest.Plant(1, ForestSpecies.Oak);
        // Oak seedlings already cross the first canopy-pressure quantization step.

        state.Refresh(forest, ids);
        Assert.True(state.Tiles[0].Crowding > 0);
        forest.Harvest(1, out _);
        Assert.True(state.Refresh(forest, ids));
        Assert.Equal(0, state.Tiles[0].Crowding);
    }

    [Fact]
    public void ChunkCache_SkipsQueriesOnUnchangedRevisionAndUnaffectedChunk()
    {
        var habitat = new Habitat();
        var forest = new ForestSystem(habitat);
        forest.Clear();
        var state = new ForestChunkVisualState(1);
        int[] ids = { 0 };
        state.Refresh(forest, ids);
        int queries = habitat.Queries;
        Assert.False(state.Refresh(forest, ids));
        Assert.Equal(queries, habitat.Queries);
        forest.Plant(1, ForestSpecies.Oak);
        Assert.False(state.Refresh(forest, ids)); // Empty chunk remains empty.
    }

    [Fact]
    public void GeometryCapture_TriangulatesAndRecoversAfterFailureWithoutGL()
    {
        Vertex[] mesh = DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Quads, () =>
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
            PrimitiveType.Quads, () => DynamicPrimitiveBatch.Vertex3(0, 0, 0)));
        Assert.Empty(DynamicPrimitiveBatch.BuildGeometry(PrimitiveType.Triangles, () => { }));
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

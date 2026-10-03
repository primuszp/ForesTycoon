namespace ForesTycoon.Tests;

public class ImportedForestModelTests
{
    [Fact]
    public void GeneratedModelsKeepOneSkeletonPlacementAcrossLods()
    {
        using var library=new ImportedForestModels();
        foreach(var (asset,nearCount,mediumCount,farCount) in new[] {
            (ForestAsset.GeneratedPineA,7896,3614,1314),(ForestAsset.GeneratedPineB,7896,3614,1314),
            (ForestAsset.GeneratedOakA,4416,2118,694),(ForestAsset.GeneratedOakB,4416,2118,694) }) {
            var near=library.Get(asset);var medium=library.Get(asset,ForestLod.Medium);var far=library.Get(asset,ForestLod.Far);
            Assert.Equal(nearCount,near.TriangleCount);Assert.Equal(mediumCount,medium.TriangleCount);Assert.Equal(farCount,far.TriangleCount);
            Assert.Equal(near.Normalization,medium.Normalization);Assert.Equal(near.Normalization,far.Normalization);
            Assert.Equal(2,near.Model.Meshes.Length);Assert.Equal(2,far.Model.Meshes.Length);
        }
    }
    [Fact]
    public void ExtractedTreesKeepGeometryButMergeRigidMaterialGroups()
    {
        using var library=new ImportedForestModels();
        foreach(var (key,triangles,meshes) in new[] {
            (ForestAsset.SpruceTall,630,2),(ForestAsset.SpruceSmall,624,2),(ForestAsset.SpruceMedium,624,2),
            (ForestAsset.OakSmall,3783,2),(ForestAsset.OakMedium,2951,2),(ForestAsset.OakLarge,7741,2),
            (ForestAsset.BroadleafA,3519,2),(ForestAsset.BroadleafB,1862,2),(ForestAsset.Birch,10616,6) }) {
            var model=library.Get(key);
            Assert.Same(model,library.Get(key));
            Assert.Equal(triangles,model.TriangleCount);
            Assert.Equal(meshes,model.Model.Meshes.Length);
            var heights=model.Model.Meshes.SelectMany(m=>Enumerable.Range(0,m.Vertices.Length/16).Select(i=>m.Vertices[i*16+2])).ToArray();
            Assert.InRange(heights.Min(),-.00001f,.00001f);Assert.InRange(heights.Max(),.99999f,1.00001f);
            Assert.All(model.Model.Meshes,m=> {
                Assert.Equal(-1,m.Skin);Assert.NotEqual(AnimatedGlbModel.AlphaMode.Blend,m.Alpha);
                Assert.All(m.Vertices,v=>Assert.True(float.IsFinite(v)));
                if(m.Image>=0)Assert.True(m.FlatColor!.Value.Y>0);
            });
        }
    }
    [Fact]
    public void VisualPresetAndBirchOptInDoNotChangeIndividualTreeData()
    {
        var settings=new GraphicsSettings();
        Assert.Equal(ForestModelStyle.Procedural,settings.ForestModels);
        settings.ForestModels=ForestModelStyle.Imported;
        Assert.True(ImportedForestModels.UsesImported(ForestSpecies.Spruce,settings));
        Assert.False(ImportedForestModels.UsesImported(ForestSpecies.Birch,settings));
        settings.ImportedBirch=true;Assert.True(ImportedForestModels.UsesImported(ForestSpecies.Birch,settings));
        settings.ForestModels=ForestModelStyle.Procedural;
        Assert.False(ImportedForestModels.UsesImported(ForestSpecies.Spruce,settings));
        Assert.False(ImportedForestModels.UsesImported(ForestSpecies.Birch,settings));
    }
}

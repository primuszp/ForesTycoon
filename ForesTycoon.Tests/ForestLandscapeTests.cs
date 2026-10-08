using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class ForestLandscapeTests
{
    [Theory]
    [InlineData((int)ForestPattern.LargeMixed)]
    [InlineData((int)ForestPattern.LargeSpruce)]
    [InlineData((int)ForestPattern.LargeBroadleaf)]
    public void LargeForestsAreConnectedRepeatableAndRespectBlockedLand(int patternValue)
    {
        ForestPattern pattern=(ForestPattern)patternValue;
        var habitat=new Habitat();
        var stands=LargeForestGenerator.Create(habitat,32,32,pattern);
        Assert.Equal(stands,LargeForestGenerator.Create(habitat,32,32,pattern));
        Assert.True(stands.Count(s=>!s.IsEmpty)>450);
        Assert.True(stands[32*16+16].IsEmpty);
        var visited=new HashSet<int>(); int largest=0;
        for(int i=0;i<stands.Length;i++){
            if(stands[i].IsEmpty||!visited.Add(i)) continue;
            var queue=new Queue<int>();queue.Enqueue(i);int count=0;
            while(queue.TryDequeue(out int tile)){
                count++;int x=tile/32,y=tile%32;
                foreach(var n in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)}){
                    if(n.Item1<0||n.Item1>=32||n.Item2<0||n.Item2>=32)continue;
                    int id=n.Item1*32+n.Item2;
                    if(!stands[id].IsEmpty&&visited.Add(id))queue.Enqueue(id);
                }
            }
            largest=Math.Max(largest,count);
        }
        Assert.True(largest>300);
        if(pattern==ForestPattern.LargeSpruce)Assert.All(stands.Where(s=>!s.IsEmpty),s=>Assert.Equal(ForestSpecies.Spruce,s.Species));
        if(pattern==ForestPattern.LargeBroadleaf)Assert.DoesNotContain(stands,s=>s.Species==ForestSpecies.Spruce);
        if(pattern==ForestPattern.LargeMixed){
            Assert.True(stands.Count(s=>s.Species==ForestSpecies.Spruce)>150);
            Assert.True(stands.Count(s=>!s.IsEmpty&&s.Species!=ForestSpecies.Spruce)>150);
        }
        var settings=TerrainSettings.Default.WithForestPattern(pattern).WithSeed(555).WithNodeSize(33,555);
        Assert.Equal(pattern,TerrainSettingsData.From(settings).ToSettings().ForestPattern);
        using var save=new System.IO.MemoryStream();
        WorldSaveSerializer.Write(save,new WorldSaveData{SoilModel=SoilModelData.From(SoilLandscapeDefinition.Default),Terrain=TerrainSettingsData.From(settings)});
        save.Position=0;
        Assert.Equal(pattern,WorldSaveSerializer.Read(save).Terrain.ToSettings().ForestPattern);
        Assert.Equal(ForestPattern.Natural,new TerrainSettingsData{NodeColumns=33,NodeRows=33,TileWidth=5,TileHeight=5,HeightScale=2,MinimumWaterDepth=0.04f,RiverWaterHeight=0.55f,SeaLevel=3,MaxHeight=6}.ToSettings().ForestPattern);
    }
    [Fact]
    public void FogIsPatchyFavoursWetValleysAndWaterAndDispersesInWind()
    {
        float dry=0,wet=0,windy=0;int clear=0,visible=0;
        for(int x=0;x<40;x++)for(int y=0;y<40;y++){
            var source=new FogSource(new Vector4(x*5,y*5,2,4),1,0,0.7f,0.5f);
            float d=FogHabitat.Density(source,30,1,0,0);
            wet+=d;dry+=FogHabitat.Density(source,30,0,0,0);
            windy+=FogHabitat.Density(source,30,1,7,0);
            if(d<0.015)clear++; if(d>0.15)visible++;
        }
        Assert.True(wet>dry);Assert.True(windy<wet);Assert.True(clear>100);Assert.True(visible>100);
        var empty=new FogSource(new Vector4(25,25,2,4),0,0,0,0.5f);
        Assert.Equal(0,FogHabitat.Density(empty,30,1,0,0));
    }
    private sealed class Habitat:IForestHabitat
    {
        public int TileCount=>1024;public int Seed=>8127;
        public bool CanSupportForest(int id)=>id!=32*16+16;
        public float GetMoisture(int id)=>0.6f;
        public float GetNormalizedElevation(int id)=>0.4f;
        public int GetAdjacentTileIds(int id,Span<int> destination)=>0;
    }
}

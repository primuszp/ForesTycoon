using OpenTK.Mathematics;
namespace ForesTycoon.Tests;
public class GlbTruckTests
{
    [Fact]
    public void SelectedAssetContainsOnlyLogTruck_WithSeparateCargoAndWheels()
    {
        string path=System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Vehicles","log-truck.glb");
        using var model=GlbTruckModel.Load(path);
        Assert.InRange(model.DrawGroupCount, 7, 13);
        Assert.True(model.Radius > model.Width);
        Assert.Equal(6,model.Parts.Count(p=>p.Category=="cargo"));
        Assert.Contains(model.Parts,p=>p.Category=="wheel");
        Assert.DoesNotContain(model.Parts,p=>p.Name.Contains("Tanker")||p.Name.Contains(".002")||p.Name.Contains(".003"));
        var vertices=model.Parts.SelectMany(p=>p.Vertices).ToArray();
        Assert.Equal(8877*3,vertices.Length);
        Assert.InRange(vertices.Max(v=>v.Position.X)-vertices.Min(v=>v.Position.X),3.29f,3.31f);
        Assert.InRange(vertices.Min(v=>v.Position.Z),-0.001f,0.001f);
        Assert.All(vertices,v=>Assert.InRange(v.Normal.Length,0.999f,1.001f));
        Assert.True(vertices.Select(v=>v.Color).Distinct().Count()>5);
    }
}

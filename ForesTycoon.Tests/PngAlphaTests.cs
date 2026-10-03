namespace ForesTycoon.Tests;

public class PngAlphaTests
{
    [Fact]
    public void PackedPaletteExpandsRgbAndPartialTransparency()
    {
        var image=PngImage.Decode(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABAQMAAADO7O3JAAAABlBMVEX/AAAA/wDSh+9xAAAAAnRSTlMAgJsrThgAAAAKSURBVHicY3AAAABCAEEpN/TvAAAAAElFTkSuQmCC"));
        Assert.Equal(new byte[] {255,0,0,0,0,255,0,128},image.Pixels);
    }
    [Fact]
    public void PackedGrayscaleAndGrayscaleAlphaExpandToRgba()
    {
        var packed=PngImage.Decode(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABAQAAAADcWUInAAAACklEQVR4nGNwAAAAQgBBKTf07wAAAABJRU5ErkJggg=="));
        Assert.Equal(new byte[] {0,0,0,255,255,255,255,255},packed.Pixels);
        var alpha=PngImage.Decode(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAQAAABeK7cBAAAADUlEQVR4nGMwYjjRAAAC3QF7so1CiQAAAABJRU5ErkJggg=="));
        Assert.Equal(new byte[] {50,50,50,0,200,200,200,128},alpha.Pixels);
    }
    [Fact]
    public void RgbTransparentColourKeyIsPreserved()
    {
        var image=PngImage.Decode(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAABnRSTlMA/wAAAACkwsAdAAAAD0lEQVR4nGP4z8DA8J8BAAf/Af8Bf4mnAAAAAElFTkSuQmCC"));
        Assert.Equal(new byte[] {255,0,0,0,0,255,0,255},image.Pixels);
    }
}

using Delphis.Core.Services;

namespace Delphis.Tests;

public class AlphaProcessorTests
{
    [Fact]
    public void ParseColor_ShouldParseHexAndNamedColors()
    {
        var white = AlphaProcessor.ParseColor("white");
        Assert.NotNull(white);
        Assert.Equal(255, white!.R);
        Assert.Equal(255, white!.G);
        Assert.Equal(255, white!.B);

        var hexRed = AlphaProcessor.ParseColor("#FF0000");
        Assert.NotNull(hexRed);
        Assert.Equal(255, hexRed!.R);
        Assert.Equal(0, hexRed!.G);
        Assert.Equal(0, hexRed!.B);

        Assert.Null(AlphaProcessor.ParseColor(null));
        Assert.Null(AlphaProcessor.ParseColor(""));
        Assert.Null(AlphaProcessor.ParseColor("nonexistent-color-123"));
    }

    [Fact]
    public void DefringeChannel_ShouldUnmultiplySemiTransparentPixels()
    {
        byte[] color = { 100, 100, 50, 100 };
        byte[] alpha = { 255, 128, 64, 0 };

        AlphaProcessor.DefringeChannel(color.AsSpan(), alpha.AsSpan());

        Assert.Equal((byte)100, color[0]);
        Assert.Equal((byte)199, color[1]);
        Assert.Equal((byte)199, color[2]);
        Assert.Equal((byte)100, color[3]);
    }

    [Fact]
    public void BuildMagickArguments_ShouldIncludeDefringeAndAlphaScaleByDefault()
    {
        var opts = new AlphaOptions(AlphaValue: 130, SmoothEdges: true, SmoothRadius: 1.5, Defringe: true);
        var args = AlphaProcessor.BuildMagickArguments("in.png", "out.png", opts);

        Assert.Contains("-alpha", args);
        Assert.Contains("set", args);
        Assert.Contains("-fx", args);
        Assert.Contains("multiply", args);
        Assert.Contains("-blur", args);
        Assert.Contains("0x1.5", args);
        Assert.Equal("in.png", args[0]);
        Assert.Equal("out.png", args[^1]);
    }

    [Fact]
    public void BuildMagickArguments_WhenNoDefringeAndHardCutout_ShouldOmitFxAndUseSet()
    {
        var opts = new AlphaOptions(AlphaValue: 200, ScaleAlpha: false, Defringe: false);
        var args = AlphaProcessor.BuildMagickArguments("in.png", "out.png", opts);

        Assert.False(args.Contains("-fx"));
        Assert.Contains("set", args);
        Assert.Contains("200", args);
    }

    [Fact]
    public void BuildMagickArguments_RemoveAlphaWithMatte_ShouldCompositeOverBackground()
    {
        var opts = new AlphaOptions(RemoveAlpha: true, BackgroundColor: new RgbColor(255, 0, 128));
        var args = AlphaProcessor.BuildMagickArguments("in.png", "out.png", opts);

        Assert.Contains("-background", args);
        Assert.Contains("#FF0080", args);
        Assert.Contains("remove", args);
        Assert.False(args.Contains("-fx"));
    }
}

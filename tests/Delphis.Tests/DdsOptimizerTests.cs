using Delphis.Core.Services;

namespace Delphis.Tests;

public class DdsOptimizerTests
{
    [Fact]
    public void BuildEncoderArguments_Nvcompress_ShouldGenerateCorrectFlags()
    {
        var optionsBc7 = new DdsOptions(Preset: "BC7", GenerateMipmaps: true);
        var argsBc7 = DdsOptimizer.BuildEncoderArguments("nvcompress", "in.png", "out.dds", optionsBc7);

        Assert.Contains("-bc7", argsBc7);
        Assert.Contains("-mip", argsBc7);
        Assert.Contains("in.png", argsBc7);
        Assert.Contains("out.dds", argsBc7);

        var optionsBc1 = new DdsOptions(Preset: "BC1", GenerateMipmaps: false);
        var argsBc1 = DdsOptimizer.BuildEncoderArguments("nvcompress", "in.png", "out.dds", optionsBc1);

        Assert.Contains("-bc1", argsBc1);
        Assert.Contains("-nomip", argsBc1);
    }

    [Fact]
    public void BuildEncoderArguments_Texconv_ShouldGenerateCorrectFormats()
    {
        var options = new DdsOptions(Preset: "BC3", GenerateMipmaps: true);
        var args = DdsOptimizer.BuildEncoderArguments("texconv", "in.png", "out.dds", options);

        Assert.Contains("-f", args);
        Assert.Contains("BC3_UNORM", args);
        Assert.Contains("-m", args);
        Assert.Contains("0", args);
    }

    [Fact]
    public void BuildEncoderArguments_TexconvRelativeOutput_ShouldUseCurrentDir()
    {
        var options = new DdsOptions(Preset: "BC7", GenerateMipmaps: false);
        var args = DdsOptimizer.BuildEncoderArguments("texconv", "in.png", "out.dds", options);

        int oIdx = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "-o")
            {
                oIdx = i;
                break;
            }
        }
        Assert.True(oIdx >= 0 && oIdx + 1 < args.Count);
        Assert.Equal(".", args[oIdx + 1]);
    }

    [Fact]
    public void ConvertBatchAsync_MissingInputPath_ShouldReturnZeroWithoutThrowing()
    {
        var optimizer = new DdsOptimizer();
        var missing = Path.Combine(Path.GetTempPath(), $"delphis_missing_{Guid.NewGuid():N}");
        var count = optimizer.ConvertBatchAsync(missing, Path.GetTempPath(), new DdsOptions()).GetAwaiter().GetResult();
        Assert.Equal(0, count);
    }
}

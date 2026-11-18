using Delphis.Core.Models;
using Delphis.Core.Services;

namespace Delphis.Tests;

public class UpscalerEngineTests
{
    [Fact]
    public void AutoDetectBestGpu_ShouldPickDiscreteGpuOverIntegrated()
    {
        var mockDetector = new MockEnvDetector(new List<GpuDeviceInfo>
        {
            new GpuDeviceInfo(0, "AMD Radeon 780M Graphics", IsDiscrete: false),
            new GpuDeviceInfo(1, "NVIDIA GeForce RTX 4070 Laptop GPU", IsDiscrete: true)
        });

        var upscaler = new UpscalerEngine(envDetector: mockDetector);
        int chosen = upscaler.AutoDetectBestGpu();

        Assert.Equal(1, chosen);
    }

    [Fact]
    public void AutoDetectBestGpu_WhenSingleIntegratedGpu_ShouldPickIt()
    {
        var mockDetector = new MockEnvDetector(new List<GpuDeviceInfo>
        {
            new GpuDeviceInfo(0, "Intel(R) Iris(R) Xe Graphics", IsDiscrete: false)
        });

        var upscaler = new UpscalerEngine(envDetector: mockDetector);
        int chosen = upscaler.AutoDetectBestGpu();

        Assert.Equal(0, chosen);
    }

    [Fact]
    public void FindModelsDirectory_ShouldFindShareModelsDirectoryIfPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"delphis_test_models_{Guid.NewGuid():N}");
        try
        {
            var binDir = Path.Combine(tempRoot, "bin");
            var shareModels = Path.Combine(tempRoot, "share", "models");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(shareModels);

            var dummyBin = Path.Combine(shareModels, "realesrgan-x4plus-anime.bin");
            File.WriteAllText(dummyBin, "dummy");

            var upscaler = new UpscalerEngine();
            var found = upscaler.FindModelsDirectory(Path.Combine(binDir, "realesrgan-ncnn-vulkan"));

            Assert.Equal(shareModels, found);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public void UpscaleSingleAsync_MissingInput_ShouldReturnFalse()
    {
        var engine = new UpscalerEngine();
        var missing = Path.Combine(Path.GetTempPath(), $"delphis_missing_{Guid.NewGuid():N}.png");
        var ok = engine.UpscaleSingleAsync(missing, Path.GetTempPath(), new UpscalerOptions()).GetAwaiter().GetResult();
        Assert.False(ok);
    }

    [Fact]
    public void UpscaleSingleAsync_AlphaModeNone_ShouldSkipAlphaExtraction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"delphis_upscale_mock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var input = Path.Combine(tempDir, "in.png");
        var output = Path.Combine(tempDir, "out.png");
        File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4 });

        try
        {
            var runner = new RecordingProcessRunner();
            var env = new StubToolEnvDetector();
            var engine = new UpscalerEngine(runner, env);

            var ok = engine.UpscaleSingleAsync(
                input,
                output,
                new UpscalerOptions(Scale: 2, Model: "realesrgan-x4plus-anime", AlphaMode: "none", GpuId: 0)
            ).GetAwaiter().GetResult();

            Assert.True(ok);
            Assert.False(runner.RecordedArgs.Any(a => a.Contains("extract")));
            Assert.False(runner.RecordedArgs.Any(a => a.Contains("CopyOpacity")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UpscaleBatchAsync_MissingInputPath_ShouldReturnZeroWithoutThrowing()
    {
        var engine = new UpscalerEngine(envDetector: new StubToolEnvDetector());
        var missing = Path.Combine(Path.GetTempPath(), $"delphis_missing_{Guid.NewGuid():N}");
        var count = engine.UpscaleBatchAsync(missing, Path.GetTempPath(), new UpscalerOptions()).GetAwaiter().GetResult();
        Assert.Equal(0, count);
    }

    [Fact]
    public void UpscaleSingleAsync_WhenRealEsrganMissingOnLinuxX64_ShouldDownloadAutomatically()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
            return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"delphis_autodl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var input = Path.Combine(tempDir, "in.png");
        var output = Path.Combine(tempDir, "out.png");
        File.WriteAllBytes(input, new byte[] { 1, 2, 3, 4 });

        try
        {
            var env = new MutableToolEnvDetector();
            var downloader = new FakeToolDownloader(() => env.HasRealEsrgan = true);
            var runner = new RecordingProcessRunner();
            var engine = new UpscalerEngine(runner, env, downloader);

            var ok = engine.UpscaleSingleAsync(
                input,
                output,
                new UpscalerOptions(Scale: 2, AlphaMode: "none", GpuId: 0)
            ).GetAwaiter().GetResult();

            Assert.True(ok);
            Assert.Equal(1, downloader.CallCount);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

internal class MutableToolEnvDetector : IEnvironmentDetector
{
    public bool HasRealEsrgan { get; set; }

    public string? FindExecutable(string name)
    {
        if (name.Contains("realesrgan", StringComparison.OrdinalIgnoreCase))
            return HasRealEsrgan ? $"/usr/bin/{name}" : null;
        return $"/usr/bin/{name}";
    }

    public IReadOnlyDictionary<string, DependencyInfo> CheckDependencies() => new Dictionary<string, DependencyInfo>();
    public IReadOnlyList<GpuDeviceInfo> DetectVulkanGpus() => new List<GpuDeviceInfo> { new(0, "Mock GPU", true) };
}

internal class FakeToolDownloader : IToolDownloader
{
    private readonly Action _onDownload;
    public int CallCount { get; private set; }

    public FakeToolDownloader(Action onDownload) => _onDownload = onDownload;

    public Task<bool> DownloadRealEsrganAsync(
        IProgress<ProcessingProgress>? progress = null,
        IProgress<LogEntry>? logProgress = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        _onDownload();
        return Task.FromResult(true);
    }
}

internal class StubToolEnvDetector : IEnvironmentDetector
{
    public string? FindExecutable(string name) => $"/usr/bin/{name}";
    public IReadOnlyDictionary<string, DependencyInfo> CheckDependencies() => new Dictionary<string, DependencyInfo>();
    public IReadOnlyList<GpuDeviceInfo> DetectVulkanGpus() => new List<GpuDeviceInfo> { new(0, "Mock GPU", true) };
}

internal class RecordingProcessRunner : IProcessRunner
{
    public List<IReadOnlyList<string>> RecordedArgs { get; } = new();

    public Task<ProcessExecutionResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IProgress<LogEntry>? logProgress = null,
        CancellationToken cancellationToken = default)
    {
        RecordedArgs.Add(arguments.ToList());

        if (executable.EndsWith("magick", StringComparison.Ordinal) && arguments.Contains("identify"))
        {
            return Task.FromResult(new ProcessExecutionResult(0, "64 64 srgba", string.Empty, false));
        }

        // Write dummy output file whenever -o <path> or trailing output path is passed.
        for (int i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "-o")
            {
                File.WriteAllBytes(arguments[i + 1], new byte[] { 9, 9 });
                return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, false));
            }
        }

        var last = arguments[^1];
        if (!last.StartsWith('-') && !last.EndsWith("info:", StringComparison.Ordinal))
        {
            var path = last.StartsWith("PNG24:", StringComparison.Ordinal) ? last["PNG24:".Length..] : last;
            File.WriteAllBytes(path, new byte[] { 9, 9 });
        }

        return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, false));
    }
}

internal class MockEnvDetector : IEnvironmentDetector
{
    private readonly IReadOnlyList<GpuDeviceInfo> _gpus;

    public MockEnvDetector(IReadOnlyList<GpuDeviceInfo> gpus)
    {
        _gpus = gpus;
    }

    public string? FindExecutable(string name) => null;
    public IReadOnlyDictionary<string, DependencyInfo> CheckDependencies() => new Dictionary<string, DependencyInfo>();
    public IReadOnlyList<GpuDeviceInfo> DetectVulkanGpus() => _gpus;
}

using System.Diagnostics;
using System.Text.RegularExpressions;
using Delphis.Core;
using Delphis.Core.Models;

namespace Delphis.Core.Services;

/// <summary>
/// Probes system environment, PATH directories, dependencies, and Vulkan GPU hardware.
/// </summary>
public class EnvironmentDetector : IEnvironmentDetector
{
    public string? FindExecutable(string name)
    {
        foreach (var dir in GetSearchDirectories())
        {
            var fullPath = Path.Combine(dir, name);
            if (IsExecutableFile(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Ordered list of directories searched for external tools:
    /// 1. $DELPHIS_TOOLS_PATH
    /// 2. $PATH
    /// 3. The Delphis installation itself (bundled dte, launchers)
    /// 4. The per-user tools directory ($XDG_DATA_HOME/delphis/bin)
    /// 5. Common locations on any Linux distribution (also covers Nix profiles)
    /// </summary>
    public static IReadOnlyList<string> GetSearchDirectories()
    {
        var home = DelphisPaths.Home;
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var ordered = new List<string>();
        ordered.AddRange(DelphisPaths.ExtraToolDirs);
        ordered.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        ordered.AddRange(DelphisPaths.InstallToolDirs);
        ordered.Add(DelphisPaths.UserToolsDir);
        ordered.AddRange(new[]
        {
            Path.Combine(home, ".local", "bin"),
            "/usr/local/bin",
            "/usr/bin",
            "/bin",
            "/opt/delphis/bin",
            Path.Combine(home, ".nix-profile", "bin"),
            "/run/current-system/sw/bin"
        });

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var dir in ordered)
        {
            if (!string.IsNullOrWhiteSpace(dir) && seen.Add(dir))
            {
                result.Add(dir);
            }
        }

        return result;
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch
        {
            return true;
        }
    }

    public IReadOnlyDictionary<string, DependencyInfo> CheckDependencies()
    {
        var definitions = new[]
        {
            (Key: "dte", Name: "DolphinTextureExtraction-tool (dte)", Executables: new[] { "dte", "DolphinTextureExtraction.tool" }, RequiredFor: "Tab 1: Dolphin Texture Extraction & Tonemaps", Optional: false),
            (Key: "realesrgan", Name: "Real-ESRGAN NCNN Vulkan", Executables: new[] { "realesrgan-ncnn-vulkan", "realesrgan" }, RequiredFor: "Tab 3: AI Upscaler", Optional: false),
            (Key: "magick", Name: "ImageMagick", Executables: new[] { "magick", "convert" }, RequiredFor: "Tab 3: AI Upscaler & Tab 4: DDS Fallback", Optional: false),
            (Key: "dds_tool", Name: "DDS Encoder (nvcompress / texconv / compressonatorcli / magick)", Executables: new[] { "nvcompress", "texconv", "compressonatorcli", "magick", "convert" }, RequiredFor: "Tab 4: DDS Texture Optimizer", Optional: false)
        };

        var results = new Dictionary<string, DependencyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in definitions)
        {
            string? foundPath = null;
            foreach (var exe in def.Executables)
            {
                var p = FindExecutable(exe);
                if (p is not null)
                {
                    foundPath = p;
                    break;
                }
            }

            results[def.Key] = new DependencyInfo(
                Key: def.Key,
                Name: def.Name,
                IsInstalled: foundPath is not null,
                Path: foundPath,
                RequiredFor: def.RequiredFor,
                IsOptional: def.Optional
            );
        }

        return results;
    }

    private static (bool Success, string Stdout, string Stderr) RunQuickProbe(string fileName, string arguments, int timeoutMs = 3000)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null)
            {
                return (false, string.Empty, string.Empty);
            }

            // Read both pipes concurrently before waiting so full buffers cannot deadlock.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, string.Empty, string.Empty);
            }

            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, timeoutMs);
            return (proc.ExitCode == 0, stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty, stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty);
        }
        catch
        {
            return (false, string.Empty, string.Empty);
        }
    }

    public IReadOnlyList<GpuDeviceInfo> DetectVulkanGpus()
    {
        var gpus = new List<GpuDeviceInfo>();

        // 1. Probe via vulkaninfo
        var vulkanInfo = FindExecutable("vulkaninfo");
        if (vulkanInfo is not null)
        {
            var (ok, stdout, _) = RunQuickProbe(vulkanInfo, "--summary");
            if (ok && !string.IsNullOrEmpty(stdout))
            {
                ParseVulkanInfoSummary(stdout, gpus);
            }
        }

        // 2. Probe via Real-ESRGAN CLI help output if needed
        if (gpus.Count == 0)
        {
            var realesrgan = FindExecutable("realesrgan-ncnn-vulkan") ?? FindExecutable("realesrgan");
            if (realesrgan is not null)
            {
                var (_, stdout, stderr) = RunQuickProbe(realesrgan, "-h");
                var combined = stdout + stderr;
                var matches = Regex.Matches(combined, @"\[(\d+)\s+([^\]]+)\]");
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var devId))
                    {
                        var name = match.Groups[2].Value.Trim();
                        var isDiscrete = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                         name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                         (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) && !name.Contains("Graphics", StringComparison.OrdinalIgnoreCase));
                        gpus.Add(new GpuDeviceInfo(devId, name, isDiscrete));
                    }
                }
            }
        }

        if (gpus.Count == 0)
        {
            gpus.Add(new GpuDeviceInfo(0, "Default Vulkan Device (Device 0)", false));
        }

        gpus.Insert(0, new GpuDeviceInfo(-1, "Auto-detect Best Discrete GPU (Recommended)", true));
        return gpus;
    }

    public static void ParseVulkanInfoSummary(string output, List<GpuDeviceInfo> destination)
    {
        int? currentId = null;
        string? currentName = null;
        string currentType = string.Empty;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var matchGpu = Regex.Match(line, @"^GPU(\d+):");
            if (matchGpu.Success)
            {
                if (currentId.HasValue && !string.IsNullOrEmpty(currentName))
                {
                    destination.Add(new GpuDeviceInfo(
                        currentId.Value,
                        currentName,
                        currentType.Contains("DISCRETE", StringComparison.OrdinalIgnoreCase)
                    ));
                }

                currentId = int.Parse(matchGpu.Groups[1].Value);
                currentName = null;
                currentType = string.Empty;
            }
            else if (line.StartsWith("deviceName", StringComparison.OrdinalIgnoreCase) && currentId.HasValue)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) currentName = parts[1].Trim();
            }
            else if (line.StartsWith("deviceType", StringComparison.OrdinalIgnoreCase) && currentId.HasValue)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2) currentType = parts[1].Trim();
            }
        }

        if (currentId.HasValue && !string.IsNullOrEmpty(currentName))
        {
            destination.Add(new GpuDeviceInfo(
                currentId.Value,
                currentName,
                currentType.Contains("DISCRETE", StringComparison.OrdinalIgnoreCase)
            ));
        }
    }
}

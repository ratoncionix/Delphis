using System.Globalization;
using Delphis.Core;
using Delphis.Core.Models;
using Delphis.Core.Services;

namespace Delphis.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        bool hasNoLogs = args.Contains("--no-logs");
        bool quiet = args.Contains("-q") || args.Contains("--quiet") || hasNoLogs;
        var filteredArgs = args.Where(a => a is not "-q" and not "--quiet" and not "--no-logs").ToArray();

        if (filteredArgs.Length == 0 || filteredArgs[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        if (filteredArgs[0] is "-v" or "--version" or "version")
        {
            Console.WriteLine($"{DelphisConstants.AppName} v{DelphisConstants.AppVersion}");
            return 0;
        }

        var command = filteredArgs[0].ToLowerInvariant();
        var cmdArgs = filteredArgs.Skip(1).ToArray();

        var env = new EnvironmentDetector();
        var runner = new ProcessRunner();
        IProgress<LogEntry>? progressLog = quiet ? null : new Progress<LogEntry>(e =>
        {
            var color = e.Level switch
            {
                LogLevel.Success => ConsoleColor.Green,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };
            Console.ForegroundColor = color;
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss}] {e.Message}");
            Console.ResetColor();
        });

        try
        {
            return command switch
            {
                "fetch-realesrgan" => await RunFetchRealEsrganAsync(),
                "alpha" => await RunAlphaAsync(cmdArgs, env, runner, progressLog),
                "dds" => await RunDdsAsync(cmdArgs, env, runner, progressLog),
                "upscale" => await RunUpscaleAsync(cmdArgs, env, runner, progressLog),
                "dte" => await RunDteAsync(cmdArgs, env, runner, progressLog, hasNoLogs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FATAL ERROR] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"{DelphisConstants.AppTitle} (CLI v{DelphisConstants.AppVersion})");
        Console.WriteLine("===============================================================");
        Console.WriteLine("Usage: delphis-cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  alpha <in> <out> [options]               Adjust or remove alpha transparency with anti-aliasing");
        Console.WriteLine("  dds <in> <out> [options]                 Convert textures to Dolphin-optimized DDS format");
        Console.WriteLine("  upscale <in> <out> [options]             4-channel alpha-preserving Real-ESRGAN AI upscaler");
        Console.WriteLine("  dte <mode> <in> <out> [options]          Extract, unpack, finalize or combine tonemaps via DTE");
        Console.WriteLine();
        Console.WriteLine("Global Options:");
        Console.WriteLine("  --quiet, -q, --no-logs                   Disable real-time execution logs");
        Console.WriteLine();
        Console.WriteLine("Alpha Options: --alpha <0-255>, --remove-alpha, --smooth, --smooth-radius <float>, --hard, --no-defringe, --bg <color>, --no-recursive");
        Console.WriteLine("DDS Options:   --preset <BC7|BC1|BC1a|BC3|BC5>, --no-mips, --no-recursive");
        Console.WriteLine("Upscale Flags: --scale <2|3|4>, --model <name>, --alpha-mode <ai|lanczos|none>, --gpu <id>, --no-recursive");
        Console.WriteLine("DTE Modes:     merge_rgba, merge_bgra, split_tonemaps, extract, unpack, custom");
        Console.WriteLine("DTE Flags:     --tasks <n>, --cleanup <mode>, --no-recursive, --mips, --no-dmd, --custom-cmd <verb>, --extra <flags>");
    }

    private static async Task<int> RunFetchRealEsrganAsync()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WARNING] The upstream Real-ESRGAN release only ships x86_64 Linux binaries.");
            Console.WriteLine("          On other architectures, build realesrgan-ncnn-vulkan from source and put it on PATH.");
            Console.ResetColor();
        }

        var lastPct = -1;
        var progressLineOpen = false;
        var progress = new SyncProgress<ProcessingProgress>(p =>
        {
            if (p.Percentage == lastPct) return;
            lastPct = p.Percentage;
            Console.Write($"\r  {p.Message}   ");
            progressLineOpen = true;
        });

        // Always report download/extraction status, even in quiet mode.
        var syncLog = new SyncProgress<LogEntry>(e =>
        {
            if (progressLineOpen)
            {
                Console.WriteLine();
                progressLineOpen = false;
            }

            Console.ForegroundColor = e.Level switch
            {
                LogLevel.Success => ConsoleColor.Green,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };
            Console.WriteLine(e.Message);
            Console.ResetColor();
        });

        var downloader = new ToolDownloader();
        var ok = await downloader.DownloadRealEsrganAsync(progress, syncLog);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// IProgress implementation that invokes the handler synchronously (keeps console output ordered).
    /// </summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 < args.Length)
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static async Task<int> RunAlphaAsync(string[] args, IEnvironmentDetector env, IProcessRunner runner, IProgress<LogEntry>? log)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: delphis-cli alpha <inputPath> <outputPath> [--alpha <val>] [--remove-alpha] [--smooth] [--smooth-radius <r>] [--hard] [--no-defringe] [--bg <color>] [--no-recursive]");
            return 1;
        }

        var input = args[0];
        var output = args[1];
        byte alpha = 255;
        bool remove = false;
        bool smooth = false;
        double smoothRadius = 1.0;
        bool scaleAlpha = true;
        bool defringe = true;
        RgbColor? bgColor = null;
        bool recursive = true;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha":
                    if (!TryReadValue(args, ref i, out var alphaRaw) || !byte.TryParse(alphaRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out alpha))
                    {
                        Console.Error.WriteLine("[ERROR] --alpha requires an integer between 0 and 255.");
                        return 1;
                    }
                    break;
                case "--remove-alpha":
                    remove = true;
                    break;
                case "--smooth":
                    smooth = true;
                    break;
                case "--smooth-radius":
                    if (!TryReadValue(args, ref i, out var radRaw) || !double.TryParse(radRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out smoothRadius) || smoothRadius < 0)
                    {
                        Console.Error.WriteLine("[ERROR] --smooth-radius requires a non-negative number.");
                        return 1;
                    }
                    break;
                case "--hard":
                    scaleAlpha = false;
                    break;
                case "--no-defringe":
                    defringe = false;
                    break;
                case "--bg":
                    if (!TryReadValue(args, ref i, out var bgRaw))
                    {
                        Console.Error.WriteLine("[ERROR] --bg requires a color value (e.g. white, #ffffff).");
                        return 1;
                    }
                    bgColor = AlphaProcessor.ParseColor(bgRaw);
                    if (bgColor is null)
                    {
                        Console.Error.WriteLine($"[ERROR] Invalid --bg color: '{bgRaw}'.");
                        return 1;
                    }
                    break;
                case "--no-recursive":
                    recursive = false;
                    break;
            }
        }

        if (!File.Exists(input) && !Directory.Exists(input))
        {
            Console.Error.WriteLine($"[ERROR] Input path does not exist: '{input}'.");
            return 1;
        }

        var processor = new AlphaProcessor(runner, env);
        var options = new AlphaOptions(
            RemoveAlpha: remove,
            AlphaValue: alpha,
            ScaleAlpha: scaleAlpha,
            SmoothEdges: smooth,
            SmoothRadius: smoothRadius,
            Defringe: defringe,
            BackgroundColor: bgColor,
            Recursive: recursive
        );

        if (File.Exists(input))
        {
            log?.Report(new LogEntry($"Alpha Processor: Processing single image '{Path.GetFileName(input)}'...", LogLevel.Info));
            var ok = await processor.ProcessSingleImageAsync(input, output, options);
            if (ok) log?.Report(new LogEntry($"[OK] Processed {Path.GetFileName(input)}", LogLevel.Success));
            else log?.Report(new LogEntry($"[ERROR] Failed processing {Path.GetFileName(input)}", LogLevel.Error));
            return ok ? 0 : 1;
        }

        var count = await processor.ProcessBatchAsync(input, output, options, null, log);
        return count > 0 ? 0 : 1;
    }

    private static async Task<int> RunDdsAsync(string[] args, IEnvironmentDetector env, IProcessRunner runner, IProgress<LogEntry>? log)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: delphis-cli dds <inputPath> <outputPath> [--preset <BC7|BC1|BC1a|BC3|BC5>] [--no-mips] [--no-recursive]");
            return 1;
        }

        var input = args[0];
        var output = args[1];
        string preset = "BC7";
        bool mips = true;
        bool recursive = true;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset":
                    if (!TryReadValue(args, ref i, out preset))
                    {
                        Console.Error.WriteLine("[ERROR] --preset requires a value (BC7, BC1, BC1a, BC3, BC5).");
                        return 1;
                    }
                    break;
                case "--no-mips":
                    mips = false;
                    break;
                case "--no-recursive":
                    recursive = false;
                    break;
            }
        }

        if (!File.Exists(input) && !Directory.Exists(input))
        {
            Console.Error.WriteLine($"[ERROR] Input path does not exist: '{input}'.");
            return 1;
        }

        var optimizer = new DdsOptimizer(runner, env);
        var options = new DdsOptions(Preset: preset, GenerateMipmaps: mips, Recursive: recursive);

        if (File.Exists(input))
        {
            log?.Report(new LogEntry($"DDS Optimizer: Converting single image '{Path.GetFileName(input)}' [Preset: {preset}]...", LogLevel.Info));
            var ok = await optimizer.ConvertSingleAsync(input, output, options);
            if (ok) log?.Report(new LogEntry($"[OK] Converted {Path.GetFileName(input)}", LogLevel.Success));
            else log?.Report(new LogEntry($"[ERROR] Failed converting {Path.GetFileName(input)}", LogLevel.Error));
            return ok ? 0 : 1;
        }

        var count = await optimizer.ConvertBatchAsync(input, output, options, null, log);
        return count > 0 ? 0 : 1;
    }

    private static async Task<int> RunUpscaleAsync(string[] args, IEnvironmentDetector env, IProcessRunner runner, IProgress<LogEntry>? log)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: delphis-cli upscale <inputPath> <outputPath> [--scale <2|3|4>] [--model <name>] [--alpha-mode <ai|lanczos|none>] [--gpu <id>] [--no-recursive]");
            return 1;
        }

        var input = args[0];
        var output = args[1];
        int scale = 4;
        string model = "realesrgan-x4plus-anime";
        string alphaMode = "ai";
        int? gpuId = null;
        bool recursive = true;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scale":
                    if (!TryReadValue(args, ref i, out var scaleRaw) || !int.TryParse(scaleRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) || !DelphisConstants.ScaleFactors.Contains(scale))
                    {
                        Console.Error.WriteLine("[ERROR] --scale must be one of: 2, 3, 4.");
                        return 1;
                    }
                    break;
                case "--model":
                    if (!TryReadValue(args, ref i, out model))
                    {
                        Console.Error.WriteLine("[ERROR] --model requires a model name.");
                        return 1;
                    }
                    break;
                case "--alpha-mode":
                    if (!TryReadValue(args, ref i, out alphaMode))
                    {
                        Console.Error.WriteLine("[ERROR] --alpha-mode requires ai, lanczos, or none.");
                        return 1;
                    }
                    break;
                case "--gpu":
                    if (!TryReadValue(args, ref i, out var gpuRaw) || !int.TryParse(gpuRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedGpu))
                    {
                        Console.Error.WriteLine("[ERROR] --gpu requires an integer device ID.");
                        return 1;
                    }
                    gpuId = parsedGpu;
                    break;
                case "--no-recursive":
                    recursive = false;
                    break;
            }
        }

        if (!File.Exists(input) && !Directory.Exists(input))
        {
            Console.Error.WriteLine($"[ERROR] Input path does not exist: '{input}'.");
            return 1;
        }

        var upscaler = new UpscalerEngine(runner, env);
        var options = new UpscalerOptions(
            Scale: scale,
            Model: model,
            AlphaMode: alphaMode,
            GpuId: gpuId ?? -1,
            Recursive: recursive
        );

        if (File.Exists(input))
        {
            log?.Report(new LogEntry($"AI Upscaler: Upscaling single file '{Path.GetFileName(input)}' [Scale: {scale}x | Model: {model}]...", LogLevel.Info));
            var ok = await upscaler.UpscaleSingleAsync(input, output, options);
            if (ok) log?.Report(new LogEntry($"[OK] Upscaled {Path.GetFileName(input)}", LogLevel.Success));
            else log?.Report(new LogEntry($"[ERROR] Failed upscaling {Path.GetFileName(input)}", LogLevel.Error));
            return ok ? 0 : 1;
        }

        var count = await upscaler.UpscaleBatchAsync(input, output, options, null, log);
        return count > 0 ? 0 : 1;
    }

    private static async Task<int> RunDteAsync(string[] args, IEnvironmentDetector env, IProcessRunner runner, IProgress<LogEntry>? log, bool defaultDisableLogFile = false)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: delphis-cli dte <merge_rgba|merge_bgra|split_tonemaps|extract|unpack|custom> <in> <out> [--tasks <n>] [--cleanup <mode>] [--no-recursive] [--mips] [--no-dmd] [--custom-cmd <verb>] [--extra <flags>]");
            return 1;
        }

        var mode = args[0];
        var input = args[1];
        var output = args[2];
        int tasks = 1;
        string cleanup = "none";
        bool recursive = true;
        bool mips = false;
        bool dmd = true;
        string? customCmd = null;
        string? extra = null;
        bool disableLogFile = defaultDisableLogFile;

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--no-logfile" or "--no-log" or "--no-logs":
                    disableLogFile = true;
                    break;
                case "--logfile" or "--keep-logfile" or "--with-logfile":
                    disableLogFile = false;
                    break;
                case "--tasks" or "-Tasks":
                    if (!TryReadValue(args, ref i, out var tasksRaw) || !int.TryParse(tasksRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out tasks) || tasks < 1)
                    {
                        Console.Error.WriteLine("[ERROR] --tasks requires a positive integer.");
                        return 1;
                    }
                    break;
                case "--cleanup":
                    if (!TryReadValue(args, ref i, out cleanup))
                    {
                        Console.Error.WriteLine("[ERROR] --cleanup requires a mode (none, default, simple, compact).");
                        return 1;
                    }
                    break;
                case "--no-recursive":
                    recursive = false;
                    break;
                case "--mips" or "-Mip":
                    mips = true;
                    break;
                case "--no-dmd":
                    dmd = false;
                    break;
                case "--custom-cmd":
                    if (!TryReadValue(args, ref i, out customCmd))
                    {
                        Console.Error.WriteLine("[ERROR] --custom-cmd requires a command verb.");
                        return 1;
                    }
                    break;
                case "--extra":
                    if (!TryReadValue(args, ref i, out extra))
                    {
                        Console.Error.WriteLine("[ERROR] --extra requires flags text.");
                        return 1;
                    }
                    break;
            }
        }

        var orchestrator = new DteOrchestrator(runner, env);
        var options = new DteOptions(
            Mode: mode,
            Tasks: tasks,
            CleanupMode: cleanup,
            Recursive: recursive,
            ExtractMipmaps: mips,
            DolphinMipDetection: dmd,
            CustomCommand: customCmd ?? "Extract",
            ExtraArgs: extra ?? string.Empty,
            DisableLogFile: disableLogFile
        );

        var res = await orchestrator.ExecuteAsync(input, output, options, log);
        return res.IsSuccess ? 0 : res.ExitCode;
    }

    private static int HandleUnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown command: '{cmd}'. Run 'delphis-cli --help' for usage.");
        Console.ResetColor();
        return 1;
    }
}

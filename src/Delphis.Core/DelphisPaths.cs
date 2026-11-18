namespace Delphis.Core;

/// <summary>
/// Distribution-agnostic filesystem locations used by Delphis.
/// Follows the XDG Base Directory specification and never assumes a
/// particular Linux distribution or package manager layout.
/// </summary>
public static class DelphisPaths
{
    /// <summary>
    /// Environment variable that overrides the per-user data directory.
    /// </summary>
    public const string DataDirEnvVar = "DELPHIS_DATA_DIR";

    /// <summary>
    /// Environment variable holding extra directories (colon separated)
    /// searched for external tools before PATH.
    /// </summary>
    public const string ToolsPathEnvVar = "DELPHIS_TOOLS_PATH";

    /// <summary>
    /// The user's home directory ($HOME), without requiring it to exist.
    /// </summary>
    public static string Home
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? "/";
            }
            return home;
        }
    }

    /// <summary>
    /// Per-user data directory:
    /// $DELPHIS_DATA_DIR, else $XDG_DATA_HOME/delphis, else ~/.local/share/delphis.
    /// </summary>
    public static string UserDataDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable(DataDirEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return overrideDir;
            }

            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathRooted(xdgDataHome)
                ? xdgDataHome
                : Path.Combine(Home, ".local", "share");

            return Path.Combine(baseDir, "delphis");
        }
    }

    /// <summary>
    /// Per-user directory for tools downloaded on demand (e.g. Real-ESRGAN).
    /// </summary>
    public static string UserToolsDir => Path.Combine(UserDataDir, "bin");

    /// <summary>
    /// Per-user directory for neural network models.
    /// </summary>
    public static string UserModelsDir => Path.Combine(UserDataDir, "models");

    /// <summary>
    /// Extra tool directories from $DELPHIS_TOOLS_PATH.
    /// </summary>
    public static IReadOnlyList<string> ExtraToolDirs
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(ToolsPathEnvVar);
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>
    /// Directories that belong to the Delphis installation itself
    /// (e.g. &lt;prefix&gt;/lib/delphis/cli and &lt;prefix&gt;/bin when installed
    /// with <c>make install</c>), so bundled tools such as <c>dte</c> are found
    /// even when the install prefix is not on PATH.
    /// </summary>
    public static IReadOnlyList<string> InstallToolDirs
    {
        get
        {
            var dirs = new List<string>();
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(baseDir))
            {
                return dirs;
            }

            dirs.Add(baseDir);

            // <prefix>/lib/delphis/cli -> <prefix>/lib/delphis/dte and <prefix>/bin
            // (also covers <repo>/build/cli -> <repo>/build/bin and <repo>/build/dte)
            var libDelphis = Path.GetDirectoryName(baseDir);
            if (libDelphis is not null)
            {
                dirs.Add(Path.Combine(libDelphis, "bin"));
                dirs.Add(Path.Combine(libDelphis, "dte"));

                var libDir = Path.GetDirectoryName(libDelphis);
                var prefix = libDir is null ? null : Path.GetDirectoryName(libDir);
                if (prefix is not null)
                {
                    dirs.Add(Path.Combine(prefix, "bin"));
                }
            }

            return dirs;
        }
    }
}

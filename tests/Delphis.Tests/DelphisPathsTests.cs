using Delphis.Core;
using Delphis.Core.Services;

namespace Delphis.Tests;

public class DelphisPathsTests
{
    private static void WithEnv(IDictionary<string, string?> vars, Action body)
    {
        var previous = vars.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (k, v) in vars) Environment.SetEnvironmentVariable(k, v);
            body();
        }
        finally
        {
            foreach (var (k, v) in previous) Environment.SetEnvironmentVariable(k, v);
        }
    }

    [Fact]
    public void UserDataDir_ShouldHonorXdgDataHome()
    {
        WithEnv(new Dictionary<string, string?>
        {
            [DelphisPaths.DataDirEnvVar] = null,
            ["XDG_DATA_HOME"] = "/tmp/xdg-data"
        }, () =>
        {
            Assert.Equal("/tmp/xdg-data/delphis", DelphisPaths.UserDataDir);
            Assert.Equal("/tmp/xdg-data/delphis/bin", DelphisPaths.UserToolsDir);
        });
    }

    [Fact]
    public void UserDataDir_ShouldFallBackToLocalShare()
    {
        WithEnv(new Dictionary<string, string?>
        {
            [DelphisPaths.DataDirEnvVar] = null,
            ["XDG_DATA_HOME"] = null
        }, () =>
        {
            Assert.Equal(Path.Combine(DelphisPaths.Home, ".local", "share", "delphis"), DelphisPaths.UserDataDir);
        });
    }

    [Fact]
    public void UserDataDir_ExplicitOverrideWins()
    {
        WithEnv(new Dictionary<string, string?>
        {
            [DelphisPaths.DataDirEnvVar] = "/opt/custom-delphis",
            ["XDG_DATA_HOME"] = "/tmp/xdg-data"
        }, () =>
        {
            Assert.Equal("/opt/custom-delphis", DelphisPaths.UserDataDir);
        });
    }

    [Fact]
    public void SearchDirectories_ShouldPutToolsPathBeforePath()
    {
        WithEnv(new Dictionary<string, string?>
        {
            [DelphisPaths.ToolsPathEnvVar] = "/opt/tools-a:/opt/tools-b",
            ["PATH"] = "/usr/bin:/bin"
        }, () =>
        {
            var dirs = EnvironmentDetector.GetSearchDirectories();
            Assert.Equal("/opt/tools-a", dirs[0]);
            Assert.Equal("/opt/tools-b", dirs[1]);
            Assert.Equal("/usr/bin", dirs[2]);
            Assert.Contains("/usr/local/bin", dirs);
            Assert.Equal(dirs.Count, dirs.Distinct().Count());
        });
    }

    [Fact]
    public void FindExecutable_ShouldFindToolInToolsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"delphis_tools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var tool = Path.Combine(dir, "delphis-fake-tool");
        File.WriteAllText(tool, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var notExecutable = Path.Combine(dir, "delphis-not-executable");
        File.WriteAllText(notExecutable, "data");

        try
        {
            WithEnv(new Dictionary<string, string?> { [DelphisPaths.ToolsPathEnvVar] = dir }, () =>
            {
                var env = new EnvironmentDetector();
                Assert.Equal(tool, env.FindExecutable("delphis-fake-tool"));
                Assert.Null(env.FindExecutable("delphis-not-executable"));
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

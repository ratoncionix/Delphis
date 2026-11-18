"""
Locates the Delphis C# CLI backend (delphis-cli) without importing Qt.

Works for every supported layout:
  * system / user install via `make install` (<prefix>/bin/delphis-cli)
  * Nix / NixOS package (wrapper on PATH)
  * a source checkout (build/ output, bin/Release|Debug, or `dotnet run`)
"""

from __future__ import annotations

import os
import shutil
from pathlib import Path

# Commands handled by delphis-cli. `delphis <command> ...` forwards to the CLI.
CLI_COMMANDS = {
    "alpha", "dds", "upscale", "dte", "fetch-realesrgan",
    "help", "-h", "--help", "version", "-v", "--version",
}

_PACKAGE_DIR = Path(__file__).resolve().parent


def _repo_root() -> Path | None:
    """Return the source checkout root when running from a git clone."""
    candidate = _PACKAGE_DIR.parent.parent  # <repo>/src/delphis_gui -> <repo>
    if (candidate / "src" / "Delphis.Cli" / "Delphis.Cli.csproj").is_file():
        return candidate
    return None


def _is_executable(path: Path) -> bool:
    return path.is_file() and os.access(path, os.X_OK)


def find_delphis_cli_command() -> list[str]:
    """
    Return the argv prefix used to invoke the C# backend, in priority order:
      1. $DELPHIS_CLI (explicit override)
      2. delphis-cli on PATH (installed launcher or Nix wrapper)
      3. Launcher installed next to this package (<prefix>/bin/delphis-cli)
      4. Build outputs inside a source checkout
      5. `dotnet run` against the source checkout (development fallback)
    """
    override = os.environ.get("DELPHIS_CLI", "").strip()
    if override:
        return [override]

    on_path = shutil.which("delphis-cli")
    if on_path:
        return [on_path]

    # <prefix>/lib/delphis/python/delphis_gui -> <prefix>/bin/delphis-cli
    try:
        prefix = _PACKAGE_DIR.parents[3]
        installed = prefix / "bin" / "delphis-cli"
        if _is_executable(installed):
            return [str(installed)]
    except IndexError:
        pass

    repo = _repo_root()
    if repo is not None:
        dev_launcher = repo / "build" / "bin" / "delphis-cli"   # created by `make`
        if _is_executable(dev_launcher):
            return [str(dev_launcher)]

        dotnet = shutil.which("dotnet")
        build_dirs = [
            repo / "build" / "cli",
            repo / "src" / "Delphis.Cli" / "bin" / "Release" / "net8.0",
            repo / "src" / "Delphis.Cli" / "bin" / "Debug" / "net8.0",
        ]
        for build_dir in build_dirs:
            dll = build_dir / "delphis-cli.dll"
            apphost = build_dir / "delphis-cli"
            # Prefer `dotnet <dll>`: the native apphost cannot run on systems
            # without an FHS dynamic loader (e.g. NixOS without nix-ld).
            if dotnet and dll.is_file():
                return [dotnet, str(dll)]
            if _is_executable(apphost):
                return [str(apphost)]

        nix_result = repo / "result" / "bin" / "delphis-cli"
        if _is_executable(nix_result):
            return [str(nix_result)]

        if dotnet:
            project = repo / "src" / "Delphis.Cli" / "Delphis.Cli.csproj"
            return [dotnet, "run", "--project", str(project), "--"]

    # Last resort: let the OS report a clear "not found" error.
    return ["delphis-cli"]


def find_delphis_cli() -> str:
    """Backwards-compatible helper returning a single executable path."""
    return find_delphis_cli_command()[0]

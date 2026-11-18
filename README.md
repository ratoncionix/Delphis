# Delphis

Texture processing suite and Qt6 desktop interface for **Dolphin Emulator** (GameCube and Wii) high-resolution texture packs.

[![Linux](https://img.shields.io/badge/Linux-any%20distribution-FCC624?logo=linux&logoColor=black)](#building-and-installing)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
[![Nix Flake](https://img.shields.io/badge/Nix-Flake%20included-5277C3?logo=nixos&logoColor=white)](#nix--nixos)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Delphis runs on any Linux distribution via GNU Make, and ships a Nix flake for reproducible NixOS packages and local development.

---

## Features

- **Extraction and tonemaps (`dte`)**: Dumps textures from GameCube/Wii disc images and archives (`.iso`, `.rvz`, `.wbfs`, `.gcm`, `.arc`, `.szs`, etc.), combines Dolphin grayscale tonemap pairs (`CombineRGBA` / `CombineBGRA`), and exports edited textures back to split tonemaps (`Finalize`).
- **Alpha processing (`alpha`)**: Proportional sub-pixel opacity scaling (`0`–`255`), alpha removal with optional background matte, RGB halo defringing, and Gaussian edge smoothing.
- **AI upscaling (`upscale`)**: Two-layer RGB + Alpha separation via Real-ESRGAN NCNN Vulkan (`realesrgan-x4plus-anime`, `realesrgan-x4plus`, `realesr-animevideov3`) at `2x`, `3x`, or `4x`, with 1-pass RGB mode for opaque images or `--alpha-mode none` and exact `(Width * scale) x (Height * scale)` alignment. On x86_64 Linux, Real-ESRGAN and its neural models are downloaded automatically into the user data directory on first use if not already present on `PATH`.
- **DDS optimization (`dds`)**: Batch converts textures to `.dds` (`BC7`, `BC1`, `BC1a`, `BC3`, `BC5`) with mipmap generation using `nvcompress`, `texconv`, `compressonatorcli`, or ImageMagick as a fallback.

---

## Requirements

### Build and core runtime

| Component | Version | Notes |
|-----------|---------|-------|
| .NET SDK | 8.0 or newer | Required to build (`make`). Installed binaries roll forward automatically to newer .NET runtimes. |
| GNU Make, `sed`, `install`, POSIX `sh` | any | Required for building and installing. |
| Python 3 + PyQt6 | Python >= 3.9 | Required for the GUI only (`delphis-cli` has no Python dependency). |

### External runtime tools

| Tool | Executable | Role | Provisioning |
|------|------------|------|--------------|
| DolphinTextureExtraction-tool | `dte` | Extraction and tonemaps | Built and installed by `make`. |
| Real-ESRGAN NCNN Vulkan | `realesrgan-ncnn-vulkan` | AI upscaling | Downloaded automatically on x86_64 Linux on first upscale if not already installed. |
| ImageMagick | `magick` or `convert` | Alpha processing, upscaling, DDS fallback | Install from your distribution package manager. |
| NVIDIA Texture Tools | `nvcompress` | BC7 / BC1 / BC3 / BC5 DDS encoding | Optional (`texconv`, `compressonatorcli`, or ImageMagick are used if absent). |
| Vulkan loader and GPU driver | `libvulkan.so.1` | Vulkan inference | Required for GPU upscaling (`vulkaninfo` is optional for GPU listing). |
| 7-Zip shared library | `7z.so` | `.7z` archives inside DTE | Optional; auto-detected when present. |

### Distribution packages

**Debian / Ubuntu**

```bash
# Ubuntu 22.04+ includes dotnet-sdk-8.0. On Debian, configure Microsoft's APT repo first.
sudo apt install dotnet-sdk-8.0 make python3 python3-pyqt6 imagemagick libvulkan1 vulkan-tools
# Optional: libnvtt-bin, 7zip
```

**Fedora**

```bash
sudo dnf install dotnet-sdk-8.0 make python3 python3-pyqt6 ImageMagick vulkan-loader vulkan-tools
# Optional: p7zip-plugins
```

**Arch Linux**

```bash
sudo pacman -S --needed dotnet-sdk-8.0 make python python-pyqt6 imagemagick vulkan-icd-loader vulkan-tools
# Optional: 7zip; realesrgan-ncnn-vulkan and nvidia-texture-tools are available in the AUR
```

**openSUSE**

```bash
sudo zypper install make python3 python3-PyQt6 ImageMagick libvulkan1 vulkan-tools
# Install .NET 8 SDK from Microsoft's repository or the install script below
```

**No root / fallback installers**

```bash
# .NET 8 SDK into ~/.dotnet (detected automatically by Makefile and launchers)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0

# PyQt6 in a user virtual environment (detected automatically by the delphis launcher)
python3 -m venv ~/.local/share/delphis/venv
~/.local/share/delphis/venv/bin/pip install -r requirements.txt
```

---

## Building and installing

```bash
git clone https://github.com/ratoncionix/Delphis.git
cd Delphis
make

# Per-user install (no root required)
make install PREFIX=~/.local

# Or system-wide install
sudo make install
```

Add `~/.local/bin` to `PATH` if it is not already present.

### Make targets and variables

| Target | Description |
|--------|-------------|
| `make` | Build `delphis-cli`, bundled `dte`, and local launchers into `build/`. |
| `make test` | Run the C# test suite and Python/PyQt6 checks. |
| `make run` | Launch the GUI directly from the source tree. |
| `make install` | Install binaries, assemblies, Python package, desktop entry, and icon into `$(DESTDIR)$(PREFIX)`. |
| `make uninstall` | Remove files installed under `$(DESTDIR)$(PREFIX)`. |
| `make clean` | Remove `build/`. |

| Variable | Default | Description |
|----------|---------|-------------|
| `PREFIX` | `/usr/local` | Installation prefix (`~/.local`, `/usr`, etc.). |
| `DESTDIR` | *(empty)* | Staging root for distribution packagers. |
| `BINDIR` | `$(PREFIX)/bin` | Launcher install directory. |
| `LIBDIR` | `$(PREFIX)/lib/delphis` | Assemblies and Python package directory. |
| `DATADIR` | `$(PREFIX)/share` | Desktop entry and icon base directory. |
| `PYTHON` | `python3` | Python interpreter baked into `delphis`. |
| `DOTNET` | auto (`dotnet` or `~/.dotnet/dotnet`) | .NET SDK used to build. |
| `DOTNET_HOST` | *(empty)* | Explicit `dotnet` binary path baked into launchers. |
| `WITH_DTE` | `1` | Set to `0` to skip building bundled `dte`. |
| `SELF_CONTAINED` | `0` | Set to `1` to bundle the .NET runtime for `RID`. |
| `RID` | auto (`linux-x64`, `linux-arm64`, `linux-musl-x64`, ...) | Target runtime identifier when `SELF_CONTAINED=1`. |
| `SEVENZIP_LIB` | auto-detected `7z.so` | Shared library symlinked for DTE `.7z` support. |

### Installed layout

```text
$PREFIX/bin/delphis                         # GUI launcher (also forwards CLI commands)
$PREFIX/bin/delphis-cli                     # C# CLI backend launcher
$PREFIX/bin/dte                             # Bundled DolphinTextureExtraction-tool launcher
$PREFIX/lib/delphis/cli/                    # delphis-cli assemblies
$PREFIX/lib/delphis/dte/                    # DTE assemblies
$PREFIX/lib/delphis/python/delphis_gui/     # Qt6 GUI package
$PREFIX/share/applications/delphis.desktop
$PREFIX/share/icons/hicolor/scalable/apps/delphis.svg
```

---

## Usage

### Graphical interface

Launch **Delphis** from your application menu or run `delphis`.

### Command-line interface

`delphis-cli` exposes every backend operation. `delphis <command>` forwards directly to `delphis-cli` without loading Qt.

```bash
delphis-cli --help
delphis-cli alpha   in.png out.png --alpha 130 --smooth
delphis-cli dds     in_dir out_dir --preset BC7
delphis-cli upscale in.png out.png --scale 4 --model realesrgan-x4plus-anime
delphis-cli dte     merge_rgba /path/to/dump /path/to/merged --tasks 4 --no-logfile
```

DTE writes `.log` files next to outputs only when `--logfile` is enabled (or **Generate DTE Log File** is checked in the GUI). Passing `--no-logfile`, `--no-log`, or `--no-logs` removes generated `DTE-*.log` files on completion while keeping live console output (unless `-q` / `--quiet` / `--no-logs` is passed).

### Tool resolution and environment variables

External binaries are searched in order: `$DELPHIS_TOOLS_PATH`, `$PATH`, the Delphis installation directory, `$XDG_DATA_HOME/delphis/bin` (`~/.local/share/delphis/bin`), and standard system/Nix profile paths (`~/.local/bin`, `/usr/local/bin`, `/usr/bin`, `/bin`, `/opt/delphis/bin`, `~/.nix-profile/bin`, `/run/current-system/sw/bin`).

| Variable | Purpose |
|----------|---------|
| `DELPHIS_TOOLS_PATH` | Colon-separated extra directories searched before `PATH`. |
| `DELPHIS_DATA_DIR` | Override per-user data directory (default `$XDG_DATA_HOME/delphis`). |
| `DELPHIS_CLI` | Explicit path to `delphis-cli` used by the GUI. |
| `DELPHIS_PYTHON` | Python interpreter used by the `delphis` launcher. |
| `DELPHIS_DOTNET` | `dotnet` host used by the `.NET` launchers. |

---

## Nix / NixOS

The repository includes a Nix flake that wraps `Makefile` (`WITH_DTE=0`) and pins all external runtime dependencies (`dte`, `realesrgan-ncnn-vulkan`, `imagemagick`, `nvidia-texture-tools`, `vulkan-tools`, `vulkan-loader`, and Qt6/KDE Breeze integration).

### Direct execution

```bash
nix run github:ratoncionix/Delphis             # GUI
nix run github:ratoncionix/Delphis -- --help   # CLI help
nix run github:ratoncionix/Delphis#dte -- HELP # DTE CLI
nix run github:ratoncionix/Delphis#test        # Automated test suite
```

### NixOS module

```nix
{
  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs/nixos-unstable";
    delphis.url = "github:ratoncionix/Delphis";
  };

  outputs = { nixpkgs, delphis, ... }: {
    nixosConfigurations.myhostname = nixpkgs.lib.nixosSystem {
      system = "x86_64-linux";
      modules = [
        delphis.nixosModules.default
        { programs.delphis.enable = true; }
      ];
    };
  };
}
```

### Home Manager module and overlay

```nix
{
  imports = [ inputs.delphis.homeManagerModules.default ];
  programs.delphis.enable = true;
}
```

`delphis.overlays.default` provides `pkgs.delphis` and `pkgs.dolphin-texture-extraction-tool` (`pkgs.dte`). Non-flake users can use `nix-build` (`default.nix`) and `nix-shell` (`shell.nix`).

---

## Development

On NixOS, `nix develop` provides `.NET 8 SDK`, `gnumake`, Python with `PyQt6`, Qt6/Breeze plugins, and runtime tools:

```bash
nix develop       # or install build requirements from your distribution
make WITH_DTE=0   # build delphis-cli (dte is already on PATH inside nix develop)
make test         # run C# and Python/PyQt6 test suites
make run          # launch GUI from source tree
```

Flake packaging checks:

```bash
nix build && ./result/bin/delphis --version
nix flake check
```

### Project layout

```text
Delphis/
├── AGENTS.md                           # AI agent guidelines (CLAUDE.md / GEMINI.md -> @AGENTS.md)
├── Makefile                            # Distribution-agnostic build & install
├── requirements.txt                    # Python GUI dependency list
├── VERSION                             # Canonical project version
├── Delphis.sln                         # .NET solution
├── Directory.Build.props               # Shared MSBuild version & Major roll-forward
├── packaging/
│   ├── dotnet-launcher.sh.in           # Launcher template for delphis-cli / dte
│   ├── delphis.sh.in                   # Launcher template for delphis GUI
│   └── delphis.desktop                 # XDG desktop entry
├── src/
│   ├── Delphis.Core/                   # Core processing library (.NET 8 BCL only)
│   ├── Delphis.Cli/                    # delphis-cli entrypoint
│   └── delphis_gui/                    # Thin PyQt6 GUI frontend
├── DolphinTextureExtraction/           # Bundled upstream DTE CLI
├── lib/                                # Upstream AuroraLib, Hack.io, LibCPK, AFSLib
├── tests/
│   ├── Delphis.Tests/                  # Automated C# test suite
│   └── test_gui.py                     # Automated Python / PyQt6 test suite
├── assets/icons/delphis.svg
├── flake.nix / flake.lock              # Nix flake (packages, modules, devShell)
├── default.nix / shell.nix             # flake-compat entrypoints
└── nix/
    ├── delphis.nix                     # Nix derivation wrapping Makefile
    ├── dte.nix                         # DTE derivation with pinned NuGet deps
    ├── deps.json                       # Pinned NuGet lockfile for dte.nix
    ├── overlay.nix                     # Nixpkgs overlay
    ├── nixos-module.nix                # NixOS module
    └── home-manager-module.nix         # Home Manager module
```

---

## License

MIT License. See [LICENSE](LICENSE) for license text and [CREDITS.md](CREDITS.md) for upstream library and tool attributions.

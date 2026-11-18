# AGENTS.md

Guidelines for AI agents (Claude Code, opencode, Antigravity/Gemini, Codex, etc.) working on **Delphis**.
This file defines **scope boundaries, invariants, and conventions** grounded in the current repository. Always inspect the relevant files before making assumptions.

---

## 1. Language policy (mandatory)

- **Everything written into this repository is in English**: C# code, Python code, Nix expressions, Makefile rules, comments, docstrings, CLI help text, UI strings, Markdown documentation, and commit messages.
- This applies even when the user communicates in another language (e.g. Portuguese). Reply to the user in their language, while keeping all repository content in English.
- If you edit a file with non-English comments or strings, translate them to English as part of the change.
- Keep comments concise and explain **why** (intent, constraints, platform quirks), not what the code obviously does.

---

## 2. Anti-hallucination rules (mandatory)

Never guess symbols, CLI flags, package names, or build behavior. Verify against the files in this repository:

1. **Local facts beat memory**: read the relevant source file before editing or describing behavior.
2. **Do not invent CLI flags, modes, models, presets, or environment variables**:
   - CLI commands & flags: `src/Delphis.Cli/Program.cs` and `src/delphis_gui/cli_locator.py` (`CLI_COMMANDS`).
   - Constants, DDS presets, AI models, scale factors, alpha presets: `src/Delphis.Core/DelphisConstants.cs` and `src/delphis_gui/constants.py`.
   - Paths & environment overrides: `src/Delphis.Core/DelphisPaths.cs` and `src/Delphis.Core/Services/EnvironmentDetector.cs`.
3. **Do not invent NuGet packages or test frameworks**:
   - `Delphis.Core`, `Delphis.Cli`, and `Delphis.Tests` use **only the .NET 8 BCL** (zero `<PackageReference>` entries). Do not add NuGet dependencies to those projects without an explicit user request (they would also break offline Nix builds of `nix/delphis.nix`).
   - `tests/Delphis.Tests/` uses a **custom lightweight runner** (`TestFramework.cs` + `Program.cs`) with `[Fact]` and `Assert.*`, executed via `dotnet run --project tests/Delphis.Tests/Delphis.Tests.csproj` (or `make test`). It does **not** use the `xunit` NuGet package or `dotnet test` VSTest discovery.
4. **Never invent hashes**: do not fabricate NuGet hashes (`nix/deps.json`) or Nix hashes. If `DolphinTextureExtraction/` or `lib/` NuGet deps ever change, regenerate `nix/deps.json` properly.
5. **No hardcoded `/nix/store/...` or distro-specific assumptions** in application code (`src/`, `Makefile`, `packaging/`).
6. **Report only observed results**: never claim tests, builds, or `nix flake check` passed unless you actually ran them after your final edit and observed them pass.

---

## 3. Project scope & architecture

**Delphis** is a **distribution-agnostic Linux** texture manipulation suite for the **Dolphin Emulator** (GameCube & Wii), with an optional **Nix flake** for NixOS users and local development (`nix develop`).

### Core components

| Path | Role | Scope / Rules |
|------|------|---------------|
| `src/Delphis.Core/` | C# (.NET 8) core library | **Single source of truth** for all image/texture processing logic (`AlphaProcessor`, `DdsOptimizer`, `UpscalerEngine`, `DteOrchestrator`, `EnvironmentDetector`, `ToolDownloader`, `ProcessRunner`, `DelphisPaths`, `DelphisConstants`). |
| `src/Delphis.Cli/` | C# (.NET 8) CLI (`delphis-cli`) | Entrypoint parsing CLI commands (`alpha`, `dds`, `upscale`, `dte`, `fetch-realesrgan`, `help`, `version`) and delegating to `Delphis.Core`. |
| `src/delphis_gui/` | Python 3 + PyQt6 desktop GUI (`delphis`) | **Thin UI layer only.** Delegates work to `delphis-cli` asynchronously via `CSharpCommandWorker` (`src/delphis_gui/core/bridge.py`). Also forwards `delphis <cli-command>` to `delphis-cli` before importing PyQt6. **Never reimplement texture/image logic in Python.** |
| `DolphinTextureExtraction/` & `lib/` | Bundled upstream DTE (`dte`) + libraries (`AuroraLib`, `Hack.io`, `LibCPK`, `AFSLib`) | Upstream .NET 8 codebase wrapped by `DteOrchestrator`. **Do not modify** unless fixing a verified DTE/library issue. |
| `Makefile` & `packaging/` | Distro-agnostic POSIX/GNU Make build & install | Builds into `build/` and installs to `$(DESTDIR)$(PREFIX)` (`bin/`, `lib/delphis/`, `share/applications/`, `share/icons/`). Uses POSIX shell launchers (`packaging/delphis.sh.in`, `packaging/dotnet-launcher.sh.in`). |
| `flake.nix`, `default.nix`, `shell.nix`, `nix/` | Nix Flake & modules | Packages `delphis` by wrapping `Makefile` (`nix/delphis.nix`, with `WITH_DTE=0` using `nix/dte.nix`), exposes NixOS & Home Manager modules and overlay, and provides `nix develop` shell. |
| `tests/Delphis.Tests/` | Automated C# test suite | Custom reflection-based `[Fact]` runner (`Program.cs` discovers all public `*Tests` classes automatically). |
| `VERSION` | Single source of truth for version | Read by `Directory.Build.props`, `src/delphis_gui/constants.py`, `flake.nix`, and `nix/delphis.nix`. |

### What Delphis is NOT (do not drift out of scope)

- **Not NixOS-only**: Everything in `src/`, `Makefile`, and `packaging/` must work on any Linux distro (Debian/Ubuntu, Fedora, Arch, openSUSE, musl/Alpine, NixOS). Nix packaging lives strictly under `flake.nix`, `default.nix`, `shell.nix`, and `nix/`.
- **Not a pure Python tool**: Python (`requirements.txt`: `PyQt6>=6.4`) is only for the GUI; headless CLI workflows (`delphis-cli`) must work without Python or PyQt6 installed.
- **Not a general image editor**: Features strictly serve Dolphin texture workflows (DTE extraction/tonemaps, alpha/defringe tuning, Real-ESRGAN 2-layer RGB+A upscaling, Dolphin DDS presets).

---

## 4. Invariants & runtime conventions

### Distribution-agnostic path & tool resolution

- **XDG & env overrides** (`DelphisPaths.cs`):
  - `DELPHIS_DATA_DIR` > `$XDG_DATA_HOME/delphis` > `~/.local/share/delphis`
  - `DELPHIS_TOOLS_PATH` (colon-separated directories checked before `PATH`)
  - `DELPHIS_CLI` (explicit `delphis-cli` override for GUI)
  - `DELPHIS_PYTHON` (Python interpreter override in `delphis` launcher)
  - `DELPHIS_DOTNET` (`dotnet` host override in launchers)
- **Tool search order** (`EnvironmentDetector.GetSearchDirectories()`):
  1. `$DELPHIS_TOOLS_PATH`
  2. `$PATH`
  3. `DelphisPaths.InstallToolDirs` (`AppContext.BaseDirectory`, sibling `bin/` and `dte/`, `<prefix>/bin`)
  4. `DelphisPaths.UserToolsDir` (`$DELPHIS_DATA_DIR/bin` / `~/.local/share/delphis/bin`)
  5. Common Linux & Nix locations (`~/.local/bin`, `/usr/local/bin`, `/usr/bin`, `/bin`, `/opt/delphis/bin`, `~/.nix-profile/bin`, `/run/current-system/sw/bin`)
- **Launchers & NixOS compatibility**:
  - `Directory.Build.props` sets `<RollForward>Major</RollForward>` so builds run on .NET 8 or newer runtimes (.NET 9/10).
  - `packaging/dotnet-launcher.sh.in` and `cli_locator.py` prefer `dotnet <app>.dll` for framework-dependent builds (which works on both standard FHS Linux and NixOS without `nix-ld`), while supporting `SELF_CONTAINED=1` (`libhostfxr.so` next to binary).
- **Dynamic Real-ESRGAN provisioning** (`UpscalerEngine.EnsureRealEsrganAsync` + `ToolDownloader`): when `realesrgan-ncnn-vulkan` is missing on x86_64 Linux, `UpscalerEngine` automatically downloads and extracts it (with neural models) into `DelphisPaths.UserToolsDir` (`$XDG_DATA_HOME/delphis/bin`) on first use.
- **ImageMagick compatibility**: support both IMv7 (`magick`, `magick identify`) and IMv6 (`convert`, `identify`, `convert ... info:`).
- **Constants sync**: `src/Delphis.Core/DelphisConstants.cs` and `src/delphis_gui/constants.py` mirror each other (`DdsPresets`/`DDS_PRESETS`, `AiModels`/`AI_MODELS`, `ScaleFactors`/`SCALE_FACTORS`, `AlphaQuickPresets`/`ALPHA_QUICK_PRESETS`). Update both + `tests/Delphis.Tests/ConstantsTests.cs` if changing any preset or model.

### Known valid options (verify in code before changing)

- **DDS presets**: `BC7` (default), `BC1`, `BC1a`, `BC3`, `BC5`
- **AI models**: `realesrgan-x4plus-anime`, `realesrgan-x4plus`, `realesr-animevideov3`
- **Scale factors**: `2`, `3`, `4`
- **DTE modes**: `merge_rgba` (`CombineRGBA`), `merge_bgra` (`CombineBGRA`), `split_tonemaps` (`Finalize`), `extract` (`Extract`), `unpack` (`Unpack`), `custom`

---

## 5. Code style

- Respect `.editorconfig`.
- **C#**: `.NET 8`, `ImplicitUsings` and `Nullable` enabled in `src/` and `tests/`; follow existing service/interface (`IFoo` + `Foo`) and record model patterns; keep helpers cohesive and avoid duplicating logic.
- **Python (PyQt6)**: Follow `src/delphis_gui/style.py` — delegate visual styling to the desktop's native `QStyle` (e.g. KDE Breeze) without overriding widgets with synthetic QSS/CSS. Resolve `delphis.svg` through XDG paths rather than hardcoded system paths.
- **Nix**: Keep `nix/delphis.nix` wrapping the top-level `Makefile` (`WITH_DTE=0`) so Nix builds test the same build/install logic used by other Linux distributions.

---

## 6. Development & verification workflow

### On NixOS (using the bundled flake)

If `.NET 8`, `PyQt6`, or runtime tools are not globally installed on the host, run build/test commands inside the flake devShell:

```bash
nix develop --command make WITH_DTE=0   # build delphis-cli + launchers (dte comes from devShell)
nix develop --command make test         # run C# test suite + Python GUI checks
```

> **Important for Nix flakes**: Flakes only see files tracked by Git. Run `git add <new-files>` before running `nix build` or `nix flake check`.

### On any Linux distribution

```bash
make                 # build delphis-cli and bundled DTE into build/
make test            # run C# tests and Python checks
make run             # launch GUI from source tree
make install PREFIX=~/.local
```

### Verification checklist before reporting completion

1. Run `make test` (or `nix develop --command make test`).
2. When changing build/packaging/paths, test `make WITH_DTE=0` (or `make`) and `make install DESTDIR=/tmp/... PREFIX=/usr/local` and verify installed launchers (`delphis --help`, `delphis-cli --version`).
3. When changing `flake.nix` or `nix/*`, run `nix build` and `nix flake check` (after staging new files with `git add`).
4. List exact files changed and only the checks actually executed and observed passing.

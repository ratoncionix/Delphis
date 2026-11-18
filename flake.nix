{
  description = "Delphis: Dolphin Texture Manipulation Tool - Extraction, Tonemaps, Alpha Transparency, AI Upscaling & DDS Optimization";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs/nixos-unstable";
    systems.url = "github:nix-systems/default";
    flake-compat = {
      url = "github:edolstra/flake-compat";
      flake = false;
    };
  };

  outputs =
    {
      self,
      nixpkgs,
      systems,
      flake-compat,
    }:
    let
      eachSystem =
        f:
        nixpkgs.lib.genAttrs (import systems) (
          system:
          f {
            inherit system;
            pkgs = import nixpkgs {
              inherit system;
              overlays = [ self.overlays.default ];
              config = {
                allowUnfree = true;
              };
            };
          }
        );
    in
    {
      overlays.default = import ./nix/overlay.nix;

      nixosModules = {
        default = import ./nix/nixos-module.nix;
        delphis = self.nixosModules.default;
      };

      homeManagerModules = {
        default = import ./nix/home-manager-module.nix;
        delphis = self.homeManagerModules.default;
      };

      packages = eachSystem (
        { pkgs, system }:
        rec {
          default = delphis;
          delphis = pkgs.delphis;
          dte = pkgs.dolphin-texture-extraction-tool;
          dolphin-texture-extraction-tool = pkgs.dolphin-texture-extraction-tool;
        }
      );

      apps = eachSystem (
        { pkgs, system }:
        let
          testApp = pkgs.writeShellApplication {
            name = "delphis-tests";
            runtimeInputs = [
              pkgs.dotnetCorePackages.sdk_8_0
            ];
            text = ''
              export DOTNET_CLI_TELEMETRY_OPTOUT=1
              export DOTNET_NOLOGO=1
              dotnet run --project tests/Delphis.Tests/Delphis.Tests.csproj "$@"
            '';
          };
        in
        rec {
          default = delphis;
          delphis = {
            type = "app";
            program = "${pkgs.delphis}/bin/delphis";
            meta = {
              description = "Delphis: Dolphin Texture Manipulation Tool";
            };
          };
          dte = {
            type = "app";
            program = "${pkgs.dolphin-texture-extraction-tool}/bin/dte";
            meta = {
              description = "DolphinTextureExtraction-tool CLI";
            };
          };
          test = {
            type = "app";
            program = "${testApp}/bin/delphis-tests";
            meta = {
              description = "Run automated test suite (.NET 8 C#)";
            };
          };
        }
      );

      checks = eachSystem (
        { pkgs, system }:
        {
          delphis-package = pkgs.delphis;
          dte-package = pkgs.dolphin-texture-extraction-tool;

          dotnet-tests = pkgs.stdenv.mkDerivation {
            pname = "delphis-dotnet-tests";
            version = nixpkgs.lib.removeSuffix "\n" (builtins.readFile ./VERSION);
            src = self;

            nativeBuildInputs = [
              pkgs.dotnetCorePackages.sdk_8_0
            ];

            dontBuild = true;

            installPhase = ''
              mkdir -p $out
              export DOTNET_CLI_TELEMETRY_OPTOUT=1
              export DOTNET_NOLOGO=1
              export HOME=$TMPDIR
              dotnet run --project tests/Delphis.Tests/Delphis.Tests.csproj
              touch $out/success
            '';
          };
        }
      );

      devShells = eachSystem (
        { pkgs, system }:
        let
          qtPluginPaths = pkgs.lib.makeSearchPath "lib/qt-6/plugins" [
            pkgs.qt6.qtbase
            pkgs.qt6.qtsvg
            pkgs.qt6.qtwayland
            pkgs.kdePackages.breeze
            pkgs.kdePackages.plasma-integration
          ];
          xdgDataDirs = pkgs.lib.makeSearchPath "share" [
            pkgs.kdePackages.breeze-icons
          ];
        in
        {
          default = pkgs.mkShell {
            name = "delphis-dev-shell";

            packages = [
              pkgs.dotnetCorePackages.sdk_8_0
              pkgs.gnumake
              (pkgs.python3.withPackages (ps: with ps; [
                pyqt6
                pillow
              ]))
              pkgs.qt6.qtbase
              pkgs.qt6.qtwayland
              pkgs.qt6.qtsvg
              pkgs.kdePackages.breeze
              pkgs.kdePackages.breeze-icons
              pkgs.kdePackages.plasma-integration
              pkgs.dolphin-texture-extraction-tool
              pkgs.realesrgan-ncnn-vulkan
              pkgs.imagemagick
              pkgs.nvidia-texture-tools
              pkgs.vulkan-tools
              pkgs.vulkan-loader
            ];

            shellHook = ''
              # Prepend the flake's Qt6/KDE plugins so mismatched host Qt plugins do not shadow them.
              export QT_PLUGIN_PATH="${qtPluginPaths}''${QT_PLUGIN_PATH:+:$QT_PLUGIN_PATH}"
              export XDG_DATA_DIRS="${xdgDataDirs}''${XDG_DATA_DIRS:+:$XDG_DATA_DIRS}"
              echo "Delphis .NET 8 development shell"
              echo "  make          build delphis-cli into build/"
              echo "  make test     run automated C# and Python tests"
              echo "  make run      launch the PyQt6 GUI from source"
            '';
          };
        }
      );
    };
}

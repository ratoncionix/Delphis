{
  config,
  lib,
  pkgs,
  ...
}:
let
  cfg = config.programs.delphis;

  # Works whether or not the Delphis overlay was added to nixpkgs.
  defaultPackage = pkgs.delphis or (pkgs.extend (import ./overlay.nix)).delphis;
in
{
  options.programs.delphis = {
    enable = lib.mkEnableOption "Delphis: Dolphin Texture Manipulation Tool";

    package = lib.mkOption {
      type = lib.types.package;
      default = defaultPackage;
      defaultText = lib.literalExpression "pkgs.delphis";
      description = "The Delphis package to install.";
    };
  };

  config = lib.mkIf cfg.enable {
    environment.systemPackages = [
      cfg.package
    ];
  };
}

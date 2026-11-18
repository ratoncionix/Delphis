final: prev:
let
  rootDir = toString ../.;
  isIgnored =
    baseName:
    baseName == ".git"
    || baseName == "result"
    || baseName == "bin"
    || baseName == "obj"
    || baseName == "__pycache__"
    || baseName == ".pytest_cache"
    || baseName == "build"
    || baseName == ".venv";

  delphisSrc = final.lib.cleanSourceWith {
    src = ../.;
    filter = path: _type: !(isIgnored (baseNameOf path));
  };

  # Scope DTE source so edits to Delphis C#/Python/docs do not force DTE rebuilds in devShell.
  dteSrc = final.lib.cleanSourceWith {
    src = ../.;
    filter =
      path: _type:
      let
        rel = final.lib.removePrefix (rootDir + "/") (toString path);
        baseName = baseNameOf path;
      in
      !(isIgnored baseName)
      && (
        rel == "DolphinTextureExtraction"
        || final.lib.hasPrefix "DolphinTextureExtraction/" rel
        || rel == "lib"
        || final.lib.hasPrefix "lib/" rel
        || rel == "Directory.Build.props"
        || rel == "VERSION"
      );
  };
in
{
  dolphin-texture-extraction-tool = prev.callPackage ./dte.nix {
    src = dteSrc;
  };
  dte = final.dolphin-texture-extraction-tool;

  delphis = final.callPackage ./delphis.nix {
    dte = final.dte;
    src = delphisSrc;
  };
}

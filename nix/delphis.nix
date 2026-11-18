{
  lib,
  stdenv,
  dotnetCorePackages,
  python3Packages,
  qt6,
  kdePackages,
  makeWrapper,
  dte,
  realesrgan-ncnn-vulkan,
  imagemagick,
  nvidia-texture-tools,
  vulkan-tools,
  vulkan-loader,
  src ? ../.,
}:
let
  dotnet-sdk = dotnetCorePackages.sdk_8_0;
  dotnet-runtime = dotnetCorePackages.runtime_8_0;

  pythonEnv = python3Packages.python.withPackages (
    ps: with ps; [
      pyqt6
      pillow
    ]
  );

  # External tools Delphis drives at runtime. On other distributions these come
  # from the system package manager; on Nix they are pinned here.
  runtimeTools = lib.makeBinPath [
    dte
    realesrgan-ncnn-vulkan
    imagemagick
    nvidia-texture-tools
    vulkan-tools
    vulkan-loader
  ];
in
stdenv.mkDerivation {
  pname = "delphis";
  version = lib.removeSuffix "\n" (builtins.readFile ../VERSION);

  inherit src;

  nativeBuildInputs = [
    dotnet-sdk
    makeWrapper
    qt6.wrapQtAppsHook
  ];

  buildInputs = [
    dotnet-runtime
    qt6.qtbase
    qt6.qtwayland
    qt6.qtsvg
    # Match Qt ABI for Breeze widget style & KDE platform theme (light/dark palette).
    kdePackages.breeze
    kdePackages.breeze-icons
    kdePackages.plasma-integration
    pythonEnv
  ];

  # Same build system as every other Linux distribution (see ../Makefile).
  # DTE is packaged separately (./dte.nix) with pinned NuGet dependencies,
  # so it is not built here.
  makeFlags = [
    "PREFIX=${placeholder "out"}"
    "PYTHON=${pythonEnv}/bin/python3"
    "DOTNET_HOST=${dotnet-runtime}/bin/dotnet"
    "WITH_DTE=0"
  ];

  preBuild = ''
    export HOME=$TMPDIR
  '';

  dontWrapQtApps = true;

  postFixup = ''
    wrapProgram $out/bin/delphis-cli \
      --prefix PATH : ${runtimeTools}

    wrapQtApp $out/bin/delphis \
      --prefix PATH : ${runtimeTools}
  '';

  meta = with lib; {
    description = "Delphis: Dolphin Texture Manipulation Tool - Extraction, Tonemaps, Alpha Transparency, AI Upscaling & DDS Optimization";
    homepage = "https://github.com/ratoncionix/Delphis";
    license = licenses.mit;
    mainProgram = "delphis";
    platforms = platforms.linux;
  };
}

"""
KDE Plasma 6 / Breeze Native Style Integration for Delphis.
Delegates visual styling directly to KDE Plasma's native QStyle (Breeze)
and system palette without overriding Qt widget delegates with synthetic CSS.
"""

from __future__ import annotations

import os
from pathlib import Path

from PyQt6.QtCore import QSettings
from PyQt6.QtGui import QIcon
from PyQt6.QtWidgets import QApplication, QStyleFactory


def get_breeze_icon(name: str, fallback_icon: str | None = None) -> QIcon:
    """Retrieve icon from the active system theme (Breeze) with fallback."""
    icon = QIcon.fromTheme(name)
    if not icon.isNull():
        return icon
    if fallback_icon:
        fb = QIcon.fromTheme(fallback_icon)
        if not fb.isNull():
            return fb
    return QIcon()


def _icon_candidates() -> list[Path]:
    """Possible locations of delphis.svg, independent of the Linux distribution."""
    here = Path(__file__).resolve().parent
    candidates = [
        here.parent.parent / "assets" / "icons" / "delphis.svg",   # source checkout
        here / "assets" / "icons" / "delphis.svg",                  # bundled with the package
    ]

    # XDG data directories ($XDG_DATA_HOME, $XDG_DATA_DIRS)
    data_dirs = [os.environ.get("XDG_DATA_HOME") or str(Path.home() / ".local" / "share")]
    data_dirs += (os.environ.get("XDG_DATA_DIRS") or "/usr/local/share:/usr/share").split(":")

    # <prefix>/lib/delphis/python/delphis_gui -> <prefix>/share
    if len(here.parents) > 3:
        data_dirs.insert(0, str(here.parents[3] / "share"))

    # Nix profiles
    data_dirs += [
        str(Path.home() / ".nix-profile" / "share"),
        "/run/current-system/sw/share",
    ]

    for d in data_dirs:
        if d:
            candidates.append(Path(d) / "icons" / "hicolor" / "scalable" / "apps" / "delphis.svg")
    return candidates

def get_app_icon() -> QIcon:
    """
    Retrieve the main application icon for Delphis.
    Loads the SVG asset from assets/icons/delphis.svg.
    """
    # 1. Bundled vector SVG asset / XDG data directories
    for svg_path in _icon_candidates():
        if svg_path.is_file():
            icon = QIcon(str(svg_path))
            if not icon.isNull():
                return icon

    # 2. Installed XDG system theme icon
    theme_icon = QIcon.fromTheme("delphis")
    if not theme_icon.isNull():
        return theme_icon

    # 3. Breeze fallback
    return get_breeze_icon("applications-graphics", "image-x-generic")

NATIVE_SYSTEM_STYLE: str | None = None

def get_native_system_style() -> str:
    """Returns the native system style detected at startup (e.g. Breeze, Fusion)."""
    global NATIVE_SYSTEM_STYLE
    if NATIVE_SYSTEM_STYLE:
        return NATIVE_SYSTEM_STYLE
    if QApplication.style():
        NATIVE_SYSTEM_STYLE = QApplication.style().objectName()
        if NATIVE_SYSTEM_STYLE:
            return NATIVE_SYSTEM_STYLE
    style_keys = QStyleFactory.keys()
    available_styles_lower = {s.lower(): s for s in style_keys}
    if "breeze" in available_styles_lower:
        return available_styles_lower["breeze"]
    if "fusion" in available_styles_lower:
        return available_styles_lower["fusion"]
    return style_keys[0] if style_keys else "Fusion"

def setup_kde_plasma_integration(app: QApplication):
    """
    Connect with desktop environment and restore active/saved visual theme.
    Loads theme from QSettings ("Native" / specific style) without breaking qt6ct/KDE platform themes.
    """
    global NATIVE_SYSTEM_STYLE
    if not NATIVE_SYSTEM_STYLE and app.style():
        NATIVE_SYSTEM_STYLE = app.style().objectName()

    app_icon = get_app_icon()
    if not app_icon.isNull():
        app.setWindowIcon(app_icon)

    settings = QSettings("Delphis", "Delphis")
    saved_theme = settings.value("ui/theme", "Native")

    available_styles = QStyleFactory.keys()
    if saved_theme and saved_theme.lower() != "native":
        matched = next((s for s in available_styles if s.lower() == saved_theme.lower()), None)
        if matched:
            app.setStyle(matched)

    app.setStyleSheet("")

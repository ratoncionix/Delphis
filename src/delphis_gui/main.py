"""
Main Application Entry Point for Delphis Qt6 GUI.

`delphis` with no arguments opens the GUI.
`delphis <cli-command> ...` (alpha, dds, upscale, dte, --help, ...)
is forwarded to the C# backend (`delphis-cli`) so the same entry point works
from scripts and terminals without Qt being involved.
"""

import os
import signal
import sys

from delphis_gui.cli_locator import CLI_COMMANDS, find_delphis_cli_command

_QUIET_FLAGS = {"-q", "--quiet", "--no-logs"}


def _should_forward_to_cli(argv: list[str]) -> bool:
    first_cmd = next((arg for arg in argv[1:] if arg not in _QUIET_FLAGS), None)
    return first_cmd in CLI_COMMANDS


def _forward_to_cli(args: list[str]) -> int:
    cmd = find_delphis_cli_command() + list(args)
    try:
        os.execvp(cmd[0], cmd)
    except FileNotFoundError:
        sys.stderr.write(
            "delphis: could not find the 'delphis-cli' backend.\n"
            "Build/install it with 'make && make install' or set DELPHIS_CLI.\n"
        )
        return 127


def _missing_qt_message(exc: ImportError) -> str:
    return (
        f"delphis: the graphical interface requires PyQt6 ({exc}).\n\n"
        "Install it with your distribution's package manager, for example:\n"
        "  Debian/Ubuntu : sudo apt install python3-pyqt6\n"
        "  Fedora        : sudo dnf install python3-pyqt6\n"
        "  Arch Linux    : sudo pacman -S python-pyqt6\n"
        "  openSUSE      : sudo zypper install python3-PyQt6\n"
        "or in a virtual environment: pip install -r requirements.txt\n\n"
        "The command-line backend works without PyQt6: run 'delphis-cli --help'.\n"
    )


def main() -> int:
    # CLI passthrough: `delphis alpha ...`, `delphis --quiet upscale ...`, `delphis --help`, ...
    if _should_forward_to_cli(sys.argv):
        return _forward_to_cli(sys.argv[1:])

    try:
        from PyQt6.QtCore import QCoreApplication
        from PyQt6.QtWidgets import QApplication
    except ImportError as exc:
        sys.stderr.write(_missing_qt_message(exc))
        return 1

    from delphis_gui.constants import APP_NAME, APP_VERSION
    from delphis_gui.gui.main_window import MainWindow
    from delphis_gui.style import setup_kde_plasma_integration

    signal.signal(signal.SIGINT, signal.SIG_DFL)

    # 1. Respect user's existing QT_QPA_PLATFORM environment variable
    # Only set a default if completely empty or unset in system
    if "QT_QPA_PLATFORM" in os.environ and not os.environ["QT_QPA_PLATFORM"].strip():
        del os.environ["QT_QPA_PLATFORM"]

    # 2. Application and Organization metadata for QSettings
    QCoreApplication.setOrganizationName("Delphis")
    QCoreApplication.setOrganizationDomain("github.com/Cleison-oliveira/Delphis")
    QCoreApplication.setApplicationName("Delphis")

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationVersion(APP_VERSION)
    app.setOrganizationName("Delphis")
    app.setOrganizationDomain("github.com/Cleison-oliveira/Delphis")
    app.setDesktopFileName("delphis")

    setup_kde_plasma_integration(app)

    window = MainWindow()
    window.show()

    return app.exec()


if __name__ == "__main__":
    sys.exit(main())

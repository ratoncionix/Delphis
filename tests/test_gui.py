"""Automated tests for Delphis Python CLI locator, forwarding, constants, and PyQt6 UI."""

from __future__ import annotations

import os
import sys
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = REPO_ROOT / "src"
if str(SRC_DIR) not in sys.path:
    sys.path.insert(0, str(SRC_DIR))

from delphis_gui.cli_locator import CLI_COMMANDS, find_delphis_cli_command
from delphis_gui.constants import AI_MODELS, APP_NAME, APP_VERSION, DDS_PRESETS
from delphis_gui.main import _should_forward_to_cli

try:
    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    from PyQt6.QtWidgets import QApplication

    HAS_PYQT6 = True
except ImportError:
    HAS_PYQT6 = False


class CliLocatorAndForwardingTests(unittest.TestCase):
    def test_cli_commands_and_constants(self) -> None:
        self.assertEqual(APP_NAME, "Delphis")
        version_file = (REPO_ROOT / "VERSION").read_text(encoding="utf-8").strip()
        self.assertEqual(APP_VERSION, version_file)
        self.assertIn("BC7", DDS_PRESETS)
        self.assertIn("realesrgan-x4plus-anime", [m[0] for m in AI_MODELS])
        for cmd in ("alpha", "dds", "upscale", "dte", "--help"):
            self.assertIn(cmd, CLI_COMMANDS)

    def test_should_forward_to_cli_with_quiet_flags(self) -> None:
        self.assertTrue(_should_forward_to_cli(["delphis", "alpha", "a", "b"]))
        self.assertTrue(_should_forward_to_cli(["delphis", "--quiet", "alpha", "a", "b"]))
        self.assertTrue(_should_forward_to_cli(["delphis", "-q", "upscale", "a", "b"]))
        self.assertTrue(_should_forward_to_cli(["delphis", "--no-logs", "dte"]))
        self.assertFalse(_should_forward_to_cli(["delphis"]))
        self.assertFalse(_should_forward_to_cli(["delphis", "--unknown"]))

    def test_find_delphis_cli_command_honors_env_override(self) -> None:
        with mock.patch.dict(os.environ, {"DELPHIS_CLI": "/custom/bin/delphis-cli"}):
            self.assertEqual(find_delphis_cli_command(), ["/custom/bin/delphis-cli"])


@unittest.skipUnless(HAS_PYQT6, "PyQt6 not installed in current Python environment")
class PyQtGuiTests(unittest.TestCase):
    _app: QApplication | None = None

    @classmethod
    def setUpClass(cls) -> None:
        cls._app = QApplication.instance() or QApplication([])

    def test_dte_tab_builds_extra_and_all_flags(self) -> None:
        from delphis_gui.gui.tabs.tab_dte import DTETab

        tab = DTETab()
        self.assertIsNone(tab.build_command())

        tab.input_picker.setPath("/tmp/in")
        tab.output_picker.setPath("/tmp/out")
        tab.chk_recursive.setChecked(False)
        tab.chk_mips.setChecked(True)
        tab.chk_dmd.setChecked(False)
        tab.chk_logfile.setChecked(True)
        tab.txt_extra.setText("-Force -Dryrun")

        cmd = tab.build_command()
        assert cmd is not None
        self.assertEqual(cmd[:4], ["dte", "merge_rgba", "/tmp/in", "/tmp/out"])
        self.assertIn("--no-recursive", cmd)
        self.assertIn("--mips", cmd)
        self.assertIn("--no-dmd", cmd)
        self.assertIn("--logfile", cmd)
        self.assertIn("--extra", cmd)
        self.assertEqual(cmd[cmd.index("--extra") + 1], "-Force -Dryrun")

    def test_alpha_tab_builds_recursive_and_defringe_flags(self) -> None:
        from delphis_gui.gui.tabs.tab_alpha import AlphaTab

        tab = AlphaTab()
        tab.input_picker.setPath("/tmp/in")
        tab.output_picker.setPath("/tmp/out")
        tab.chk_recursive.setChecked(False)
        tab.chk_defringe.setChecked(False)
        tab.chk_hard.setChecked(True)
        tab.chk_smooth.setChecked(True)
        tab.spin_smooth_radius.setValue(2.5)

        cmd = tab.build_command()
        assert cmd is not None
        self.assertEqual(cmd[:3], ["alpha", "/tmp/in", "/tmp/out"])
        self.assertIn("--no-recursive", cmd)
        self.assertIn("--no-defringe", cmd)
        self.assertIn("--hard", cmd)
        self.assertIn("--smooth", cmd)
        self.assertIn("2.5", cmd)

    def test_upscale_and_dds_tabs_honor_no_recursive(self) -> None:
        from delphis_gui.gui.tabs.tab_dds import DDSTab
        from delphis_gui.gui.tabs.tab_upscale import UpscaleTab

        up = UpscaleTab()
        up.input_picker.setPath("/tmp/in")
        up.output_picker.setPath("/tmp/out")
        up.chk_recursive.setChecked(False)
        up_cmd = up.build_command()
        assert up_cmd is not None
        self.assertIn("--no-recursive", up_cmd)

        dds = DDSTab()
        dds.input_picker.setPath("/tmp/in")
        dds.output_picker.setPath("/tmp/out")
        dds.chk_recursive.setChecked(False)
        dds.chk_mipmaps.setChecked(False)
        dds_cmd = dds.build_command()
        assert dds_cmd is not None
        self.assertIn("--no-recursive", dds_cmd)
        self.assertIn("--no-mips", dds_cmd)

    def test_main_window_and_alpha_slider(self) -> None:
        from delphis_gui.gui.main_window import MainWindow

        win = MainWindow()
        self.assertEqual(win.tab_widget.count(), 4)
        win.tab_alpha.alpha_slider.setValue(130)
        self.assertEqual(win.tab_alpha.alpha_slider.value(), 130)
        win.console_drawer.append_log("test message", "success")
        self.assertIn("test message", win.console_drawer.log_edit.toPlainText())
        win.console_drawer.clear()
        self.assertEqual(win.console_drawer.log_edit.toPlainText(), "")

    def test_worker_progress_ignores_paths_and_handles_cancel(self) -> None:
        from delphis_gui.core.bridge import CSharpCommandWorker

        worker = CSharpCommandWorker(["alpha"])
        progress_events: list[tuple[int, int]] = []
        finished_events: list[tuple[bool, str]] = []
        cancelled_events: list[bool] = []
        worker.signals.progress.connect(lambda c, t: progress_events.append((c, t)))
        worker.signals.finished.connect(lambda ok, msg: finished_events.append((ok, msg)))
        worker.signals.cancelled.connect(lambda: cancelled_events.append(True))

        # Test worker execution with a fast Python script that emits path + progress + bar.
        script = (
            "import sys\n"
            "print('Processing /home/user/10/20/texture.png', flush=True)\n"
            "print('Upscaling (3/10): file.png', flush=True)\n"
            "print('│██████│ 50.0%', flush=True)\n"
        )
        with mock.patch(
            "delphis_gui.core.bridge.find_delphis_cli_command",
            return_value=[sys.executable, "-c", script],
        ):
            worker.run()

        self.assertEqual(progress_events, [(3, 10), (50, 100)])
        self.assertEqual(len(finished_events), 1)
        self.assertTrue(finished_events[0][0])
        self.assertEqual(cancelled_events, [])

        # Cancelled worker must emit cancelled without also emitting finished.
        worker2 = CSharpCommandWorker(["alpha"])
        finished2: list[tuple[bool, str]] = []
        cancelled2: list[bool] = []
        worker2.signals.finished.connect(lambda ok, msg: finished2.append((ok, msg)))
        worker2.signals.cancelled.connect(lambda: cancelled2.append(True))
        worker2.cancel()
        with mock.patch(
            "delphis_gui.core.bridge.find_delphis_cli_command",
            return_value=[sys.executable, "-c", "print('hi')"],
        ):
            worker2.run()
        self.assertEqual(cancelled2, [True])
        self.assertEqual(finished2, [])


if __name__ == "__main__":
    unittest.main()

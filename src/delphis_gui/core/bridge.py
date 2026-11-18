"""
Asynchronous Process Bridge & Worker Subsystem connecting PyQt6 to Delphis C# Engine.
"""

from __future__ import annotations

import re
import shutil
import subprocess
import threading

from PyQt6.QtCore import QObject, QThread, pyqtSignal

from delphis_gui.cli_locator import find_delphis_cli, find_delphis_cli_command

__all__ = [
    "CSharpCommandWorker",
    "GpuProbeWorker",
    "WorkerSignals",
    "detect_vulkan_gpus",
    "find_delphis_cli",
    "find_delphis_cli_command",
]


class WorkerSignals(QObject):
    """Signals emitted by asynchronous workers to update the GUI."""
    started = pyqtSignal()
    output_line = pyqtSignal(str, str)  # (message, level: 'info' | 'success' | 'warning' | 'error')
    progress = pyqtSignal(int, int)     # (current, total)
    finished = pyqtSignal(bool, str)    # (success, summary_message)
    cancelled = pyqtSignal()


def detect_vulkan_gpus() -> list[tuple[int, str]]:
    """Detect available Vulkan GPU devices via vulkaninfo or fallback."""
    gpus: list[tuple[int, str]] = [(-1, "Auto-detect Best Discrete GPU (Recommended)")]
    vulkaninfo = shutil.which("vulkaninfo")
    if vulkaninfo:
        try:
            output = subprocess.check_output(
                [vulkaninfo, "--summary"],
                text=True,
                stderr=subprocess.DEVNULL,
                timeout=3,
            )
            # Matches lines like: GPU0: ... deviceName = NVIDIA GeForce RTX 3080
            for match in re.finditer(r"GPU(\d+):\s*[\r\n]+(?:\s+.*\n)*?\s+deviceName\s*=\s*(.+)", output):
                dev_id = int(match.group(1))
                dev_name = match.group(2).strip()
                gpus.append((dev_id, f"GPU {dev_id}: {dev_name}"))
        except (OSError, subprocess.SubprocessError):
            pass

    if len(gpus) == 1:
        gpus.append((0, "GPU 0: Default Vulkan Graphics Device"))
    return gpus


class GpuProbeWorker(QThread):
    """Non-blocking background probe for Vulkan GPU devices."""

    gpusDetected = pyqtSignal(list)

    def run(self) -> None:
        self.gpusDetected.emit(detect_vulkan_gpus())


class CSharpCommandWorker(QThread):
    """
    Asynchronous QThread executing Delphis C# commands without blocking the Qt event loop.
    """
    def __init__(self, command_args: list[str], quiet: bool = False, parent: QObject | None = None):
        super().__init__(parent)
        self.signals = WorkerSignals()
        self.command_args = list(command_args)
        self.quiet = quiet
        self._is_cancelled = False
        self._current_process: subprocess.Popen[str] | None = None
        self._lock = threading.Lock()

        if self.quiet and "--quiet" not in self.command_args and "-q" not in self.command_args and "--no-logs" not in self.command_args:
            self.command_args = ["--quiet"] + self.command_args

    def cancel(self) -> None:
        """Cancel running subprocess."""
        self._is_cancelled = True
        with self._lock:
            if self._current_process and self._current_process.poll() is None:
                try:
                    self._current_process.terminate()
                    threading.Timer(0.5, self._force_kill_process).start()
                except OSError:
                    pass
        self.signals.cancelled.emit()
        self.signals.output_line.emit("Operation cancelled by user.", "warning")

    def _force_kill_process(self) -> None:
        with self._lock:
            if self._current_process and self._current_process.poll() is None:
                try:
                    self._current_process.kill()
                except OSError:
                    pass

    def run(self) -> None:
        self.signals.started.emit()

        cmd = find_delphis_cli_command() + self.command_args

        if not self.quiet:
            cmd_str = " ".join(cmd)
            self.signals.output_line.emit(f"$ {cmd_str}", "info")

        try:
            with subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1,
            ) as proc:
                with self._lock:
                    self._current_process = proc

                if proc.stdout is not None:
                    while True:
                        if self._is_cancelled:
                            with self._lock:
                                if self._current_process and self._current_process.poll() is None:
                                    self._current_process.terminate()
                            break

                        line = proc.stdout.readline()
                        if not line and proc.poll() is not None:
                            break
                        if line:
                            clean_line = line.rstrip()

                            # 1. Parse DTE percentage progress bar: e.g. "│█████████████▓ │ 34.76%"
                            pct_match = re.search(r"│.*│\s*(\d+(?:\.\d+)?)%", clean_line)
                            if pct_match:
                                try:
                                    pct_val = int(float(pct_match.group(1)))
                                    self.signals.progress.emit(pct_val, 100)
                                    continue  # Do not dump raw progress bar lines to console log
                                except ValueError:
                                    pass

                            # 2. Parse explicit batch progress: e.g. "(5/20)" or "Upscaling (5/20):"
                            match_prog = re.search(r"\((\d+)/(\d+)\)", clean_line)
                            if match_prog:
                                try:
                                    cur = int(match_prog.group(1))
                                    tot = int(match_prog.group(2))
                                    self.signals.progress.emit(cur, tot)
                                except ValueError:
                                    pass

                            # 3. Classify log level
                            lower = clean_line.lower()
                            level = "info"
                            if "error" in lower or "fail" in lower or "fatal" in lower:
                                level = "error"
                            elif "warning" in lower or "[!]" in lower:
                                level = "warning"
                            elif "success" in lower or "done" in lower or "[ok]" in lower or "✔" in clean_line:
                                level = "success"

                            # 4. Emit log line unless in quiet mode
                            if not self.quiet or level in ("error", "warning"):
                                self.signals.output_line.emit(clean_line, level)

                rc = proc.wait()
            with self._lock:
                self._current_process = None

            if self._is_cancelled:
                return

            success = rc == 0
            msg = "Operation completed successfully." if success else f"Process exited with code {rc}."
            self.signals.finished.emit(success, msg)

        except (OSError, subprocess.SubprocessError, ValueError) as e:
            with self._lock:
                self._current_process = None
            if not self._is_cancelled:
                self.signals.output_line.emit(f"[FATAL] Error running command: {e}", "error")
                self.signals.finished.emit(False, str(e))

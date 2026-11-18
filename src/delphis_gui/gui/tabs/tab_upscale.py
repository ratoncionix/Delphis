"""
Tab 3: 4-Channel AI Upscaler (Real-ESRGAN + ImageMagick)
"""

from __future__ import annotations

from PyQt6.QtCore import QTimer, pyqtSignal
from PyQt6.QtWidgets import (
    QCheckBox,
    QComboBox,
    QFormLayout,
    QGroupBox,
    QMessageBox,
    QScrollArea,
    QVBoxLayout,
    QWidget,
)

from delphis_gui.constants import AI_MODELS, ALPHA_UPSCALE_MODES, SCALE_FACTORS
from delphis_gui.core.bridge import GpuProbeWorker
from delphis_gui.gui.widgets.path_picker import PathPicker


class UpscaleTab(QWidget):
    """
    Interface for 4-Channel Alpha-Preserving AI Upscaling.
    Separates RGBA channels, upscales RGB and Alpha independently, and cleanly recompiles.
    """
    requestWorker = pyqtSignal(list)

    def __init__(self, parent=None):
        super().__init__(parent)

        scroll = QScrollArea(self)
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QScrollArea.Shape.NoFrame)

        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setContentsMargins(14, 14, 14, 14)
        layout.setSpacing(14)

        # 1. Path Selection Card
        paths_group = QGroupBox("Paths & Textures", self)
        paths_layout = QFormLayout(paths_group)
        paths_layout.setSpacing(10)

        self.input_picker = PathPicker(
            mode="both",
            file_filter="PNG Images (*.png);;All Files (*)",
            placeholder="Select PNG image file or folder containing textures...",
            parent=self
        )
        paths_layout.addRow("Input Image/Folder:", self.input_picker)

        self.output_picker = PathPicker(
            mode="directory",
            placeholder="Select destination folder for upscaled textures...",
            parent=self
        )
        paths_layout.addRow("Output Folder:", self.output_picker)
        layout.addWidget(paths_group)

        # 2. Upscaler Configuration Card
        config_group = QGroupBox("AI Upscaling Parameters", self)
        config_layout = QFormLayout(config_group)
        config_layout.setSpacing(10)

        # Scale Factor
        self.combo_scale = QComboBox(self)
        for s in SCALE_FACTORS:
            self.combo_scale.addItem(f"{s}x Resolution Scaling", s)
        self.combo_scale.setCurrentIndex(2)  # Default 4x
        config_layout.addRow("Scale Factor:", self.combo_scale)

        # AI Model
        self.combo_model = QComboBox(self)
        for model_id, model_label in AI_MODELS:
            self.combo_model.addItem(model_label, model_id)
        config_layout.addRow("AI Neural Model:", self.combo_model)

        # Alpha Channel Mode
        self.combo_alpha = QComboBox(self)
        for mode_id, mode_label in ALPHA_UPSCALE_MODES:
            self.combo_alpha.addItem(mode_label, mode_id)
        config_layout.addRow("Alpha Channel Mode:", self.combo_alpha)

        # Vulkan GPU Device (populated asynchronously to keep startup instantaneous)
        self.combo_gpu = QComboBox(self)
        self.combo_gpu.addItem("Auto-detect Best Discrete GPU (Recommended)", -1)
        self.combo_gpu.addItem("GPU 0: Default Vulkan Graphics Device", 0)
        config_layout.addRow("Vulkan GPU Device:", self.combo_gpu)
        self._gpu_worker: GpuProbeWorker | None = None
        QTimer.singleShot(0, self._probe_gpus_async)

        layout.addWidget(config_group)

        # 3. Batch Options Card
        options_group = QGroupBox("Processing Options", self)
        options_layout = QVBoxLayout(options_group)
        options_layout.setSpacing(10)

        self.chk_recursive = QCheckBox("Process Subdirectories Recursively", self)
        self.chk_recursive.setChecked(True)
        options_layout.addWidget(self.chk_recursive)

        layout.addWidget(options_group)

        layout.addStretch()
        scroll.setWidget(container)

        main_vbox = QVBoxLayout(self)
        main_vbox.setContentsMargins(0, 0, 0, 0)
        main_vbox.addWidget(scroll)

    def _probe_gpus_async(self) -> None:
        if self._gpu_worker is not None and self._gpu_worker.isRunning():
            return
        self._gpu_worker = GpuProbeWorker(self)
        self._gpu_worker.gpusDetected.connect(self._on_gpus_detected)
        self._gpu_worker.start()

    def _on_gpus_detected(self, gpus: list[tuple[int, str]]) -> None:
        current_id = self.combo_gpu.currentData()
        self.combo_gpu.clear()
        for dev_id, name in gpus:
            self.combo_gpu.addItem(name, dev_id)
        idx = self.combo_gpu.findData(current_id)
        if idx >= 0:
            self.combo_gpu.setCurrentIndex(idx)

    def build_command(self) -> list[str] | None:
        """Build the delphis-cli upscale command arguments, or None if required paths are missing."""
        in_path = self.input_picker.path()
        out_path = self.output_picker.path()

        if not in_path or not out_path:
            return None

        scale_val = self.combo_scale.currentData()
        model_val = self.combo_model.currentData()
        alpha_mode_val = self.combo_alpha.currentData()
        gpu_val = self.combo_gpu.currentData()

        cmd = [
            "upscale", in_path, out_path,
            "--scale", str(scale_val),
            "--model", str(model_val),
            "--alpha-mode", str(alpha_mode_val),
            "--gpu", str(gpu_val),
        ]

        if not self.chk_recursive.isChecked():
            cmd.append("--no-recursive")

        return cmd

    def execute_action(self):
        """Execute AI upscale task from top action bar."""
        cmd = self.build_command()
        if cmd is None:
            QMessageBox.warning(self, "Missing Paths", "Please specify both Input Path and Output Folder.")
            return

        self.requestWorker.emit(cmd)

    def save_settings(self, settings):
        """Persist Upscale tab UI state."""
        settings.beginGroup("upscale")
        settings.setValue("input_path", self.input_picker.path())
        settings.setValue("output_path", self.output_picker.path())
        settings.setValue("scale_index", self.combo_scale.currentIndex())
        settings.setValue("model_index", self.combo_model.currentIndex())
        settings.setValue("alpha_mode_index", self.combo_alpha.currentIndex())
        settings.setValue("gpu_index", self.combo_gpu.currentIndex())
        settings.setValue("recursive", self.chk_recursive.isChecked())
        settings.endGroup()

    def load_settings(self, settings):
        """Restore Upscale tab UI state from QSettings."""
        settings.beginGroup("upscale")
        in_path = settings.value("input_path", "")
        if in_path:
            self.input_picker.setPath(in_path)
        out_path = settings.value("output_path", "")
        if out_path:
            self.output_picker.setPath(out_path)

        scale_idx = settings.value("scale_index", 2, type=int)
        if 0 <= scale_idx < self.combo_scale.count():
            self.combo_scale.setCurrentIndex(scale_idx)

        model_idx = settings.value("model_index", 0, type=int)
        if 0 <= model_idx < self.combo_model.count():
            self.combo_model.setCurrentIndex(model_idx)

        alpha_idx = settings.value("alpha_mode_index", 0, type=int)
        if 0 <= alpha_idx < self.combo_alpha.count():
            self.combo_alpha.setCurrentIndex(alpha_idx)

        gpu_idx = settings.value("gpu_index", 0, type=int)
        if 0 <= gpu_idx < self.combo_gpu.count():
            self.combo_gpu.setCurrentIndex(gpu_idx)

        self.chk_recursive.setChecked(settings.value("recursive", True, type=bool))
        settings.endGroup()

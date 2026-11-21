import gc
import logging
import threading
from pathlib import Path
from typing import Any, Dict, Optional, Tuple

import torch

from src.model import ResidualUNet3D

logger = logging.getLogger("neuroscan.worker.manager")


def enable_mc_dropout(model: torch.nn.Module) -> None:
    model.eval()
    for m in model.modules():
        if isinstance(m, (torch.nn.Dropout, torch.nn.Dropout2d, torch.nn.Dropout3d)):
            m.train()


class ModelManager:
    """Thread-safe atomic model & calibration hot-reload manager."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self.model: Optional[ResidualUNet3D] = None
        self.calibration: Dict[str, float] = {
            "a_wt": 1.75,
            "a_tc": 2.867,
            "a_et": 2.6526,
        }
        self.current_version: str = "v1.0.0"
        self.weights_path: str = ""

    def hot_reload(
        self,
        new_version: str,
        weights_path: Path,
        calib_dict: Dict[str, Any],
        device: torch.device,
    ) -> None:
        logger.info("Loading model version '%s' from %s on %s...", new_version, weights_path, device)
        new_model = ResidualUNet3D(
            in_channels=4,
            num_classes=4,
            features=(16, 32, 64, 128),
            bottleneck=256,
            dropout=0.2,
        )

        if weights_path.exists():
            ckpt = torch.load(str(weights_path), map_location="cpu", weights_only=False)
            if isinstance(ckpt, dict) and "model_state_dict" in ckpt:
                new_model.load_state_dict(ckpt["model_state_dict"])
            elif isinstance(ckpt, dict):
                new_model.load_state_dict(ckpt)

        new_model.to(device)
        enable_mc_dropout(new_model)

        factors = calib_dict.get("calibration_factors", calib_dict)
        normalized_calib = {
            "a_wt": float(factors.get("a_wt", 1.75)),
            "a_tc": float(factors.get("a_tc", 2.867)),
            "a_et": float(factors.get("a_et", 2.6526)),
        }

        # Brief atomic pointer swap so in-flight requests finishing on old_model are never interrupted.
        with self._lock:
            old_model = self.model
            self.model = new_model
            self.calibration = normalized_calib
            self.current_version = new_version
            self.weights_path = str(weights_path)

        del old_model
        gc.collect()
        if device.type == "cuda":
            torch.cuda.empty_cache()

        logger.info(
            "Atomic hot-reload complete -> version='%s', calibration=%s",
            new_version,
            normalized_calib,
        )

    def get_active_artifacts(self) -> Tuple[Optional[ResidualUNet3D], Dict[str, float], str]:
        with self._lock:
            return self.model, dict(self.calibration), self.current_version

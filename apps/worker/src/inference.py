import json
import io
import os
import uuid
import tempfile
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple, Union

import nibabel as nib
import numpy as np
from scipy import ndimage
import torch

from manager import ModelManager, enable_mc_dropout
from .config import settings
from .model import ResidualUNet3D
from .calibration import calibration_manager


class InferenceEngine:
    def __init__(self):
        self.model_manager = ModelManager()
        self.device = self._resolve_device()
        self.temp_dir = Path(__file__).resolve().parent.parent / "temp"
        self.temp_dir.mkdir(parents=True, exist_ok=True)
        self.hot_reload_dir = Path(__file__).resolve().parent.parent / "weights" / "hot_reload"
        self.hot_reload_dir.mkdir(parents=True, exist_ok=True)

    @property
    def model(self) -> ResidualUNet3D | None:
        return self.model_manager.model

    @property
    def current_version(self) -> str:
        return self.model_manager.current_version

    def _resolve_device(self) -> torch.device:
        target = settings.DEVICE.lower()
        if target == "cuda" and torch.cuda.is_available():
            return torch.device("cuda")
        if target == "mps" and hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
            return torch.device("mps")
        return torch.device("cpu")

    def load_model(self) -> None:
        weights_path = Path(settings.MODEL_WEIGHTS_PATH)
        if not weights_path.exists():
            root_fallback = Path(__file__).resolve().parents[3] / "working_unet3d_final_fp32.pth"
            if root_fallback.exists():
                weights_path = root_fallback

        calib_dict = {"calibration_factors": dict(calibration_manager.cached_factors)}
        calib_path = Path(settings.CALIBRATION_CACHE_PATH)
        if calib_path.exists():
            try:
                calib_dict = json.loads(calib_path.read_text(encoding="utf-8"))
            except Exception:
                pass

        self.model_manager.hot_reload(
            new_version=settings.MODEL_VERSION,
            weights_path=weights_path,
            calib_dict=calib_dict,
            device=self.device,
        )

    def reload_from_paths(self, version: str, weights_path: Path, calib_path: Path) -> None:
        calib_dict = json.loads(calib_path.read_text(encoding="utf-8"))
        self.model_manager.hot_reload(
            new_version=version,
            weights_path=weights_path,
            calib_dict=calib_dict,
            device=self.device,
        )

    @staticmethod
    def p95_normalize(volume: np.ndarray) -> np.ndarray:
        vol = volume.astype(np.float32, copy=False)
        nonzero = vol[vol != 0]
        if nonzero.size == 0:
            return np.zeros_like(vol, dtype=np.float32)

        p95 = np.percentile(nonzero, 95.0)
        out = np.zeros_like(vol, dtype=np.float32)
        if p95 > 0:
            mask = vol != 0
            out[mask] = vol[mask] / p95
        return out

    def predict_mc_dropout_batched(
        self,
        image: np.ndarray,
        num_passes: Optional[int] = None,
        batch_t: Optional[int] = None,
        progress_callback: Optional[Callable[[str, str], None]] = None,
        active_model: Optional[torch.nn.Module] = None,
    ) -> Dict[str, torch.Tensor]:
        target_model = active_model if active_model is not None else self.model
        if target_model is None:
            raise RuntimeError("No active ResidualUNet3D model loaded for inference.")
        if num_passes is None:
            num_passes = settings.MC_NUM_PASSES
        if batch_t is None:
            batch_t = settings.MC_BATCH_SIZE

        _, orig_d, orig_h, orig_w = image.shape
        pd, ph, pw = settings.PATCH_DEPTH, settings.PATCH_HEIGHT, settings.PATCH_WIDTH
        overlap = settings.OVERLAP

        pad_d = max(0, pd - orig_d)
        pad_h = max(0, ph - orig_h)
        pad_w = max(0, pw - orig_w)

        if pad_d > 0 or pad_h > 0 or pad_w > 0:
            image = np.pad(
                image,
                ((0, 0), (0, pad_d), (0, pad_h), (0, pad_w)),
                mode="constant",
                constant_values=0
            )

        _, d, h, w = image.shape

        step_d = max(1, int(pd * (1 - overlap)))
        step_h = max(1, int(ph * (1 - overlap)))
        step_w = max(1, int(pw * (1 - overlap)))

        steps_d = list(range(0, max(1, d - pd + 1), step_d))
        if steps_d[-1] + pd < d:
            steps_d.append(d - pd)

        steps_h = list(range(0, max(1, h - ph + 1), step_h))
        if steps_h[-1] + ph < h:
            steps_h.append(h - ph)

        steps_w = list(range(0, max(1, w - pw + 1), step_w))
        if steps_w[-1] + pw < w:
            steps_w.append(w - pw)

        is_cuda = self.device.type == "cuda"
        # Store MC probability volumes on GPU as float16 (CUDA) or float32 (CPU)
        # to eliminate per-patch GPU->CPU copies and 3x full-volume CPU->GPU re-uploads.
        storage_dtype = torch.float16 if is_cuda else torch.float32
        prob_wt = torch.empty((num_passes, orig_d, orig_h, orig_w), dtype=storage_dtype, device=self.device)
        prob_tc = torch.empty((num_passes, orig_d, orig_h, orig_w), dtype=storage_dtype, device=self.device)
        prob_et = torch.empty((num_passes, orig_d, orig_h, orig_w), dtype=storage_dtype, device=self.device)

        enable_mc_dropout(target_model)

        with torch.no_grad():
            image_tensor = torch.from_numpy(np.ascontiguousarray(image, dtype=np.float32)).to(self.device)

            # Precompute sliding-window overlap divisor once on device.
            count_accum = torch.zeros((d, h, w), dtype=torch.float32, device=self.device)
            for sd in steps_d:
                for sh in steps_h:
                    for sw in steps_w:
                        count_accum[sd:sd + pd, sh:sh + ph, sw:sw + pw] += 1.0
            count_accum.clamp_(min=1.0)
            count_inv = (1.0 / count_accum[:orig_d, :orig_h, :orig_w]).unsqueeze(0).unsqueeze(0)
            del count_accum

            for t_step in range(0, num_passes, batch_t):
                cur_b = min(batch_t, num_passes - t_step)
                prob_accum = torch.zeros((cur_b, 4, d, h, w), dtype=torch.float32, device=self.device)

                for sd in steps_d:
                    for sh in steps_h:
                        for sw in steps_w:
                            patch = image_tensor[:, sd:sd + pd, sh:sh + ph, sw:sw + pw]
                            tensor = patch.unsqueeze(0).expand(cur_b, -1, -1, -1, -1).contiguous()

                            if is_cuda:
                                with torch.autocast(device_type="cuda", dtype=torch.float16):
                                    logits = target_model(tensor)
                            else:
                                logits = target_model(tensor)

                            probs = torch.softmax(logits.float(), dim=1)
                            prob_accum[:, :, sd:sd + pd, sh:sh + ph, sw:sw + pw].add_(probs)

                averaged = prob_accum[:, :, :orig_d, :orig_h, :orig_w] * count_inv
                p_et = averaged[:, 1]
                p_tc = p_et + averaged[:, 2]
                p_wt = p_tc + averaged[:, 3]

                end_t = t_step + cur_b
                prob_wt[t_step:end_t].copy_(p_wt.to(storage_dtype))
                prob_tc[t_step:end_t].copy_(p_tc.to(storage_dtype))
                prob_et[t_step:end_t].copy_(p_et.to(storage_dtype))

                del prob_accum, averaged, p_et, p_tc, p_wt
                if progress_callback is not None:
                    progress_callback(
                        "inference_processing",
                        f"Completed {end_t}/{num_passes} MC Dropout passes on {self.device.type.upper()}"
                    )

            del image_tensor, count_inv

        return {
            "WT": prob_wt,
            "TC": prob_tc,
            "ET": prob_et
        }

    def compute_calibrated_quantiles(
        self,
        prob_arr: Union[torch.Tensor, np.ndarray],
        voxel_vol_cm3: float,
        a_scale: float = 1.0
    ) -> Tuple[List[float], Tuple[float, float], np.ndarray, np.ndarray, np.ndarray]:
        if isinstance(prob_arr, np.ndarray):
            t_prob = torch.from_numpy(prob_arr).to(self.device)
        else:
            t_prob = prob_arr.to(self.device, non_blocking=False)

        d, h, w = t_prob.shape[1], t_prob.shape[2], t_prob.shape[3]

        valid_mask = (t_prob.max(dim=0).values > 0.01)
        q_levels = torch.linspace(0.01, 0.99, 99, dtype=torch.float32, device=self.device)

        empty_mask = np.zeros((d, h, w), dtype=bool)
        if not valid_mask.any():
            del t_prob, valid_mask, q_levels
            return [0.0] * 99, (0.0, 0.0), empty_mask, empty_mask, empty_mask

        valid_voxels = t_prob[:, valid_mask]
        del t_prob
        n_valid = valid_voxels.shape[1]

        th_low = min(0.85, 0.5 + 0.15 * min(a_scale, 3.0))
        th_high = max(0.15, 0.5 - 0.15 * min(a_scale, 3.0))

        # Chunk along voxel dimension so torch.quantile stays strictly under CUDA's 2^24 element limit.
        chunk_size = max(1, int(settings.QUANTILE_CHUNK_SIZE))
        vol_cdf_accum = torch.zeros(99, dtype=torch.float64, device=self.device)
        m05_valid_t = torch.empty(n_valid, dtype=torch.bool, device=self.device)
        m50_valid_t = torch.empty(n_valid, dtype=torch.bool, device=self.device)
        m95_valid_t = torch.empty(n_valid, dtype=torch.bool, device=self.device)

        with torch.no_grad():
            for start in range(0, n_valid, chunk_size):
                end = min(start + chunk_size, n_valid)
                chunk = valid_voxels[:, start:end].float()
                q_chunk = torch.quantile(chunk, q_levels, dim=0)
                vol_cdf_accum.add_(q_chunk.sum(dim=1, dtype=torch.float64))
                m05_valid_t[start:end] = q_chunk[4] >= th_low
                m50_valid_t[start:end] = q_chunk[49] >= 0.5
                m95_valid_t[start:end] = q_chunk[94] >= th_high
                del chunk, q_chunk

        vol_cdf = vol_cdf_accum.cpu().numpy() * voxel_vol_cm3
        vol_cdf_rounded = [round(float(v), 2) for v in vol_cdf]

        v_med = vol_cdf[49]
        v_low_raw = vol_cdf[2]
        v_high_raw = vol_cdf[96]

        raw_half_width = max((v_high_raw - v_low_raw) / 2.0, 0.05 * v_med + 0.2)
        ci_low = max(0.0, round(float(v_med - a_scale * raw_half_width), 2))
        ci_high = round(float(v_med + a_scale * raw_half_width), 2)

        m05_valid = m05_valid_t.cpu().numpy()
        m50_valid = m50_valid_t.cpu().numpy()
        m95_valid = m95_valid_t.cpu().numpy()
        valid_mask_np = valid_mask.cpu().numpy()

        mask_05 = np.zeros((d, h, w), dtype=bool)
        mask_50 = np.zeros((d, h, w), dtype=bool)
        mask_95 = np.zeros((d, h, w), dtype=bool)

        mask_05[valid_mask_np] = m05_valid
        mask_50[valid_mask_np] = m50_valid
        mask_95[valid_mask_np] = m95_valid

        del valid_voxels, valid_mask, q_levels, vol_cdf_accum, m05_valid_t, m50_valid_t, m95_valid_t
        if self.device.type == "cuda":
            torch.cuda.empty_cache()

        return vol_cdf_rounded, (ci_low, ci_high), mask_05, mask_50, mask_95

    def _compute_fast_gpu_quantiles(self, prob_arr: np.ndarray, voxel_vol_cm3: float):
        vol_cdf, _, mask_05, mask_50, mask_95 = self.compute_calibrated_quantiles(prob_arr, voxel_vol_cm3, a_scale=1.0)
        return vol_cdf, mask_05, mask_50, mask_95

    @staticmethod
    def postprocess_volume(pred: np.ndarray, second_lcc_min: int = 100, et_min: int = 50) -> np.ndarray:
        output = pred.copy()
        tumor = output > 0
        labeled, count = ndimage.label(tumor)

        if count > 0:
            sizes = ndimage.sum(tumor, labeled, index=np.arange(1, count + 1))
            order = np.argsort(sizes)[::-1]
            keep_ids = [int(order[0] + 1)]

            if len(order) >= 2 and sizes[order[1]] >= second_lcc_min:
                keep_ids.append(int(order[1] + 1))

            keep_mask = np.isin(labeled, keep_ids)
            output[~keep_mask] = 0

        et_count = int((output == 1).sum())
        if 0 < et_count < et_min:
            output[output == 1] = 2

        return output

    @staticmethod
    def _load_nii_from_bytes(data: bytes) -> nib.Nifti1Image:
        is_gzipped = len(data) >= 2 and data[0] == 0x1F and data[1] == 0x8B
        suffix = ".nii.gz" if is_gzipped else ".nii"
        with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
            tmp.write(data)
            tmp_path = tmp.name
        try:
            img = nib.load(tmp_path)
            data_arr = np.asanyarray(img.dataobj, dtype=np.float32)
            if data_arr.ndim > 3:
                data_arr = np.squeeze(data_arr)
                if data_arr.ndim > 3:
                    data_arr = data_arr[..., 0]
            affine = img.affine.copy()
            header = img.header.copy()
            return nib.Nifti1Image(data_arr, affine, header)
        finally:
            if os.path.exists(tmp_path):
                os.remove(tmp_path)

    @staticmethod
    def _save_nii_to_file(mask_arr: np.ndarray, affine: np.ndarray, header: Any, out_path: Path) -> None:
        spatial = mask_arr.transpose(1, 2, 0).astype(np.uint8)
        new_header = header.copy() if header is not None else None
        if new_header is not None:
            new_header.set_data_dtype(np.uint8)
        nii_obj = nib.Nifti1Image(spatial, affine, new_header)
        nib.save(nii_obj, str(out_path))

    def run_uq_inference(
        self,
        flair_bytes: bytes,
        t1_bytes: bytes,
        t1ce_bytes: bytes,
        t2_bytes: bytes,
        num_passes: Optional[int] = None,
        batch_t: Optional[int] = None,
        progress_callback: Optional[Callable[[str, str], None]] = None,
    ) -> Dict[str, Any]:
        if num_passes is None:
            num_passes = settings.MC_NUM_PASSES
        if batch_t is None:
            batch_t = settings.MC_BATCH_SIZE

        if progress_callback is not None:
            progress_callback("inference_started", "Loading and normalizing 4 MRI sequences")

        flair_nii = self._load_nii_from_bytes(flair_bytes)
        t1_nii = self._load_nii_from_bytes(t1_bytes)
        t1ce_nii = self._load_nii_from_bytes(t1ce_bytes)
        t2_nii = self._load_nii_from_bytes(t2_bytes)

        affine = flair_nii.affine
        header = flair_nii.header
        zooms = header.get_zooms()[:3]
        voxel_vol_cm3 = float(abs(zooms[0] * zooms[1] * zooms[2])) / 1000.0

        flair_data = self.p95_normalize(flair_nii.get_fdata(dtype=np.float32))
        t1_data = self.p95_normalize(t1_nii.get_fdata(dtype=np.float32))
        t1ce_data = self.p95_normalize(t1ce_nii.get_fdata(dtype=np.float32))
        t2_data = self.p95_normalize(t2_nii.get_fdata(dtype=np.float32))

        if not (flair_data.shape == t1_data.shape == t1ce_data.shape == t2_data.shape):
            raise ValueError(
                f"MRI sequence shapes do not match: "
                f"FLAIR={flair_data.shape}, T1={t1_data.shape}, "
                f"T1ce={t1ce_data.shape}, T2={t2_data.shape}"
            )

        # Atomically snapshot (model, calibration, version) so hot-reload never disrupts an active case.
        active_model, factors, active_version = self.model_manager.get_active_artifacts()
        if active_model is None:
            raise RuntimeError("Worker model is not loaded yet.")

        stacked = np.stack([flair_data, t1_data, t1ce_data, t2_data], axis=0).transpose(0, 3, 1, 2)
        d, h, w = stacked.shape[1], stacked.shape[2], stacked.shape[3]

        mc_probs = self.predict_mc_dropout_batched(
            stacked,
            num_passes=num_passes,
            batch_t=batch_t,
            progress_callback=progress_callback,
            active_model=active_model,
        )

        if progress_callback is not None:
            progress_callback("inference_processing", "Computing conformal quantiles on GPU")

        a_wt = float(factors.get("a_wt", 1.75))
        a_tc = float(factors.get("a_tc", 2.867))
        a_et = float(factors.get("a_et", 2.6526))

        wt_cdf, wt_ci, wt_05, wt_50, wt_95 = self.compute_calibrated_quantiles(mc_probs.pop("WT"), voxel_vol_cm3, a_wt)
        tc_cdf, tc_ci, tc_05, tc_50, tc_95 = self.compute_calibrated_quantiles(mc_probs.pop("TC"), voxel_vol_cm3, a_tc)
        et_cdf, et_ci, et_05, et_50, et_95 = self.compute_calibrated_quantiles(mc_probs.pop("ET"), voxel_vol_cm3, a_et)

        def create_multilabel_mask(wt_m: np.ndarray, tc_m: np.ndarray, et_m: np.ndarray) -> np.ndarray:
            m = np.zeros((d, h, w), dtype=np.uint8)
            m[wt_m] = 3
            m[tc_m] = 2
            m[et_m] = 1
            return self.postprocess_volume(m, second_lcc_min=100, et_min=50)

        mask_p05 = create_multilabel_mask(wt_05, tc_05, et_05)
        mask_p50 = create_multilabel_mask(wt_50, tc_50, et_50)
        mask_p95 = create_multilabel_mask(wt_95, tc_95, et_95)

        task_id = str(uuid.uuid4())
        p05_path = self.temp_dir / f"{task_id}_p05.nii.gz"
        p50_path = self.temp_dir / f"{task_id}_p50.nii.gz"
        p95_path = self.temp_dir / f"{task_id}_p95.nii.gz"

        self._save_nii_to_file(mask_p05, affine, header, p05_path)
        self._save_nii_to_file(mask_p50, affine, header, p50_path)
        self._save_nii_to_file(mask_p95, affine, header, p95_path)

        percentiles_range = list(range(1, 100))

        return {
            "task_id": task_id,
            "model_version": active_version,
            "masks": {
                "p05_url": f"/api/mask/{task_id}?p=5",
                "p50_url": f"/api/mask/{task_id}?p=50",
                "p95_url": f"/api/mask/{task_id}?p=95"
            },
            "volumes_cm3": {
                "WT": {
                    "median": wt_cdf[49],
                    "ci_95": [wt_ci[0], wt_ci[1]],
                    "percentiles": percentiles_range,
                    "volumes_cm3": wt_cdf
                },
                "TC": {
                    "median": tc_cdf[49],
                    "ci_95": [tc_ci[0], tc_ci[1]],
                    "percentiles": percentiles_range,
                    "volumes_cm3": tc_cdf
                },
                "ET": {
                    "median": et_cdf[49],
                    "ci_95": [et_ci[0], et_ci[1]],
                    "percentiles": percentiles_range,
                    "volumes_cm3": et_cdf
                }
            },
            "calibration_factors": {
                "a_wt": a_wt,
                "a_tc": a_tc,
                "a_et": a_et
            }
        }

    def get_mask_file_path(self, task_id: str, p: int) -> Path | None:
        p_str = f"p0{p}" if p < 10 else f"p{p}"
        target = self.temp_dir / f"{task_id}_{p_str}.nii.gz"
        if target.exists():
            return target
        return None

    def read_mask_bytes(self, task_id: str, p: int) -> bytes:
        target = self.get_mask_file_path(task_id, p)
        if target is None:
            raise FileNotFoundError(f"Generated mask not found for task={task_id}, p={p}")
        return target.read_bytes()

    def cleanup_task_masks(self, task_id: str) -> None:
        for p in (5, 50, 95):
            target = self.get_mask_file_path(task_id, p)
            if target is not None and target.exists():
                target.unlink(missing_ok=True)

inference_engine = InferenceEngine()

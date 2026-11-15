import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import nibabel as nib
import numpy as np
import scipy.ndimage as ndimage
import torch
import torch.nn.functional as F
from tqdm import tqdm

try:
    from .models import ResidualUNet3D
except ImportError:
    from models import ResidualUNet3D


def p95_normalize(vol: np.ndarray) -> np.ndarray:
    """
    95th percentile intensity normalization over non-zero brain voxels.
    """
    vol = vol.astype(np.float32, copy=False)
    nz = vol[vol != 0]
    if nz.size == 0:
        return np.zeros_like(vol, dtype=np.float32)

    p95 = np.percentile(nz, 95.0)
    out = np.zeros_like(vol, dtype=np.float32)
    if p95 > 0:
        mask = vol != 0
        out[mask] = vol[mask] / p95
    return out


def remap_seg(seg: np.ndarray) -> np.ndarray:
    """
    Normalize BraTS label encoding:
    0: Background (BG)
    1: Enhancing Tumor (ET)
    2: Necrotic and Non-Enhancing Tumor Core (NCR/NET)
    3: Peritumoral Edema (ED)
    """
    out = np.zeros(seg.shape, dtype=np.uint8)
    out[seg == 4] = 1
    out[seg == 1] = 2
    out[seg == 2] = 3
    return out


def dice_binary(pred: np.ndarray, gt: np.ndarray) -> float:
    pred_b = pred.astype(bool)
    gt_b = gt.astype(bool)
    denom = pred_b.sum() + gt_b.sum()
    if denom == 0:
        return 1.0
    return float(2.0 * np.logical_and(pred_b, gt_b).sum() / denom)


def brats_metrics(pred: np.ndarray, gt: np.ndarray) -> Dict[str, float]:
    wt = dice_binary(pred > 0, gt > 0)
    tc = dice_binary(np.isin(pred, [1, 2]), np.isin(gt, [1, 2]))
    et = dice_binary(pred == 1, gt == 1)
    return {
        "WT": wt,
        "TC": tc,
        "ET": et,
        "Mean": (wt + tc + et) / 3.0,
    }


def compute_class_weights(metas: List[Dict[str, Any]]) -> torch.Tensor:
    counts = np.zeros(4, dtype=np.float64)
    for m in metas:
        counts += np.asarray(m["class_pixel_counts"], dtype=np.float64)

    freq = counts / counts.sum()
    weights = 1.0 / np.sqrt(freq + 1e-12)
    weights /= weights.mean()
    return torch.tensor(weights, dtype=torch.float32)


def dice_ce_loss_3d(
    logits: torch.Tensor,
    target: torch.Tensor,
    class_weights: Optional[torch.Tensor] = None,
    smooth: float = 1e-5
) -> torch.Tensor:
    """
    3D Multiclass Dice + Cross-Entropy Loss (Isensee et al., 2021)
    - logits: [B, C, D, H, W]
    - target: [B, D, H, W]
    """
    ce = F.cross_entropy(logits, target.long(), weight=class_weights)

    num_classes = logits.shape[1]
    probs = torch.softmax(logits, dim=1)
    target_one_hot = F.one_hot(target.long(), num_classes=num_classes).permute(0, 4, 1, 2, 3).to(dtype=probs.dtype)

    dims = (0, 2, 3, 4)
    intersection = (probs * target_one_hot).sum(dim=dims)
    denominator = probs.sum(dim=dims) + target_one_hot.sum(dim=dims)

    dice_per_class = (2.0 * intersection + smooth) / (denominator + smooth)
    dice_loss = 1.0 - dice_per_class.mean()

    return dice_loss + ce


class EarlyStopping3D:
    def __init__(self, patience: int = 5, min_delta: float = 1e-4, mode: str = "max"):
        self.patience = patience
        self.min_delta = min_delta
        self.mode = mode
        self.counter = 0
        self.best_metric = -float("inf") if mode == "max" else float("inf")
        self.early_stop = False

    def __call__(self, current_metric: float) -> bool:
        if self.mode == "max":
            improved = current_metric > (self.best_metric + self.min_delta)
        else:
            improved = current_metric < (self.best_metric - self.min_delta)

        if improved:
            self.best_metric = current_metric
            self.counter = 0
            return True
        else:
            self.counter += 1
            if self.counter >= self.patience:
                self.early_stop = True
            return False


def postprocess_volume_3d(
    pred: np.ndarray,
    second_lcc_min_voxels: int = 100,
    et_min_voxels: int = 50
) -> np.ndarray:
    """
    Postprocessing using Connected Component Analysis:
    - Retains 1st largest component and 2nd if >= second_lcc_min_voxels
    - Replaces isolated ET predictions with NCR if total ET count < et_min_voxels
    """
    pred = pred.copy()
    tumor = pred > 0
    labeled, n = ndimage.label(tumor)

    if n > 0:
        sizes = ndimage.sum(tumor, labeled, index=np.arange(1, n + 1))
        order = np.argsort(sizes)[::-1]
        keep_ids = [int(order[0] + 1)]

        if len(order) >= 2 and sizes[order[1]] >= second_lcc_min_voxels:
            keep_ids.append(int(order[1] + 1))

        keep_mask = np.isin(labeled, keep_ids)
        pred[~keep_mask] = 0

    et_count = int((pred == 1).sum())
    if 0 < et_count < et_min_voxels:
        pred[pred == 1] = 2

    return pred


@torch.no_grad()
def predict_case_3d_sliding_window(
    model: torch.nn.Module,
    image: np.ndarray,
    patch_size: Tuple[int, int, int] = (128, 128, 128),
    overlap: float = 0.5,
    device: Optional[torch.device] = None,
    use_amp: bool = True
) -> np.ndarray:
    """
    Sliding window patch inference with linear overlap blending.
    image shape: (4, D, H, W)
    """
    if device is None:
        device = next(model.parameters()).device

    model.eval()
    C, D, H, W = image.shape
    pD, pH, pW = patch_size

    step_d = max(1, int(pD * (1 - overlap)))
    step_h = max(1, int(pH * (1 - overlap)))
    step_w = max(1, int(pW * (1 - overlap)))

    steps_d = list(range(0, max(1, D - pD + 1), step_d))
    if steps_d[-1] + pD < D:
        steps_d.append(D - pD)

    steps_h = list(range(0, max(1, H - pH + 1), step_h))
    if steps_h[-1] + pH < H:
        steps_h.append(H - pH)

    steps_w = list(range(0, max(1, W - pW + 1), step_w))
    if steps_w[-1] + pW < W:
        steps_w.append(W - pW)

    prob_accum = np.zeros((4, D, H, W), dtype=np.float32)
    count_accum = np.zeros((D, H, W), dtype=np.float32)

    is_cuda = device.type == "cuda"
    for sd in steps_d:
        for sh in steps_h:
            for sw in steps_w:
                sub_vol = image[:, sd:sd+pD, sh:sh+pH, sw:sw+pW]
                x = torch.from_numpy(np.asarray(sub_vol, dtype=np.float32)).unsqueeze(0).to(device)

                with torch.autocast(device_type="cuda", dtype=torch.float16, enabled=(use_amp and is_cuda)):
                    logits = model(x)
                    probs = torch.softmax(logits, dim=1).squeeze(0).cpu().numpy()

                prob_accum[:, sd:sd+pD, sh:sh+pH, sw:sw+pW] += probs
                count_accum[sd:sd+pD, sh:sh+pH, sw:sw+pW] += 1.0

    prob_accum /= np.maximum(count_accum, 1.0)
    pred_vol = prob_accum.argmax(axis=0).astype(np.uint8)
    return pred_vol


def evaluate_3d_dataset(
    model: torch.nn.Module,
    metas: List[Dict[str, Any]],
    device: Optional[torch.device] = None
) -> Dict[str, float]:
    rows = []
    for m in tqdm(metas, desc="3D Validation", leave=False):
        image = np.load(m["image_path"], mmap_mode="r")
        pred = predict_case_3d_sliding_window(model, image, device=device)
        gt = np.load(m["seg_path"], mmap_mode="r")
        rows.append(brats_metrics(pred, gt))

    keys = ["WT", "TC", "ET", "Mean"]
    return {k: float(np.mean([r[k] for r in rows])) for k in keys}


def load_model(
    weights_path: Optional[Path | str] = None,
    device: Optional[torch.device] = None
) -> ResidualUNet3D:
    """
    Initializes 3D Residual U-Net and loads pre-trained weights from weights folder.
    """
    if device is None:
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    if weights_path is None:
        weights_path = Path(__file__).resolve().parent.parent / "weights" / "unet3d_final_fp32.pth"

    weights_path = Path(weights_path)
    if not weights_path.exists():
        raise FileNotFoundError(f"Checkpoint not found at: {weights_path}")

    model = ResidualUNet3D(
        in_channels=4,
        num_classes=4,
        features=(16, 32, 64, 128),
        bottleneck=256,
        dropout=0.2
    ).to(device)

    ckpt = torch.load(weights_path, map_location=device)
    state_dict = ckpt["model_state_dict"] if isinstance(ckpt, dict) and "model_state_dict" in ckpt else ckpt
    model.load_state_dict(state_dict)
    model.eval()
    print(f"Loaded checkpoint successfully from: {weights_path} (device: {device})")
    return model


def run_inference_on_folder(
    model: torch.nn.Module,
    data_dir: Path | str,
    predict_dir: Path | str,
    patch_size: Tuple[int, int, int] = (128, 128, 128),
    overlap: float = 0.5,
    device: Optional[torch.device] = None
) -> List[Path]:
    """
    Abstract inference runner:
    1. Scans data_dir for cases with FLAIR, T1, T1ce, T2 sequences
    2. Runs sliding-window 3D inference and connected-component postprocessing
    3. Saves final segmentation volume to predict_dir as .nii.gz maintaining spatial affine
    """
    data_dir = Path(data_dir)
    predict_dir = Path(predict_dir)
    predict_dir.mkdir(parents=True, exist_ok=True)

    # Discover cases by matching filenames ending with _flair, _t1, _t1ce, _t2
    all_nii = list(data_dir.glob("*.nii")) + list(data_dir.glob("*.nii.gz"))
    cases: Dict[str, Dict[str, Path]] = {}

    for p in all_nii:
        name = p.name
        match = re.match(r"^(.*?)(?:_|\.)(flair|t1ce|t1|t2)(?:\.nii|\.nii\.gz)$", name, re.IGNORECASE)
        if match:
            case_id = match.group(1)
            modality = match.group(2).lower()
            if case_id not in cases:
                cases[case_id] = {}
            cases[case_id][modality] = p

    saved_outputs: List[Path] = []
    for case_id, modalities in cases.items():
        if not all(k in modalities for k in ["flair", "t1", "t1ce", "t2"]):
            print(f"Skipping incomplete case '{case_id}': missing one of [flair, t1, t1ce, t2]")
            continue

        print(f"Processing inference for case: {case_id}...")
        ref_nii = nib.load(str(modalities["flair"]))
        affine = ref_nii.affine
        header = ref_nii.header

        vols = []
        for key in ["flair", "t1", "t1ce", "t2"]:
            data = nib.load(str(modalities[key])).get_fdata(dtype=np.float32)
            vols.append(p95_normalize(data))

        # Stack to (C, D, H, W)
        image = np.stack(vols, axis=0).transpose(0, 3, 1, 2)

        raw_pred = predict_case_3d_sliding_window(
            model, image, patch_size=patch_size, overlap=overlap, device=device
        )
        refined_pred = postprocess_volume_3d(raw_pred)

        # Transpose back from (D, H, W) to (H, W, D) for original NIfTI orientation
        spatial_pred = refined_pred.transpose(1, 2, 0).astype(np.uint8)

        out_path = predict_dir / f"{case_id}_seg.nii.gz"
        out_nii = nib.Nifti1Image(spatial_pred, affine, header)
        nib.save(out_nii, str(out_path))
        print(f"Prediction saved to: {out_path}")
        saved_outputs.append(out_path)

    return saved_outputs

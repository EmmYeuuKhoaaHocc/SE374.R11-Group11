import json
import random
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import nibabel as nib
import numpy as np
import torch
from torch.utils.data import DataLoader, Dataset

try:
    from .utils import p95_normalize, remap_seg
except ImportError:
    from utils import p95_normalize, remap_seg


class FastBraTS3DPatchDataset(Dataset):
    """
    Volumetric 3D Patch Dataset with balanced foreground/background sampling
    and 3D spatial (flip) + intensity augmentations.
    """
    def __init__(
        self,
        metas: List[Dict[str, Any]],
        patch_size: Tuple[int, int, int] = (128, 128, 128),
        samples_per_volume: int = 8,
        fg_prob: float = 0.67,
        augment: bool = True
    ):
        self.metas = metas
        self.patch_size = patch_size
        self.samples_per_volume = samples_per_volume
        self.fg_prob = fg_prob
        self.augment = augment
        self.total_samples = len(metas) * samples_per_volume

    def __len__(self) -> int:
        return self.total_samples

    def _crop_patch(
        self,
        img: np.ndarray,
        seg: np.ndarray,
        fg_coords: List[List[int]]
    ) -> Tuple[np.ndarray, np.ndarray]:
        D, H, W = seg.shape
        pD, pH, pW = self.patch_size

        if random.random() < self.fg_prob and len(fg_coords) > 0:
            center = fg_coords[random.randint(0, len(fg_coords) - 1)]
            cD, cH, cW = center
            sD = max(0, min(cD - pD // 2, D - pD))
            sH = max(0, min(cH - pH // 2, H - pH))
            sW = max(0, min(cW - pW // 2, W - pW))
        else:
            sD = random.randint(0, max(0, D - pD))
            sH = random.randint(0, max(0, H - pH))
            sW = random.randint(0, max(0, W - pW))

        patch_img = img[:, sD:sD+pD, sH:sH+pH, sW:sW+pW]
        patch_seg = seg[sD:sD+pD, sH:sH+pH, sW:sW+pW]
        return patch_img, patch_seg

    def _augment(self, x: torch.Tensor, y: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor]:
        for dim in [1, 2, 3]:
            if random.random() < 0.5:
                x = torch.flip(x, dims=[dim])
                y = torch.flip(y, dims=[dim - 1])
        if random.random() < 0.5:
            scale = random.uniform(0.9, 1.1)
            shift = random.uniform(-0.05, 0.05)
            x = torch.where(x != 0, x * scale + shift, x)
        return x, y

    def __getitem__(self, idx: int) -> Tuple[torch.Tensor, torch.Tensor]:
        meta_idx = idx // self.samples_per_volume
        m = self.metas[meta_idx]

        img = np.load(m["image_path"], mmap_mode="r")
        seg = np.load(m["seg_path"], mmap_mode="r")

        patch_img, patch_seg = self._crop_patch(img, seg, m.get("fg_coords", []))

        x = torch.from_numpy(np.asarray(patch_img, dtype=np.float32).copy())
        y = torch.from_numpy(np.asarray(patch_seg, dtype=np.int64).copy())

        if self.augment:
            x, y = self._augment(x, y)
        return x, y


def find_one_nii(case_dir: Path, suffix: str) -> Optional[Path]:
    candidates = list(case_dir.glob("*.nii")) + list(case_dir.glob("*.nii.gz"))
    suffix = suffix.lower()
    for p in candidates:
        name = p.name.lower()
        if name.endswith(f"_{suffix}.nii") or name.endswith(f"_{suffix}.nii.gz"):
            return p
    return None


def discover_cases(root: Path) -> List[Dict[str, str]]:
    cases = []
    for d in sorted(root.rglob("*")):
        if not d.is_dir():
            continue

        flair = find_one_nii(d, "flair")
        t1 = find_one_nii(d, "t1")
        t1ce = find_one_nii(d, "t1ce")
        t2 = find_one_nii(d, "t2")
        seg = find_one_nii(d, "seg")

        if all([flair, t1, t1ce, t2, seg]):
            cases.append({
                "id": d.name,
                "flair": str(flair),
                "t1": str(t1),
                "t1ce": str(t1ce),
                "t2": str(t2),
                "seg": str(seg),
            })
    return cases


def cache_case_3d(case: Dict[str, str], cache_dir: Path, force: bool = False) -> Dict[str, Any]:
    cache_dir.mkdir(parents=True, exist_ok=True)
    cid = case["id"]
    image_path = cache_dir / f"{cid}_image_3d.npy"
    seg_path = cache_dir / f"{cid}_seg_3d.npy"
    meta_path = cache_dir / f"{cid}_meta_3d.json"

    if image_path.exists() and seg_path.exists() and meta_path.exists() and not force:
        return json.loads(meta_path.read_text())

    vols = []
    for key in ["flair", "t1", "t1ce", "t2"]:
        vol = nib.load(case[key]).get_fdata(dtype=np.float32)
        vols.append(p95_normalize(vol))

    seg = remap_seg(nib.load(case["seg"]).get_fdata(dtype=np.float32))

    image = np.stack(vols, axis=0).transpose(0, 3, 1, 2)
    target = seg.transpose(2, 0, 1)

    np.save(image_path, image.astype(np.float16))
    np.save(seg_path, target.astype(np.uint8))

    fg_indices = np.argwhere(target > 0)
    if len(fg_indices) > 5000:
        step = len(fg_indices) // 5000
        fg_indices = fg_indices[::step]

    counts = np.bincount(target.reshape(-1), minlength=4).astype(np.int64).tolist()

    meta = {
        "case_id": cid,
        "image_path": str(image_path),
        "seg_path": str(seg_path),
        "shape": list(target.shape),
        "fg_coords": fg_indices.tolist(),
        "class_pixel_counts": counts,
    }
    meta_path.write_text(json.dumps(meta))
    return meta


def create_train_val_loaders(
    train_metas: List[Dict[str, Any]],
    patch_size: Tuple[int, int, int] = (128, 128, 128),
    batch_size: int = 2,
    samples_per_volume: int = 8,
    num_workers: int = 2
) -> DataLoader:
    train_ds = FastBraTS3DPatchDataset(
        train_metas,
        patch_size=patch_size,
        samples_per_volume=samples_per_volume,
        fg_prob=0.67,
        augment=True
    )

    pin_memory = torch.cuda.is_available()
    train_loader = DataLoader(
        train_ds,
        batch_size=batch_size,
        shuffle=True,
        num_workers=num_workers,
        pin_memory=pin_memory,
        persistent_workers=(num_workers > 0),
        prefetch_factor=2 if num_workers > 0 else None,
        drop_last=True,
    )
    return train_loader

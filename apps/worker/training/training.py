import argparse
import time
from pathlib import Path
from typing import Optional

import numpy as np
import torch
from sklearn.model_selection import train_test_split

try:
    from .dataloader import cache_case_3d, create_train_val_loaders, discover_cases
    from .models import ResidualUNet3D
    from .utils import EarlyStopping3D, compute_class_weights, dice_ce_loss_3d, evaluate_3d_dataset
except ImportError:
    from dataloader import cache_case_3d, create_train_val_loaders, discover_cases
    from models import ResidualUNet3D
    from utils import EarlyStopping3D, compute_class_weights, dice_ce_loss_3d, evaluate_3d_dataset


def train_3d_unet(
    data_root: Path | str,
    cache_dir: Path | str,
    weights_path: Path | str,
    num_epochs: int = 50,
    batch_size: int = 2,
    lr: float = 2e-4,
    patience: int = 5,
    seed: int = 42,
    device: Optional[torch.device] = None
):
    if device is None:
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    data_root = Path(data_root)
    cache_dir = Path(cache_dir)
    weights_path = Path(weights_path)
    weights_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"Executing 3D Residual U-Net training on device: {device}")
    cases = discover_cases(data_root)
    if not cases:
        raise ValueError(f"No complete cases found in {data_root}")

    print(f"Total cases discovered: {len(cases)}")
    case_ids = [c["id"] for c in cases]
    train_ids, val_ids = train_test_split(case_ids, test_size=0.20, random_state=seed, shuffle=True)
    train_ids = set(train_ids)

    train_cases = [c for c in cases if c["id"] in train_ids]
    val_cases = [c for c in cases if c["id"] not in train_ids]
    print(f"Train cases: {len(train_cases)} | Validation cases: {len(val_cases)}")

    print("Caching datasets...")
    train_metas = [cache_case_3d(c, cache_dir) for c in train_cases]
    val_metas = [cache_case_3d(c, cache_dir) for c in val_cases]

    train_loader = create_train_val_loaders(train_metas, batch_size=batch_size)
    class_weights = compute_class_weights(train_metas).to(device)

    model = ResidualUNet3D(
        in_channels=4,
        num_classes=4,
        features=(16, 32, 64, 128),
        bottleneck=256,
        dropout=0.2
    ).to(device)

    optimizer = torch.optim.Adam(model.parameters(), lr=lr)
    early_stopping = EarlyStopping3D(patience=patience, min_delta=1e-4, mode="max")
    scaler = torch.amp.GradScaler("cuda", enabled=(device.type == "cuda"))

    best_score = -1.0
    best_epoch = 0

    print("Starting training loop...")
    for epoch in range(1, num_epochs + 1):
        model.train()
        loss_sum = 0.0
        steps = 0
        t0 = time.time()

        for x, y in train_loader:
            x = x.to(device, non_blocking=True)
            y = y.to(device, non_blocking=True)

            optimizer.zero_grad(set_to_none=True)
            with torch.autocast(device_type="cuda", dtype=torch.float16, enabled=(device.type == "cuda")):
                logits = model(x)
                loss = dice_ce_loss_3d(logits, y, class_weights=class_weights)

            scaler.scale(loss).backward()
            scaler.step(optimizer)
            scaler.update()

            loss_sum += float(loss.item())
            steps += 1

        mean_train_loss = loss_sum / max(1, steps)
        epoch_time = (time.time() - t0) / 60.0

        val_metrics = evaluate_3d_dataset(model, val_metas, device=device)
        improved = early_stopping(val_metrics["Mean"])

        msg = (
            f"Epoch {epoch:02d}/{num_epochs:02d} [{epoch_time:.2f}m] | "
            f"Train Loss: {mean_train_loss:.4f} | "
            f"Val WT: {val_metrics['WT']:.4f}, TC: {val_metrics['TC']:.4f}, "
            f"ET: {val_metrics['ET']:.4f} | Mean Dice: {val_metrics['Mean']:.4f}"
        )

        if improved:
            best_score = val_metrics["Mean"]
            best_epoch = epoch
            torch.save({
                "model_state_dict": model.state_dict(),
                "optimizer_state_dict": optimizer.state_dict(),
                "epoch": epoch,
                "best_mean_dice": best_score,
                "base_features": (16, 32, 64, 128),
                "bottleneck": 256,
            }, weights_path)
            msg += f" (BEST - saved to {weights_path.name})"
        else:
            msg += f" [No improvement: {early_stopping.counter}/{early_stopping.patience}]"

        print(msg)
        if early_stopping.early_stop:
            print(f"Early stopping triggered at epoch {epoch}")
            break

    print(f"Training finished. Best Mean Dice: {best_score:.4f} at epoch {best_epoch}")
    return best_score, best_epoch


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Train 3D Residual U-Net on BraTS")
    parser.add_argument("--data_root", type=str, default="./data", help="Root directory with BraTS cases")
    parser.add_argument("--cache_dir", type=str, default="./cache", help="Cache directory for .npy volumes")
    parser.add_argument("--weights_path", type=str, default="../weights/unet3d_final_fp32.pth", help="Checkpoint save path")
    parser.add_argument("--epochs", type=int, default=50, help="Number of training epochs")
    parser.add_argument("--batch_size", type=int, default=2, help="Patch batch size")
    parser.add_argument("--lr", type=float, default=2e-4, help="Learning rate")
    parser.add_argument("--patience", type=int, default=5, help="Early stopping patience")
    args = parser.parse_args()

    train_3d_unet(
        data_root=args.data_root,
        cache_dir=args.cache_dir,
        weights_path=args.weights_path,
        num_epochs=args.epochs,
        batch_size=args.batch_size,
        lr=args.lr,
        patience=args.patience
    )

from pathlib import Path
try:
    from .utils import load_model, run_inference_on_folder
except ImportError:
    from utils import load_model, run_inference_on_folder

if __name__ == "__main__":
    BASE_DIR = Path(__file__).resolve().parent
    weights = BASE_DIR.parent / "weights" / "unet3d_final_fp32.pth"
    model = load_model(weights)
    run_inference_on_folder(model, BASE_DIR / "data", BASE_DIR / "predict")

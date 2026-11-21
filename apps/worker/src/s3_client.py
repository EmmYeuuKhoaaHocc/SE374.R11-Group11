import logging
from pathlib import Path
from typing import Optional

import boto3
from botocore.config import Config as BotoConfig

from .config import settings

logger = logging.getLogger("neuroscan.worker.s3")


class WorkerS3Downloader:
    def __init__(self) -> None:
        self._client = None

    @property
    def is_configured(self) -> bool:
        return bool(settings.S3_ENDPOINT.strip())

    def _get_client(self):
        if not self.is_configured:
            raise RuntimeError(
                "S3_ENDPOINT is not configured. Pass --s3-endpoint (e.g. ngrok http://... to port 8333) to download hot-reloaded models."
            )
        if self._client is None:
            endpoint = settings.S3_ENDPOINT.strip()
            if not endpoint.startswith("http://") and not endpoint.startswith("https://"):
                endpoint = f"https://{endpoint}"
            self._client = boto3.client(
                "s3",
                endpoint_url=endpoint,
                aws_access_key_id=settings.S3_ACCESS_KEY or "any",
                aws_secret_access_key=settings.S3_SECRET_KEY or "any",
                region_name=settings.S3_REGION,
                config=BotoConfig(s3={"addressing_style": "path"}),
            )
        return self._client

    def download_file(self, bucket: str, key: str, dest_path: Path) -> Path:
        dest_path.parent.mkdir(parents=True, exist_ok=True)
        client = self._get_client()
        clean_key = key.lstrip("/")
        logger.info("Downloading s3://%s/%s -> %s", bucket, clean_key, dest_path)
        client.download_file(bucket, clean_key, str(dest_path))
        return dest_path


s3_downloader = WorkerS3Downloader()

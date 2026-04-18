#!/bin/bash
# 全量感知 - OCR + 模板匹配一体化
# 用法: scan.sh [截图路径]
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
API="http://127.0.0.1:5100"

IMAGE="${1:-$("$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/capture.sh")}"
if [ ! -f "$IMAGE" ]; then
    echo "{\"error\": \"截图不存在: $IMAGE\"}" >&2
    exit 1
fi

curl -s -X POST "$API/api/scan" -F "image=@$IMAGE"

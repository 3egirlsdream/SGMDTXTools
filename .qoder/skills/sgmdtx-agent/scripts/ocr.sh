#!/bin/bash
# OCR 识别 - 对截图进行文字识别
# 用法: ocr.sh [截图路径] [x,y,w,h]
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
API="http://127.0.0.1:5100"

IMAGE="${1:-$("$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/capture.sh")}"
if [ ! -f "$IMAGE" ]; then
    echo "{\"error\": \"截图不存在: $IMAGE\"}" >&2
    exit 1
fi

if [ -n "$2" ]; then
    curl -s -X POST "$API/api/ocr" -F "image=@$IMAGE" -F "region=$2"
else
    curl -s -X POST "$API/api/ocr" -F "image=@$IMAGE"
fi

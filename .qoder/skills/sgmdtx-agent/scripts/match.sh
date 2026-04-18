#!/bin/bash
# 模板匹配 - 在截图中查找已知 UI 元素
# 用法: match.sh [截图路径] [模板名1 模板名2 ...]
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
API="http://127.0.0.1:5100"

IMAGE="${1:-$("$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/capture.sh")}"
if [ ! -f "$IMAGE" ]; then
    echo "{\"error\": \"截图不存在: $IMAGE\"}" >&2
    exit 1
fi

shift
if [ $# -gt 0 ]; then
    # 构建 JSON 数组
    TEMPLATES=$(printf '%s\n' "$@" | jq -R . | jq -s .)
    curl -s -X POST "$API/api/match" -F "image=@$IMAGE" -F "templates=$TEMPLATES"
else
    curl -s -X POST "$API/api/match" -F "image=@$IMAGE"
fi

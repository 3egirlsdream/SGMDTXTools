#!/bin/bash
# 截屏 - 调用 Python API 获取最新截图，或直接使用 screenshots/ 目录下最新文件
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
SCREENSHOTS_DIR="$PROJ_DIR/screenshots"

# 获取最新截图
latest=$(ls -t "$SCREENSHOTS_DIR"/capture_*.png 2>/dev/null | head -1)
if [ -z "$latest" ]; then
    echo '{"error": "screenshots 目录下没有截图文件，请先通过 C# 控制台执行 capture 命令"}' >&2
    exit 1
fi

echo "$latest"

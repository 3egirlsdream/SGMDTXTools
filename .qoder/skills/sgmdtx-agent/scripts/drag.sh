#!/bin/bash
# 拖拽操作
# 用法: drag.sh <x1> <y1> <x2> <y2>
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"

X1="$1"; Y1="$2"; X2="$3"; Y2="$4"

if [ -z "$X1" ] || [ -z "$Y1" ] || [ -z "$X2" ] || [ -z "$Y2" ]; then
    echo '{"error": "用法: drag.sh <x1> <y1> <x2> <y2>"}' >&2
    exit 1
fi

if command -v cliclick &>/dev/null; then
    WINDOW_INFO="$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/.window_offset"
    if [ -f "$WINDOW_INFO" ]; then
        source "$WINDOW_INFO"
        SX1=$((WINDOW_X + X1)); SY1=$((WINDOW_Y + Y1))
        SX2=$((WINDOW_X + X2)); SY2=$((WINDOW_Y + Y2))
        cliclick dd:"$SX1,$SY1" du:"$SX2,$SY2"
        echo "{\"action\": \"drag\", \"from\": [$X1,$Y1], \"to\": [$X2,$Y2]}"
    else
        echo "{\"error\": \"未找到窗口位置信息\"}" >&2
        exit 1
    fi
else
    echo "{\"error\": \"请安装 cliclick 或通过 C# 控制台执行 drag $X1,$Y1 $X2,$Y2\"}" >&2
    exit 1
fi

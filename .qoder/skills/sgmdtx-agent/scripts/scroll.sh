#!/bin/bash
# 滚轮操作
# 用法: scroll.sh <x> <y> <up|down> [次数]
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"

X="$1"; Y="$2"; DIR="$3"; CLICKS="${4:-3}"

if [ -z "$X" ] || [ -z "$Y" ] || [ -z "$DIR" ]; then
    echo '{"error": "用法: scroll.sh <x> <y> <up|down> [次数]"}' >&2
    exit 1
fi

if command -v cliclick &>/dev/null; then
    WINDOW_INFO="$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/.window_offset"
    if [ -f "$WINDOW_INFO" ]; then
        source "$WINDOW_INFO"
        SX=$((WINDOW_X + X)); SY=$((WINDOW_Y + Y))
        cliclick m:"$SX,$SY"
        for i in $(seq 1 "$CLICKS"); do
            if [ "$DIR" = "up" ]; then
                cliclick "ku:arrow-up"
            else
                cliclick "ku:arrow-down"
            fi
        done
        echo "{\"action\": \"scroll\", \"x\": $X, \"y\": $Y, \"direction\": \"$DIR\", \"clicks\": $CLICKS}"
    else
        echo "{\"error\": \"未找到窗口位置信息\"}" >&2
        exit 1
    fi
else
    echo "{\"error\": \"请安装 cliclick 或通过 C# 控制台执行 scroll $X,$Y $DIR $CLICKS\"}" >&2
    exit 1
fi

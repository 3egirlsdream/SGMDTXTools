#!/bin/bash
# 点击操作 - 在游戏窗口指定坐标点击
# 用法: click.sh <x> <y>
# 依赖: 需要 C# 控制台正在运行，通过其 stdin 管道发送命令
# 或者直接通过 cliclick (macOS) 模拟
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"

X="$1"
Y="$2"

if [ -z "$X" ] || [ -z "$Y" ]; then
    echo '{"error": "用法: click.sh <x> <y>"}' >&2
    exit 1
fi

# macOS: 使用 cliclick 工具 (brew install cliclick)
if command -v cliclick &>/dev/null; then
    # 需要知道窗口在屏幕上的绝对位置
    # 从最新的窗口信息获取偏移
    WINDOW_INFO="$PROJ_DIR/.qoder/skills/sgmdtx-agent/scripts/.window_offset"
    if [ -f "$WINDOW_INFO" ]; then
        source "$WINDOW_INFO"  # 设置 WINDOW_X, WINDOW_Y
        SCREEN_X=$((WINDOW_X + X))
        SCREEN_Y=$((WINDOW_Y + Y))
        cliclick c:"$SCREEN_X,$SCREEN_Y"
        echo "{\"action\": \"click\", \"x\": $X, \"y\": $Y, \"screen_x\": $SCREEN_X, \"screen_y\": $SCREEN_Y}"
    else
        echo "{\"error\": \"未找到窗口位置信息，请先运行 window_info.sh\"}" >&2
        exit 1
    fi
else
    echo "{\"error\": \"请安装 cliclick: brew install cliclick，或通过 C# 控制台执行 click $X,$Y\"}" >&2
    exit 1
fi

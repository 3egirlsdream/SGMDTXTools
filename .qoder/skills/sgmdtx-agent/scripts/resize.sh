#!/bin/bash
# 调整游戏窗口为目标分辨率
# 用法: resize.sh [WxH]  (默认 1280x720)
API="http://127.0.0.1:5200"

SIZE="${1:-1280x720}"
W=$(echo "$SIZE" | cut -dx -f1)
H=$(echo "$SIZE" | cut -dx -f2)

curl -s -X POST "$API/api/resize" \
  -H "Content-Type: application/json" \
  -d "{\"width\":$W,\"height\":$H}"

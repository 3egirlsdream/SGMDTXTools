#!/bin/bash
# 滚轮操作
# 用法: scroll.sh <x> <y> <up|down> [次数]
API="http://127.0.0.1:5200"

X="$1"; Y="$2"; DIR="$3"; CLICKS="${4:-3}"

if [ -z "$X" ] || [ -z "$Y" ] || [ -z "$DIR" ]; then
    echo '{"error": "用法: scroll.sh <x> <y> <up|down> [次数]"}' >&2
    exit 1
fi

curl -s -X POST "$API/api/scroll" \
  -H "Content-Type: application/json" \
  -d "{\"x\":\"$X\",\"y\":\"$Y\",\"direction\":\"$DIR\",\"clicks\":$CLICKS}"

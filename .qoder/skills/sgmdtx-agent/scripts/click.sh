#!/bin/bash
# 点击操作
# 用法: click.sh <x> <y> [right]
API="http://127.0.0.1:5200"

X="$1"; Y="$2"; BUTTON="${3:-left}"

if [ -z "$X" ] || [ -z "$Y" ]; then
    echo '{"error": "用法: click.sh <x> <y> [right]"}' >&2
    exit 1
fi

curl -s -X POST "$API/api/click" \
  -H "Content-Type: application/json" \
  -d "{\"x\":\"$X\",\"y\":\"$Y\",\"button\":\"$BUTTON\"}"

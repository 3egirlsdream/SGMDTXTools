#!/bin/bash
# 拖拽操作
# 用法: drag.sh <x1> <y1> <x2> <y2>
API="http://127.0.0.1:5200"

X1="$1"; Y1="$2"; X2="$3"; Y2="$4"

if [ -z "$X1" ] || [ -z "$Y1" ] || [ -z "$X2" ] || [ -z "$Y2" ]; then
    echo '{"error": "用法: drag.sh <x1> <y1> <x2> <y2>"}' >&2
    exit 1
fi

curl -s -X POST "$API/api/drag" \
  -H "Content-Type: application/json" \
  -d "{\"x1\":\"$X1\",\"y1\":\"$Y1\",\"x2\":\"$X2\",\"y2\":\"$Y2\"}"

#!/bin/bash
# 模板匹配 - 截图 + 匹配指定模板
# 用法: match.sh [模板名...]
API="http://127.0.0.1:5200"

if [ -n "$1" ]; then
    # 构建 JSON 数组
    TEMPLATES="[\"$1\""
    shift
    for t in "$@"; do
        TEMPLATES="$TEMPLATES,\"$t\""
    done
    TEMPLATES="$TEMPLATES]"

    curl -s -X POST "$API/api/match" \
      -H "Content-Type: application/json" \
      -d "{\"templates\":$TEMPLATES}"
else
    curl -s -X POST "$API/api/match"
fi

#!/bin/bash
# OCR 识别 - 截图 + 文字识别
# 用法: ocr.sh [x,y,w,h]
API="http://127.0.0.1:5200"

if [ -n "$1" ]; then
    curl -s -X POST "$API/api/ocr" \
      -H "Content-Type: application/json" \
      -d "{\"region\":\"$1\"}"
else
    curl -s -X POST "$API/api/ocr"
fi

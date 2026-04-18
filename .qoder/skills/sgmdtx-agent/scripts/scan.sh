#!/bin/bash
# 全量感知 - 截图 + OCR + 模板匹配 (一次调用完成)
# 用法: scan.sh
API="http://127.0.0.1:5200"
curl -s -X POST "$API/api/scan"

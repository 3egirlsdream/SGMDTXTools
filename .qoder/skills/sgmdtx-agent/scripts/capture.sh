#!/bin/bash
# 截屏 - 调用 C# HTTP API 实时截取游戏画面
API="http://127.0.0.1:5200"
curl -s -X POST "$API/api/capture"

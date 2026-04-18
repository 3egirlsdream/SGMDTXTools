#!/bin/bash
# 调整游戏窗口为目标分辨率
# 用法: resize.sh [宽x高]  (默认 1280x720)
# 说明: 窗口自动调整由 C# 控制台在感知命令 (scan/ocr/match/capture) 前自动执行。
#       此脚本用于手动触发调整或查看当前信息。
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"

SIZE="${1:-1280x720}"

echo "{\"info\": \"窗口自动调整由 C# 控制台在感知命令前自动执行\", \"target\": \"$SIZE\", \"hint\": \"在 C# 控制台中执行: resize $SIZE\"}"

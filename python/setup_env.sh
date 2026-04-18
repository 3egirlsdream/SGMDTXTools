#!/bin/bash
# 跨平台 Python venv 初始化脚本
# 适用于 Windows Git Bash / macOS / Linux
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

VENV_DIR="venv"
REQ_FILE="requirements.txt"

# --- 检测 Python ---
PYTHON=""
for cmd in python3 python; do
    if command -v "$cmd" &>/dev/null; then
        ver=$("$cmd" --version 2>&1 | grep -oP '\d+\.\d+' | head -1)
        major=$(echo "$ver" | cut -d. -f1)
        minor=$(echo "$ver" | cut -d. -f2)
        if [ "$major" -ge 3 ] && [ "$minor" -ge 9 ]; then
            PYTHON="$cmd"
            break
        fi
    fi
done

if [ -z "$PYTHON" ]; then
    echo "[ERROR] 未找到 Python 3.9+，请先安装 Python。"
    exit 1
fi

echo "[INFO] 使用 Python: $($PYTHON --version 2>&1)"

# --- 检测平台 ---
IS_WINDOWS=false
if [[ "$(uname -s)" == MINGW* ]] || [[ "$(uname -s)" == MSYS* ]] || [[ "$(uname -s)" == CYGWIN* ]] || [[ -n "$WINDIR" ]]; then
    IS_WINDOWS=true
fi

# --- 检查已有 venv 是否匹配当前平台 ---
NEED_CREATE=true

if [ -d "$VENV_DIR" ]; then
    if $IS_WINDOWS && [ -f "$VENV_DIR/Scripts/python.exe" ]; then
        echo "[INFO] 已存在 Windows venv，跳过创建。"
        NEED_CREATE=false
    elif ! $IS_WINDOWS && [ -f "$VENV_DIR/bin/python3" ]; then
        echo "[INFO] 已存在 Unix venv，跳过创建。"
        NEED_CREATE=false
    else
        echo "[WARN] 发现其他平台的 venv，删除后重建..."
        rm -rf "$VENV_DIR"
    fi
fi

# --- 创建 venv ---
if $NEED_CREATE; then
    echo "[INFO] 创建 Python 虚拟环境..."
    "$PYTHON" -m venv "$VENV_DIR"
    echo "[INFO] venv 创建完成。"
fi

# --- 激活 venv 并安装依赖 ---
if $IS_WINDOWS; then
    PIP="$VENV_DIR/Scripts/pip.exe"
    VENV_PYTHON="$VENV_DIR/Scripts/python.exe"
else
    PIP="$VENV_DIR/bin/pip"
    VENV_PYTHON="$VENV_DIR/bin/python3"
fi

if [ ! -f "$REQ_FILE" ]; then
    echo "[ERROR] 未找到 $REQ_FILE"
    exit 1
fi

echo "[INFO] 安装依赖 ($REQ_FILE)..."
"$VENV_PYTHON" -m pip install --upgrade pip -q
"$PIP" install -r "$REQ_FILE"

echo ""
echo "========================================"
echo " venv 初始化完成!"
echo " Python: $($VENV_PYTHON --version 2>&1)"
echo " 路径:   $VENV_DIR/"
echo "========================================"
echo ""
echo "启动服务: $VENV_PYTHON run.py"

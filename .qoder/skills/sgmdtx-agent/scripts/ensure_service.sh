#!/bin/bash
# 确保 Python 感知服务正在运行
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
API="http://127.0.0.1:5100"

# 检查服务是否已在运行
if curl -s --max-time 2 "$API/api/health" | grep -q '"status":"ok"'; then
    echo '{"status": "running"}'
    exit 0
fi

# 启动服务
PYTHON="$PROJ_DIR/python/venv/bin/python3"
if [ ! -f "$PYTHON" ]; then
    PYTHON="$PROJ_DIR/python/venv/Scripts/python.exe"
fi
if [ ! -f "$PYTHON" ]; then
    PYTHON="python3"
fi

echo "启动 Python 感知服务..." >&2
cd "$PROJ_DIR/python"
PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK=True "$PYTHON" run.py &
PID=$!

# 等待服务就绪
for i in $(seq 1 30); do
    sleep 1
    if curl -s --max-time 2 "$API/api/health" | grep -q '"status":"ok"'; then
        echo "{\"status\": \"started\", \"pid\": $PID}"
        exit 0
    fi
done

echo '{"error": "服务启动超时"}' >&2
exit 1

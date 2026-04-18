#!/bin/bash
# 确保 C# 控制台和 Python 感知服务正在运行
API_CS="http://127.0.0.1:5200"
API_PY="http://127.0.0.1:5100"

# 检查 C# HTTP API
if ! curl -s --max-time 2 "$API_CS/api/health" | grep -q '"status"'; then
    echo '{"error": "C# 控制台未启动。请先运行: dotnet run --project src/SGMDTXTools.Console -- <进程名>"}' >&2
    exit 1
fi

# 检查 Python 感知服务（由 C# 自动管理，这里只确认状态）
PY_OK=false
if curl -s --max-time 2 "$API_PY/api/health" | grep -q '"status"'; then
    PY_OK=true
fi

CS_STATUS=$(curl -s --max-time 2 "$API_CS/api/health")
echo "{\"cs_api\": \"running\", \"python_api\": \"$PY_OK\", \"detail\": $CS_STATUS}"

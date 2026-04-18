#!/bin/bash
# 知识库操作
# 用法:
#   kb.sh list                      - 列出所有知识文件
#   kb.sh read <文件名>             - 读取知识文件
#   kb.sh search <关键词>           - 搜索知识
#   kb.sh write <文件名> <内容>     - 写入(覆盖)知识文件
#   kb.sh append <文件名> <内容>    - 追加内容到知识文件
PROJ_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"
KB_DIR="$PROJ_DIR/knowledge"

CMD="$1"
shift

case "$CMD" in
    list|ls)
        for f in "$KB_DIR"/*.md; do
            [ -f "$f" ] || continue
            name=$(basename "$f")
            lines=$(wc -l < "$f" | tr -d ' ')
            title=$(head -5 "$f" | grep '^# ' | head -1 | sed 's/^# //')
            echo "$name ($lines 行) - $title"
        done
        ;;
    read)
        FILE="$1"
        [ -z "$FILE" ] && { echo "用法: kb.sh read <文件名>" >&2; exit 1; }
        # 自动补 .md 后缀
        [[ "$FILE" != *.md ]] && FILE="$FILE.md"
        if [ -f "$KB_DIR/$FILE" ]; then
            cat "$KB_DIR/$FILE"
        else
            echo "知识文件不存在: $FILE" >&2
            exit 1
        fi
        ;;
    search)
        KEYWORD="$1"
        [ -z "$KEYWORD" ] && { echo "用法: kb.sh search <关键词>" >&2; exit 1; }
        grep -rn -i "$KEYWORD" "$KB_DIR"/*.md 2>/dev/null || echo "未找到: $KEYWORD"
        ;;
    write)
        FILE="$1"; shift
        CONTENT="$*"
        [ -z "$FILE" ] || [ -z "$CONTENT" ] && { echo "用法: kb.sh write <文件名> <内容>" >&2; exit 1; }
        [[ "$FILE" != *.md ]] && FILE="$FILE.md"
        echo "$CONTENT" > "$KB_DIR/$FILE"
        echo "已写入: $FILE"
        ;;
    append)
        FILE="$1"; shift
        CONTENT="$*"
        [ -z "$FILE" ] || [ -z "$CONTENT" ] && { echo "用法: kb.sh append <文件名> <内容>" >&2; exit 1; }
        [[ "$FILE" != *.md ]] && FILE="$FILE.md"
        echo "" >> "$KB_DIR/$FILE"
        echo "$CONTENT" >> "$KB_DIR/$FILE"
        echo "已追加到: $FILE"
        ;;
    *)
        echo "知识库命令: list, read, search, write, append" >&2
        exit 1
        ;;
esac

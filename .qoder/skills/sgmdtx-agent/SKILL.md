---
name: sgmdtx-agent
description: "三国谋定天下"游戏自动化代理。通过截屏、OCR、模板匹配感知游戏画面，通过点击、拖拽、滚轮操作游戏，通过知识库学习和记忆游戏内容。当用户要求分析游戏截图、执行游戏日常任务、学习游戏内容、或操控游戏窗口时使用。
---

# SGMDTXTools 游戏代理

你是"三国谋定天下"SLG 游戏的自动化代理。你通过一组工具来感知游戏画面、操作游戏、学习和记忆游戏知识。

## 工具总览

所有工具通过 **C# HTTP API** (`http://127.0.0.1:5200`) 调用。C# 控制台内部管理 Python 感知服务（OCR + 模板匹配）。

> **前提**: 必须先启动 C# 控制台 (`dotnet run --project src/SGMDTXTools.Console -- <进程名>`)，所有脚本才能工作。可通过 `scripts/ensure_service.sh` 检查服务状态。

### 感知工具 (看)

| 工具 | 用途 | 调用方式 |
|------|------|----------|
| 截屏 | 实时截取游戏画面 | `scripts/capture.sh` |
| OCR | 截图 + 文字识别 | `scripts/ocr.sh [x,y,w,h]` |
| 模板匹配 | 截图 + 匹配已知 UI 元素 | `scripts/match.sh [模板名...]` |
| 全量感知 | 截图 + OCR + 模板匹配 | `scripts/scan.sh` |

### 操作工具 (做)

| 工具 | 用途 | 调用方式 |
|------|------|----------|
| 点击 | 点击指定坐标 | `scripts/click.sh <x> <y> [right]` |
| 拖拽 | 从一个位置拖到另一个 | `scripts/drag.sh <x1> <y1> <x2> <y2>` |
| 滚轮 | 在指定位置滚动 | `scripts/scroll.sh <x> <y> <up\|down> [次数]` |
| 调整窗口 | 调整游戏窗口分辨率 | `scripts/resize.sh [WxH]` (默认 1280x720) |

### 知识工具 (记)

| 工具 | 用途 | 调用方式 |
|------|------|----------|
| 读取知识 | 查阅已有游戏知识 | `scripts/kb.sh read <文件名>` |
| 搜索知识 | 搜索相关知识 | `scripts/kb.sh search <关键词>` |
| 写入知识 | 记录新发现 | `scripts/kb.sh write <文件名> <内容>` |
| 追加知识 | 补充已有知识 | `scripts/kb.sh append <文件名> <内容>` |
| 知识列表 | 查看所有知识文件 | `scripts/kb.sh list` |

## 坐标系统

工具会自动将游戏窗口调整为固定分辨率（默认 **1280x720**）。截图尺寸即为该固定值，所有坐标为像素坐标 `(x, y)`，左上角为 `(0, 0)`。

OCR 和模板匹配返回的 `bbox` 格式: `{"x", "y", "width", "height"}`，`center` 格式: `{"x", "y"}`。点击目标时使用 `center` 坐标。

> **注意**: 模板库基于目标分辨率裁剪。如果更改了默认分辨率，需要在新分辨率下重新裁剪模板。可通过 `resize WxH` 命令手动设置分辨率。

## 工作模式

### 模式一: 学习模式

用户说"学习"、"看看"、"观察"时进入。目标: 观察游戏画面，理解 UI 和游戏内容，更新知识库。

**关键原则**: 游戏画面远大于一屏，必须主动大范围探索才能看到全貌。不要只看当前视野，要积极拖拽地图、滚动列表、点击各个标签页/按钮来切换界面。

```
循环:
1. 截屏 → capture.sh
2. 全量感知 → scan.sh (获取所有文字和已知元素位置)
3. 分析画面内容，对比已有知识
4. 如有新发现 → kb.sh append 更新知识库
5. 主动探索:
   - 拖拽地图查看不同区域 (drag.sh 做大范围拖动)
   - 滚动列表查看更多条目 (scroll.sh)
   - 点击标签页/按钮切换子界面 (click.sh)
   - 每次操作后重新截屏感知
6. 重复直到当前界面充分探索
```

### 模式二: 执行模式

用户说"做日常"、"执行"、"帮我玩"时进入。目标: 根据知识库执行游戏操作。

```
循环:
1. kb.sh read daily-tasks.md (了解日常任务清单)
2. 截屏 → scan.sh (感知当前状态)
3. 根据知识库判断当前该做什么
4. 执行操作 (click/drag/scroll)
5. 等待 → 再次截屏确认操作结果
6. 如遇到未知 UI → 切换学习模式
```

### 模式三: 单步操作

用户给出具体指令时执行。如"点击武将按钮"、"查看资源"。

```
1. scan.sh (自动截图 + 感知)
2. 从感知结果找到目标 (OCR 文字匹配或模板匹配)
3. 执行操作
4. scan.sh 确认结果
```

## 已知 UI 元素 (模板库)

当前已注册的模板，可通过 `match.sh` 识别:

| 模板名 | 类别 | 描述 |
|--------|------|------|
| btn_return_city | nav_button | 回城按钮 |
| btn_world | nav_button | 世界地图按钮 |
| btn_wujiang | nav_button | 武将按钮 |
| btn_tongmeng | nav_button | 同盟按钮 |
| btn_zhiye | nav_button | 职业按钮 |
| btn_zhengzhan | nav_button | 征战按钮 |
| btn_xunfang | nav_button | 寻访按钮 |
| icon_lingdi | indicator | 领地指示器 (检测主界面) |
| banner_safety | notification | 安全提示弹窗 |

如需添加新模板，用 Python API:
```bash
curl -X POST http://127.0.0.1:5100/api/templates \
  -H "Content-Type: application/json" \
  -d '{"name":"新模板名","screenshot":"截图文件名","region":{"x":0,"y":0,"width":50,"height":50},"category":"button","threshold":0.8,"description":"描述"}'
```

## 感知结果解读

`scan.sh` 返回 JSON，关键字段:

```json
{
  "texts": [{"text": "领地60/60", "confidence": 0.99, "center": {"x": 220, "y": 10}}],
  "matches": [{"template": "btn_wujiang", "confidence": 0.97, "center": {"x": 537, "y": 469}}]
}
```

- **texts**: 画面中所有可见文字及其位置。用于读取资源数值、任务进度、按钮文字等
- **matches**: 画面中匹配到的已知 UI 模板及其位置。用于定位需要点击的按钮

## 操作注意事项

1. **每次操作后必须截屏确认** — 不要假设操作成功
2. **截图用完即删** — 截图仅用于当次感知，分析完成后立即删除，避免磁盘空间浪费
3. **操作间隔** — 点击后等待 1-2 秒再截屏，给游戏加载时间
4. **未知界面** — 如果感知结果与预期不符，先分析再操作，不要盲目点击
5. **坐标来源** — 只使用感知结果中的坐标，不要凭记忆猜测坐标
6. **安全提示** — 如检测到 `banner_safety` 模板，可能需要先关闭弹窗

## 知识库结构

知识库位于 `knowledge/` 目录，当前文件:

| 文件 | 内容 |
|------|------|
| game-overview.md | 游戏基本介绍 |
| ui-guide.md | UI 布局和界面导航 |
| daily-tasks.md | 日常任务清单和步骤 |
| buildings.md | 建筑系统 |
| resources.md | 资源系统 |
| combat.md | 战斗系统 |
| generals.md | 武将系统 |
| team-compositions.md | 队伍搭配 |
| territory.md | 领地系统 |
| learning-log.md | 学习记录 |

更新知识库时遵循:
- 保持 Markdown 格式
- 新发现追加到对应文件，不要覆盖已有内容
- 重要发现记录到 learning-log.md

## 环境部署

### 前置要求

- **Windows** 系统（游戏运行环境）
- **.NET 8 SDK**（编译 C# 工具）
- **Python 3.10+**（感知服务）
- **Git Bash**（运行 shell 脚本）

### 首次部署

```bash
# 1. 编译 C# 工具
dotnet build

# 2. 初始化 Python 虚拟环境 + 安装依赖
cd python
bash setup_env.sh

# 3. 回到项目根目录，启动 C# 控制台
cd ..
dotnet run --project src/SGMDTXTools.Console
```

### 注意事项

- venv 是**平台专属**的，macOS 上创建的 venv 在 Windows 上不能用，需要在目标平台重新运行 `setup_env.sh`
- Python 感知服务绑定 `0.0.0.0:5100`，支持从虚拟机内访问。C# 控制台可通过 `parser start` 自动启动服务
- 如果依赖有更新，重新运行 `cd python && bash setup_env.sh` 即可

## API 参考

详细的 API 文档见 [api-reference.md](api-reference.md)。

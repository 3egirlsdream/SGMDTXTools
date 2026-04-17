# SGMDTXTools 操作指南

## 启动

```bash
# 方式一：命令行参数指定进程名
dotnet run --project src/SGMDTXTools.Console -- 游戏进程名

# 方式二：交互输入进程名
dotnet run --project src/SGMDTXTools.Console
> 请输入游戏进程名(不含.exe): 游戏进程名
```

启动后进入交互式命令行，输入命令操作。`Ctrl+C` 优雅退出。

---

## 坐标系统

所有输入模拟命令支持两种坐标格式：

| 格式 | 示例 | 说明 |
|------|------|------|
| 像素坐标 | `100,200` | 客户区内的 (x,y) 像素位置 |
| 网格引用 | `F8` | 10列(A-J) x 18行(1-18) 网格的单元格中心 |

网格布局：`A1`(左上角) 到 `J18`(右下角)，每个引用指向对应单元格的中心像素。

---

## 命令一览

### 窗口与截图

| 命令 | 说明 |
|------|------|
| `find` | 查找游戏窗口，显示句柄、标题、进程、位置、尺寸、DPI缩放 |
| `capture` | 单次截图，保存到 `screenshots/` 目录 |
| `capture --grid` | 截图并叠加坐标网格 |
| `watch [秒数]` | 定时截图，默认5秒间隔，按回车停止 |
| `monitor` | 监控窗口位置变化，按回车停止 |
| `grid <引用>` | 网格引用转像素坐标（如 `grid F8`） |

### 输入模拟

| 命令 | 说明 |
|------|------|
| `click <坐标>` | 左键点击 |
| `click <坐标> right` | 右键点击 |
| `dblclick <坐标>` | 左键双击 |
| `drag <起点> <终点>` | 左键拖拽（线性插值20步） |
| `scroll <坐标> up\|down [次数]` | 滚轮滚动，默认3次 |
| `moveto <坐标>` | 移动鼠标光标到指定位置 |

### 知识库

| 命令 | 说明 |
|------|------|
| `knowledge list` / `kb list` | 列出所有知识文件 |
| `knowledge read <文件>` | 读取指定知识文件内容 |
| `knowledge search <关键词>` | 搜索知识库 |
| `knowledge stats` | 知识库统计（文件数、行数、大小） |
| `knowledge context` | 预览完整 LLM 上下文（前500字符） |

### 系统

| 命令 | 说明 |
|------|------|
| `status` | 显示当前状态（窗口、截图、知识库） |
| `help` | 显示帮助信息 |
| `quit` / `exit` | 退出程序 |

---

## 输入模拟详细说明

### click - 点击

```
click <坐标> [right]
```

- 左键点击：`click F8` 或 `click 100,200`
- 右键点击：`click F8 right` 或 `click 100,200 right`
- 内部流程：WM_MOUSEMOVE → 延迟 → WM_BUTTONDOWN → 延迟 → WM_BUTTONUP

### dblclick - 双击

```
dblclick <坐标>
```

- 示例：`dblclick E5` 或 `dblclick 300,400`
- 仅支持左键双击
- 内部流程：WM_MOUSEMOVE → LBUTTONDOWN → LBUTTONUP → 间隔 → LBUTTONDBLCLK → LBUTTONUP

### drag - 拖拽

```
drag <起点> <终点>
```

- 示例：`drag A1 J18` 或 `drag 100,200 500,600`
- 支持网格和像素坐标混用：`drag F8 300,400`
- 默认20步线性插值，拖拽过程中保持左键按下
- 内部流程：移动到起点 → LBUTTONDOWN → 逐步MOUSEMOVE(带MK_LBUTTON) → LBUTTONUP

### scroll - 滚轮

```
scroll <坐标> up|down [次数]
```

- 示例：`scroll F8 down 5` 或 `scroll 500,300 up 10`
- 次数默认为3
- 每次滚动增量为 WHEEL_DELTA (120)
- 内部使用屏幕坐标（ClientToScreen 自动转换）

### moveto - 移动鼠标

```
moveto <坐标>
```

- 示例：`moveto C3` 或 `moveto 200,300`
- 仅发送 WM_MOUSEMOVE，不按键

---

## 技术架构

```
┌─────────────────────────────────────────────────┐
│              Console / Program.cs                │
│  命令解析 → 服务调用 → 结果输出                    │
└────────────────────┬────────────────────────────┘
                     │
     ┌───────────────┼───────────────┐
     │               │               │
     ▼               ▼               ▼
 WindowLocator  ScreenCapturer  InputSimulator
 (窗口定位)     (截图服务)      (输入模拟)
     │               │               │
     ▼               ▼               ▼
  User32.dll      Gdi32.dll    PostMessage
  (EnumWindows)   (BitBlt)     (WM_MOUSE*)
```

### 核心服务

| 服务 | 类 | 说明 |
|------|-----|------|
| 窗口定位 | `ProcessWindowLocator` | 按进程名查找窗口，获取句柄和客户区信息 |
| 截图 | `DesktopDcCapturer` | Desktop DC + BitBlt 截图，支持定时截图 |
| 网格叠加 | `GridOverlay` | 网格绘制、坐标互转(GridRef <-> Pixel) |
| 输入模拟 | `PostMessageInputSimulator` | PostMessage 发送鼠标消息到窗口 |
| 知识库 | `KnowledgeManager` | Markdown 知识文件的读写和搜索 |
| DPI | `DpiHelper` | PerMonitorV2 DPI 感知 |

### 输入模拟配置

| 参数 | 默认值 | 说明 |
|------|--------|------|
| ClickDelayMs | 50ms | 按下和抬起之间的延迟 |
| DoubleClickIntervalMs | 80ms | 双击两次点击的间隔 |
| MoveStepDelayMs | 15ms | 拖拽/滚轮每步之间的延迟 |
| DragStepCount | 20 | 拖拽插值步数 |
| PreActionDelayMs | 30ms | 操作前鼠标移动后的等待时间 |
| ScrollDelta | 120 | 单次滚轮增量 |

---

## 目录结构

```
SGMDTXTools/
├── src/
│   ├── SGMDTXTools.Core/          # 核心库
│   │   ├── Native/                # Win32 P/Invoke
│   │   │   ├── User32.cs          # user32.dll (窗口/输入)
│   │   │   ├── Gdi32.cs           # gdi32.dll (图形)
│   │   │   ├── Shcore.cs          # shcore.dll (DPI)
│   │   │   └── NativeStructs.cs   # RECT, POINT 结构体
│   │   ├── Models/                # 数据模型
│   │   │   ├── WindowInfo.cs      # 窗口信息
│   │   │   ├── CaptureResult.cs   # 截图结果
│   │   │   ├── GridConfig.cs      # 网格配置
│   │   │   ├── InputCoordinate.cs # 坐标抽象(像素/网格)
│   │   │   ├── InputSimulatorConfig.cs # 输入模拟配置
│   │   │   ├── MouseButton.cs     # 鼠标按键枚举
│   │   │   └── ScrollDirection.cs # 滚动方向枚举
│   │   ├── Services/              # 业务服务
│   │   │   ├── IWindowLocator.cs
│   │   │   ├── ProcessWindowLocator.cs
│   │   │   ├── IScreenCapturer.cs
│   │   │   ├── DesktopDcCapturer.cs
│   │   │   ├── GridOverlay.cs
│   │   │   ├── IInputSimulator.cs
│   │   │   ├── PostMessageInputSimulator.cs
│   │   │   ├── KnowledgeManager.cs
│   │   │   └── DpiHelper.cs
│   │   └── Logging/
│   │       └── LoggerConfig.cs
│   └── SGMDTXTools.Console/       # 控制台应用
│       ├── Program.cs             # 命令行入口
│       └── app.manifest           # DPI 感知清单
├── tests/                         # 测试项目
├── knowledge/                     # 游戏知识库(Markdown)
├── screenshots/                   # 截图输出目录
├── logs/                          # 日志目录
└── docs/                          # 文档
    └── operation-guide.md         # 本操作指南
```

---

## 注意事项

1. **运行环境**：仅支持 Windows（依赖 Win32 API），需要 .NET 8.0 运行时
2. **PostMessage 特性**：消息发送到窗口消息队列，不需要窗口在前台，但目标窗口必须处理标准鼠标消息
3. **DPI 感知**：已设置 PerMonitorV2，坐标使用物理像素，无需手动缩放
4. **日志**：控制台输出 Info 级别以上，文件记录 Debug 级别以上（`logs/` 目录，每日滚动，保留30天）
5. **Ctrl+C**：支持优雅退出，长时间操作（拖拽、定时截图等）会响应取消请求

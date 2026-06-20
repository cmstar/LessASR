# Domain

## 业务场景

为 Windows 用户提供接近 Typeless 的本地语音输入体验（产品名 **LessASR**）：托盘常驻、快捷键触发、识别后优先直接写入当前焦点文本框。

## 核心角色

- **用户**：通过 F10 触发听写，在主窗口查看状态、历史与统计。

## 核心对象

| 对象 | 说明 |
| --- | --- |
| DictationSession | 一次从按键到注入/展示的听写过程 |
| AppSettings | 模型路径、whisper-server 路径与端口、保留策略等（存于固定数据目录下的 SQLite） |
| TextHistoryEntry | 可选保存的识别文本记录 |
| DailyStatsSnapshot | 按日聚合的使用统计（不含识别原文） |
| AsrResult | ASR 返回的文本与耗时指标 |

## 听写状态

`Idle` → `EnsuringModelReady` → `Ready` → `Recording` → `Transcribing` → `Injecting` → `Idle`

异常或需用户操作时进入 `ResultNeedsAction` 或 `Error`。

详细交互与浮窗行为见 `docs/superpowers/specs/2026-06-09-dictation-flow-design.md`。

## 关键规则

- F10 为默认切换键（键位定义于 `DictationHotkey`，与监听器实现解耦）：第一次按下开始录音，第二次按下结束并转写。热键在低级钩子中被吞掉，不传递给前台应用；开始录音时在按键瞬间捕获目标窗口/输入框。注入时优先对经典 Win32/Scintilla 控件直接写入；无法直接写入时恢复目标窗口并通过剪贴板粘贴回退，回退路径应尽量恢复用户原剪贴板。
- 录音中按 Esc 或点击浮窗关闭按钮可取消录音，不触发转写。
- 录音时长不足 0.3 秒时，第二次按 F10 视为取消，不触发转写。
- `Error` 或 `ResultNeedsAction` 后，用户再按 F10 直接开始新一轮录音，无需先关闭浮窗。
- 文本历史保留策略可配置（关闭 / 1 天 / 7 天 / 1 个月）；缩短策略时立即清理过期记录。
- 统计数据不可关闭，最多保留 2 个月，不保存识别原文。
- 窗口关闭行为可配置（默认最小化到托盘；也可设为直接退出）。托盘菜单「退出程序」始终可退出并释放资源。
- MVP 不做 LLM 润色，ASR 原文直接注入（经空后处理器）。
- 用户数据目录固定为 `%USERPROFILE%\.lessasr\`（`data/` 存 SQLite，`logs/` 存日志），不可配置。

## 术语

- **whisper-server**：本地 HTTP ASR 服务进程。
- **注入（Injection）**：通过 Win32 控件消息、`SendInput` 或剪贴板粘贴回退将文本写入目标控件。
- **浮窗（Overlay）**：桌面底部展示识别结果与复制按钮的 WPF 窗口。

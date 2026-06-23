# Domain

## 业务场景

为 Windows 用户提供接近 Typeless 的本地语音输入体验（产品名 **LessASR**）：托盘常驻、快捷键触发、识别后优先直接写入当前焦点文本框。

## 核心角色

- **用户**：通过右 Ctrl 触发听写，在主窗口查看状态、历史与统计。

## 核心对象

| 对象 | 说明 |
| --- | --- |
| DictationSession | 一次从按键到注入/展示的听写过程（单句听写） |
| ContinuousDictationSession | 连续听写会话：段列表、单路录音、FIFO 识别队列（最大 50 段） |
| ContinuousDictationSegment | 连续听写中的一段语音及其状态（WaitingInput / Transcribing / Completed / Failed） |
| AppSettings | 模型路径、whisper-server 路径与端口、保留策略等（存于固定数据目录下的 SQLite） |
| TextHistoryEntry | 可选保存的识别文本记录 |
| DailyStatsSnapshot | 按日聚合的使用统计（不含识别原文） |
| AsrResult | ASR 返回的文本与耗时指标 |

## 听写状态

`Idle` → `EnsuringModelReady` → `Ready` → `Recording` → `Transcribing` → `Injecting`（内部，浮窗仍显示「识别中」）→ `Idle` / `ResultNeedsAction` / `Error`

异常或需用户操作时进入 `ResultNeedsAction` 或 `Error`。

详细交互与浮窗行为见 `docs/superpowers/specs/2026-06-09-dictation-flow-design.md`。

## 连续听写状态

连续听写不使用单句状态机；每段独立经历 `WaitingInput` → `Transcribing` → `Completed` / `Failed`。会话另有「录制状态」（Recording Active）：F9 进入、F9 结束 / Esc / 「结束录制」/ 队列满 50 退出。

详细交互见 `docs/superpowers/specs/2026-06-23-continuous-dictation-design.md`。

## 关键规则

- 右 Ctrl 为默认切换键（键位定义于 `DictationHotkey`，与监听器实现解耦）：第一次按下开始录音，第二次按下结束并转写。热键在低级钩子中被吞掉，不传递给前台应用；开始录音时在按键瞬间捕获目标窗口/输入框。注入时优先对经典 Win32/Scintilla 控件直接写入；无法直接写入时恢复目标窗口并通过剪贴板粘贴回退，回退路径应尽量恢复用户原剪贴板。
- 录音中按 Esc 或点击浮窗关闭按钮可取消录音，不触发转写。
- 录音时长不足 0.3 秒时，第二次按右 Ctrl 视为取消，不触发转写。
- `Error` 或 `ResultNeedsAction` 后，用户再按右 Ctrl 直接开始新一轮录音，无需先关闭浮窗。
- 文本历史保留策略可配置（关闭 / 1 天 / 7 天 / 1 个月）；缩短策略时立即清理过期记录。
- 统计数据不可关闭，最多保留 2 个月，不保存识别原文。
- 窗口关闭行为可配置（默认最小化到托盘；也可设为直接退出）。托盘菜单「退出程序」始终可退出并释放资源。
- MVP 不做 LLM 润色，ASR 原文直接注入（经空后处理器）。
- 用户数据目录固定为 `%USERPROFILE%\.lessasr\`（`data/` 存 SQLite，`logs/` 存日志），不可配置。

### 连续听写

- **F9** 为连续听写专用热键：窗口不存在时创建连续听写窗口、聚焦、置顶并进入录制状态；窗口已开且未录制时追加录制（不清空已有段）；窗口已开且正在录制时结束录制状态（当前有效段入队，不新建 WaitingInput）。
- **右 Ctrl 双模式**：连续窗口**未开**时，行为与单句听写相同（toggle 录音 → 转写 → 注入）；连续窗口**已开**时，右 Ctrl 表示**句子边界**——结束当前段并入识别队列、新建 WaitingInput，录音不中断；单句 `DictationOrchestrator` 不响应。
- 识别队列 FIFO，最大深度 **50**；队列满时自动结束录制状态并提示用户。
- 录音时长不足 0.3 秒的段视为无效，对应列表项直接移除，不入队。
- 每段识别完成（成功或失败）各写入一次统计，与单句听写共用 `IStatsRepository`；终止或关窗均不回滚已写入统计。
- **关窗**（非「终止」）：收集所有 `Completed` 段当前文本（含用户编辑），按列表顺序以换行符（`\n`）合并为一条正文；非空时写入**一条** `TextHistoryEntry`；忽略 `Transcribing`、`WaitingInput`、`Failed` 项，不等待队列排空。
- **「终止」**：立即停止录音，清空列表与队列，取消进行中的 ASR；**不写**文本历史。
- **Esc / 「结束录制」**：仅结束录制状态；当前 `WaitingInput` 段不入队；窗口与已有项保留。
- 连续窗口开着时不使用听写浮窗；识别结果仅写入专用窗口内的段文本框，不注入外部焦点控件。

## 术语

- **whisper-server**：本地 HTTP ASR 服务进程。
- **注入（Injection）**：通过 Win32 控件消息、`SendInput` 或剪贴板粘贴回退将文本写入目标控件。
- **浮窗（Overlay）**：桌面底部展示识别结果与复制按钮的 WPF 窗口。
- **连续听写窗口**：F9 打开的专用 WPF 窗口，展示分段列表与终止 / 结束录制 / 复制操作。

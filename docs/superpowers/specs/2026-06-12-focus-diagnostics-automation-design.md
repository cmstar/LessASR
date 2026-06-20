# 焦点诊断自动化测试设计

日期：2026-06-12

## 目标

建立一套用于定位 LessASR 文本注入失败的自动化测试能力。第一阶段只覆盖本地测试目标窗口，不依赖记事本、VS Code、浏览器或其他第三方应用。

当前要诊断的问题是：用户按 F10 唤起语音输入后，LessASR 浮窗可能导致原输入窗口失焦；录音结束并完成转写后，识别文本无法写回原输入框。测试需要记录热键、焦点、浮窗、捕获目标、粘贴、UI Automation 和文本变化的完整时间线，以便后续基于证据修复。

## 范围

### 包含

- 本地 TestTarget 测试窗口，用于接收 LessASR 文本注入。
- TestTarget 的焦点、键盘、粘贴、文本变化和 UI 展示日志。
- LessASR 诊断模式下的结构化 JSONL 事件日志。
- 使用现有 F10 热键触发听写流程。
- 使用 `tests/Resources/test-sound.wav` 作为测试音频输入。
- 使用 fake ASR 固定返回测试文本，不验证 Whisper 识别率。
- 少量端到端测试：真实热键、真实浮窗、fake 音频、fake ASR、真实文本注入。

### 不包含

- 记事本、Notepad++、VS Code、浏览器等第三方软件兼容性测试。
- Whisper 真实识别准确率测试。
- 修复焦点或注入 bug 的具体实现。
- 长期性能采样、用户行为分析或生产遥测。

## 测试架构

```text
xUnit / FlaUI TestRunner
  ├─ 启动 TestTarget
  ├─ 启动 LessASR 诊断/测试模式
  ├─ 使用 SendInput 发送 F10
  ├─ 订阅 UI Automation 事件
  └─ 合并 TestTarget 与 LessASR 诊断日志进行断言

LessASR
  ├─ 真实 GlobalHotkeyListener
  ├─ 真实 DictationOverlayWindow
  ├─ fake AudioRecorder（读取 tests/Resources/test-sound.wav）
  ├─ fake AsrBackend（固定返回测试文本）
  ├─ 真实 SendInputTextInjector
  └─ JSONL DiagnosticEventSink

TestTarget
  ├─ Native/WinForms TextBox（第一阶段主测）
  ├─ WPF TextBox（保留用于后续覆盖）
  ├─ 只读 TextBox（保留用于失败路径）
  ├─ 事件记录器
  └─ 屏幕日志面板
```

## TestTarget 测试窗口

TestTarget 是一个专用桌面程序，启动后显示本地可控输入窗口。它不是 LessASR 的生产功能，只服务自动化测试和手工诊断。

### 输入区域

第一阶段至少包含三个输入控件：

| 控件 | 目的 |
| --- | --- |
| Native/WinForms TextBox | 主测对象，接近记事本的经典 `Edit` 行为 |
| WPF TextBox | 后续覆盖 WPF/现代控件路径 |
| 只读 TextBox | 后续验证不可写目标和失败路径 |

第一条端到端测试只要求 Native/WinForms TextBox 跑通。第三方应用兼容性测试在第一阶段不做。

### 屏幕日志区域

TestTarget 窗口必须有一个只读日志区域，用于实时打印诊断事件内容。这个区域用于手工运行时直接观察进度，不替代 JSONL 文件。

日志区域要求：

- 按事件顺序追加文本。
- 显示事件编号、时间、事件名、当前 foreground/focus 摘要。
- 显示 TestTarget 自身收到的焦点、键盘、粘贴、文本变化事件。
- 可显示从 LessASR 诊断日志文件尾随读取到的事件摘要。
- 不允许该日志区域自动抢焦点；启动和清空日志后，焦点仍应回到主测试输入框。
- 日志内容可以滚动查看。

示例屏幕日志：

```text
001 Target.Window.Activated foreground=TestTarget focus=NativeTextBox
002 Target.NativeTextBox.GotKeyboardFocus
003 LessASR.Hotkey.Matched vk=F10 suppressed=true
004 LessASR.Capture.After root=TestTarget focus=NativeTextBox
005 LessASR.Overlay.Show.After foreground=TestTarget overlayVisible=true
006 LessASR.Inject.Strategy method=ReplaceSelectionMessage
007 Target.NativeTextBox.TextChanged textLength=15
```

### TestTarget 事件记录

TestTarget 需要记录以下事件：

- Window `Activated` / `Deactivated`
- 控件 `GotKeyboardFocus` / `LostKeyboardFocus`
- `WM_SETFOCUS` / `WM_KILLFOCUS`
- `WM_ACTIVATE` / `WM_ACTIVATEAPP`
- `WM_KEYDOWN` / `WM_KEYUP`
- `WM_SYSKEYDOWN` / `WM_SYSKEYUP`
- `WM_CHAR`
- `WM_PASTE`
- WPF `DataObject.Pasting`
- 输入框文本变化

每条事件都应记录：

- 递增 `sequenceId`
- `timestamp`
- 当前线程 ID
- 当前 foreground HWND
- 当前 focus HWND
- 当前 active HWND
- 当前 caret HWND
- 相关 HWND 的 class name、process id、process name、window title

## UI Automation 事件

UI Automation 事件由 TestRunner 订阅，而不是只由 TestTarget 自己记录。这样可以验证外部自动化观察者看到的焦点与文本变化。

第一阶段建议订阅：

- `AutomationFocusChangedEvent`
- `ValuePattern.ValueProperty` 变化
- 支持时订阅 `TextPattern.TextChangedEvent`

UIA 事件写入测试运行日志，并在失败时与 LessASR/TestTarget 事件时间线一起输出。

## LessASR 诊断模式

LessASR 增加诊断事件 sink，仅在诊断模式开启时写结构化事件。诊断事件用于定位组件边界，不作为普通应用日志。

### 文件位置

诊断日志固定写入用户 LessASR 目录的新子目录：

```text
%USERPROFILE%\.lessasr\diagnostics\diagnostics-YYYY-MM-DD-HHmmss-pPID.jsonl
```

目录布局：

```text
.lessasr/
  data/
  logs/
  diagnostics/
```

`logs/` 继续用于现有异常日志，例如 `app-YYYY-MM-DD.log`。`diagnostics/` 专门存放焦点、热键、浮窗和注入诊断日志。

### 启用方式

诊断日志只在测试或手工诊断模式下启用。建议使用环境变量：

```text
LESSASR_TEST_MODE=1
LESSASR_DIAGNOSTICS=1
```

测试模式下：

- fake recorder 读取 `tests/Resources/test-sound.wav`。
- fake ASR 固定返回测试文本。
- 真实热键监听、真实浮窗、真实文本注入保持启用。
- 不验证 Whisper 识别结果。

### LessASR 事件

LessASR 至少记录：

- `Hotkey.Callback.Enter`
- `Hotkey.Matched`
- `Hotkey.Suppressed`
- `InjectionTargetCapture.Before`
- `InjectionTargetCapture.After`
- `Overlay.Show.Before`
- `Overlay.Show.After`
- `TextInjection.Before`
- `TextInjection.StrategySelected`
- `TextInjection.After`
- `Dictation.StateChanged`

每条事件至少包含：

- `timestamp`
- `sequenceId`
- `eventName`
- `state`
- `threadId`
- `foregroundWindow`
- `focusWindow`
- `activeWindow`
- `caretWindow`
- `capturedRootWindow`
- `capturedFocusWindow`
- `className`
- `processId`
- `processName`
- `windowTitle`
- 当前热键消息信息（热键事件适用）
- 当前注入策略与结果（注入事件适用）

## 端到端测试流程

第一阶段主测试：

1. 启动 TestTarget。
2. 聚焦 TestTarget 的 Native/WinForms TextBox。
3. 清空 TestTarget 文本与屏幕日志。
4. 启动 LessASR，启用测试模式和诊断模式。
5. TestRunner 订阅 UI Automation 事件。
6. 使用 `SendInput` 发送 F10。
7. 等待 LessASR 进入 `Recording`，浮窗出现。
8. 再次使用 `SendInput` 发送 F10。
9. fake recorder 返回 `tests/Resources/test-sound.wav`。
10. fake ASR 固定返回 `LessASR 自动化测试文本`。
11. LessASR 通过真实 `SendInputTextInjector` 注入文本。
12. TestRunner 读取 TestTarget 输入框文本。
13. TestRunner 读取 TestTarget 事件、UIA 事件和 LessASR JSONL 诊断日志。
14. 断言文本注入成功，并输出完整时间线。

## 断言

第一阶段主测试至少断言：

- F10 没有作为普通输入进入 TestTarget 输入框。
- `InjectionTargetCapture.After` 捕获到 TestTarget，而不是 LessASR 浮窗或主窗口。
- 浮窗显示后 foreground 不应变成 LessASR overlay。
- 注入前 captured root 仍指向 TestTarget。
- TestTarget 收到文本变化事件。
- TestTarget 输入框最终包含 fake ASR 固定文本。
- LessASR 最终回到 `Idle`，或失败时进入可解释的 `ResultNeedsAction` 并保留诊断证据。

如果失败，测试输出：

- TestTarget 屏幕日志内容。
- LessASR 诊断日志文件路径。
- 合并后的事件时间线。
- 最后 foreground/focus/active/caret 状态。

## 同步与等待

测试不得依赖固定 sleep 作为主要同步机制。应使用条件等待：

- 等待 TestTarget 输入框获得焦点。
- 等待 LessASR 进入 `Recording`。
- 等待浮窗 visible。
- 等待 TestTarget 输入框文本变为期望值。
- 等待 LessASR 回到 `Idle` 或进入 `ResultNeedsAction`。

固定延迟只能作为短暂稳定窗口或最终 timeout 的一部分。

## 成功标准

第一阶段完成后，应能在开发机上运行一条稳定的本地 E2E 测试：

```text
TestTarget Native TextBox 聚焦
  -> SendInput F10 开始录音
  -> LessASR 浮窗显示且不破坏目标捕获
  -> SendInput F10 停止录音
  -> fake ASR 返回固定文本
  -> LessASR 真实注入
  -> TestTarget Native TextBox 显示固定文本
  -> TestTarget 屏幕日志和 .lessasr/diagnostics JSONL 均包含完整证据链
```

## 后续扩展

第一阶段稳定后，再考虑：

- WPF TextBox 注入路径。
- 只读输入框失败路径。
- Notepad 兼容性冒烟测试。
- Notepad++ / VS Code / 浏览器 textarea 兼容性冒烟测试。
- 右 Alt 对标 Typeless 的实验性测试。

## 运行方式

实现计划见 `docs/superpowers/plans/2026-06-12-focus-diagnostics-automation.md`。日常开发与 CI 默认跳过 UI E2E；在 Windows 桌面会话中显式设置 `LESSASR_RUN_UI_E2E=1` 后运行 `FocusDiagnosticsE2ETests`。详细命令见 `docs/development.md` 中的「焦点诊断自动化测试」一节。

## 参考

- `tmp/todo.txt`
- `docs/architecture.md`
- `docs/development.md`
- `docs/superpowers/specs/2026-06-09-dictation-flow-design.md`
- `tests/Resources/test-sound.wav`

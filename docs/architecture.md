# Architecture

## 系统组成

```text
LocalAsrClient.App (WPF Shell)
  ├── 托盘、主窗口、听写浮窗、词汇表页面
  ├── 右 Ctrl 热键监听（Win32 低级钩子）
  ├── F9 热键监听 → ContinuousDictationCoordinator
  ├── ContinuousDictationWindow（连续听写专用窗口）
  ├── ContinuousDictationCoordinator（窗口生命周期、热键路由、关窗历史）
  ├── NAudio 音频采集（单句与连续各一路 IAudioRecorder）
  └── 文本注入（Win32 直写 / SendInput / 剪贴板粘贴回退）

LocalAsrClient.Core (Dictation Core)
  ├── DictationOrchestrator（单句听写状态机与编排）
  ├── ContinuousDictationSession（连续听写：段列表 + 单路录音 + FIFO 转写队列）
  ├── TranscriptionPipeline（单段 WAV → ASR → 后处理 → 统计，单句与连续共用）
  ├── ManagedWhisperServerBackend（ASR 后端）
  └── SQLite 持久化（设置、统计、文本历史）

whisper-server (外部进程)
  └── HTTP 语音转文字服务，由客户端托管生命周期
```

## 技术栈

| 层级 | 技术 |
| --- | --- |
| UI | WPF (.NET 8) + WPF UI（Fluent 控件与窗口能力） |
| 托盘 | System.Windows.Forms.NotifyIcon |
| 音频 | NAudio |
| 存储 | Microsoft.Data.Sqlite |
| ASR | whisper-server HTTP API |
| 测试 | xUnit |

## 模块边界

- `LocalAsrClient.Core` 定义平台抽象（`IHotkeyListener`、`IAudioRecorder`、`ITextInjector` 等），不引用 WPF 或 WinForms。
- `LocalAsrClient.App` 实现上述抽象并负责 DI 引导（`AppServices`）。
- `LocalAsrClient.App/Themes` 集中维护颜色、排版与通用控件样式；业务页面仅组合设计令牌与 WPF UI 控件。
- `LocalAsrClient.App/Dialogs` 提供配置驱动的通用确认窗口；业务调用方负责传入文案、可选摘要、按钮文字与普通 / 危险语义，不重复实现模态窗口。
- Core 测试可在无桌面会话环境运行。

## 数据流

### 单句听写

1. 用户按下右 Ctrl（连续窗口未开）→ 捕获输入焦点 → `IHotkeyListener` 通知 `DictationOrchestrator`。
2. 再次按下或超时 → 停止 `IAudioRecorder`，获得 WAV 数据。
3. Core 从最新设置构造语言参数和可选词汇表 prompt，`IAsrBackend` 确保 whisper-server 就绪后发送 HTTP 转写请求。
4. 识别文本经 `TranscriptionScriptPostProcessor`（简繁 OpenCC；简中 / 繁中时规范化 CJK 标点）后由 `ITextInjector` 注入。
5. 注入失败时进入 `ResultNeedsAction`，浮窗展示结果供复制。
6. 成功或失败后写入 `IStatsRepository`；若启用则写入 `ITextHistoryRepository`。

### 连续听写

1. 用户按下 F9 → `ContinuousDictationCoordinator` 创建或激活 `ContinuousDictationWindow`，启动 `ContinuousDictationSession` 录制状态。
2. 右 Ctrl（连续窗口已开）→ Coordinator 通知 Session 分段：当前段入 FIFO 转写队列，新建 WaitingInput，录音不中断。
3. Session 串行消费队列（最大 50 段）→ 每段经 `TranscriptionPipeline`（读取最新语言与词汇表设置 → ASR → 后处理）→ 段状态更新为 Completed / Failed，并写入 `IStatsRepository`。
4. 识别结果通过 ViewModel 绑定回填至 `ContinuousDictationWindow` 对应段 TextBox；Completed 段可编辑。
5. 关窗时 Coordinator 合并所有 Completed 段（`\n` 拼接，含用户编辑）写入一条 `ITextHistoryRepository`；「终止」清空会话且不写历史。
6. 连续窗口已开时，右 Ctrl 与 Esc 由 Coordinator 路由，单句 `DictationOrchestrator` 与听写浮窗不参与。

单句或连续听写完成“写入 + 保留期清理”后，`NotifyingTextHistoryRepository` 发布变更通知；用户确认删除单条历史后也由该包装器发布通知。主窗口收到通知后重新查询完整的最近历史并更新分组。进入历史页时还会主动刷新一次，避免展示启动时缓存。

## 外部集成

- **whisper-server**：客户端按设置路径启动子进程，通过 `HttpClient` 调用 `/inference` 等端点。
- **服务状态同步**：`WhisperServerProcessManager` 发布启动、就绪、停止与失败状态变化；WPF ViewModel 在 UI Dispatcher 上接收并刷新首页及模型页，包含热键触发的后台启动路径。
- **Win32/WPF Clipboard**：热键钩子、前台窗口恢复、`SendInput` 与剪贴板粘贴回退。

## 关键架构决策

- Core/App 分离以支持无头测试与后续替换 UI 层。
- 文本注入优先使用 Win32 控件直写；现代应用或未知控件无法直写时，使用“保存剪贴板 → 写入识别文本 → Ctrl+V → 恢复剪贴板”的兼容回退。
- 简繁后处理：`TranscriptionScriptPostProcessor` + OpenCC（`t2s` / `s2t`）；简中 / 繁中偏好时经 `ITranscriptionPunctuationPolicy` 判定后由 `CjkPunctuationNormalizer` 规范化标点；LLM 后处理接口仍保留。
- Whisper 词汇表作为全局设置保存；单句与连续听写在每次 ASR 请求前读取最新值并构造 `prompt`。词汇表支持混合 Unicode 语言，只提供识别软偏向，不覆盖首选语言。
- 用户数据目录固定为 `%USERPROFILE%\.lessasr\`（`LessAsrPaths`），设置项仅存于该目录下的 SQLite，避免「路径配置与数据库位置」循环依赖。
- `--test-mode` 使用内存 SQLite，桌面验收与 UI 自动化不得接触用户的生产数据库。

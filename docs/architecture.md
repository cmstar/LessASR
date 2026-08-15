# Architecture

## 系统组成

```text
LocalAsrClient.App (WPF Shell)
  ├── 托盘、主窗口、听写浮窗、模型页与词汇表页面
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
  ├── SwitchableAsrBackend（单一当前后端的运行时路由）
  ├── ManagedWhisperServerBackend / RemoteOpenAiBackend
  ├── AsrServiceCoordinator（选择、停止本地、忙碌约束与配置操作）
  └── SQLite 持久化（设置、远程 API 配置、词汇表、统计、文本历史）

whisper-server (外部进程)
  └── HTTP 语音转文字服务，由客户端托管生命周期

OpenAI-compatible API (远程进程或服务)
  └── 用户提供完整 Audio Transcriptions 端点；LessASR 不管理生命周期
```

## 技术栈

| 层级 | 技术 |
| --- | --- |
| UI | WPF (.NET 8) + WPF UI（Fluent 控件与窗口能力） |
| 托盘 | System.Windows.Forms.NotifyIcon |
| 音频 | NAudio |
| 存储 | Microsoft.Data.Sqlite |
| ASR | whisper-server HTTP API / OpenAI 兼容 Audio Transcriptions API |
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
3. Core 从最新设置取得语言，并通过 `IVocabularyRepository` 查询当前使用中的词汇表，构造可选 prompt；`SwitchableAsrBackend` 将请求路由到唯一的当前服务。本地后端确保 whisper-server 就绪；远程后端校验配置后直接调用用户填写的完整端点。
4. 识别文本经 `TranscriptionScriptPostProcessor`（简繁 OpenCC；简中 / 繁中时规范化 CJK 标点）后由 `ITextInjector` 注入。
5. 注入失败时进入 `ResultNeedsAction`，浮窗展示结果供复制。
6. 成功或失败后写入 `IStatsRepository`；若启用则写入 `ITextHistoryRepository`。

### 连续听写

1. 用户按下 F9 → `ContinuousDictationCoordinator` 创建或激活 `ContinuousDictationWindow`，启动 `ContinuousDictationSession` 录制状态。
2. 右 Ctrl（连续窗口已开）→ Coordinator 通知 Session 分段：当前段入 FIFO 转写队列，新建 WaitingInput，录音不中断。
3. Session 串行消费队列（最大 50 段）→ 每段经 `TranscriptionPipeline`（读取最新语言和当前使用中的词汇表 → ASR → 后处理）→ 段状态更新为 Completed / Failed，并写入 `IStatsRepository`。
4. 识别结果通过 ViewModel 绑定回填至 `ContinuousDictationWindow` 对应段 TextBox；Completed 段可编辑。
5. 关窗时 Coordinator 合并所有 Completed 段（`\n` 拼接，含用户编辑）写入一条 `ITextHistoryRepository`；「终止」清空会话且不写历史。
6. 连续窗口已开时，右 Ctrl 与 Esc 由 Coordinator 路由，单句 `DictationOrchestrator` 与听写浮窗不参与。

单句或连续听写完成“写入 + 保留期清理”后，`NotifyingTextHistoryRepository` 发布变更通知；用户确认删除单条历史后也由该包装器发布通知。主窗口收到通知后重新查询完整的最近历史并更新分组。进入历史页时还会主动刷新一次，避免展示启动时缓存。

设置页缩短文本历史保留期时，先通过 `ITextHistoryRepository.CountPrunableAsync` 计算超出新期限的记录数量；数量大于零时使用通用危险确认窗口提示用户。只有确认后才保存新策略并立即调用 `PruneAsync`，取消时设置与历史均保持不变；使用统计存储不参与该流程。

## 识别服务管理

- 模型页左侧列表固定以不可删除的本地 Whisper 开头，并可保存任意数量的远程 API 配置；右侧只展示当前选中项的详情。`AppSettings.ActiveRemoteApiProfileId == null` 表示本地服务。
- `AsrServiceCoordinator` 是切换与远程配置变更的业务边界。切换到远程前必须成功停止本地托管进程；切回本地只改变路由，不主动启动进程。
- `AsrActivityGate` 为听写和服务变更提供共享租约：单句从录音开始持有到结果持久化结束，连续听写持有到录音停止且识别队列清空；切换、编辑、删除、本地启停与重启必须先取得同一租约，因此不会发生检查通过后又在听写中途替换路由的竞态。当前启用的远程配置不可删除。
- 本地服务运行中保存路径、端口或线程参数时，`WhisperServerProcessManager` 先保留当前活动参数并标记 `IsRestartRequired`；模型页随后自动停止并重启服务，使待定参数立即生效。
- 远程 API 使用独立的 `HttpClient` 与后端，不复用本地 `ResilientWhisperServerClient` 的重启或重试逻辑。`RemoteHttpClientPool` 按规范化代理地址复用客户端与连接池；未配置代理时保留系统代理行为，配置代理后不自动绕过本机或局域网。测试配置也不会改变当前路由。
- 单句在发出请求前快照后端名和模型名；连续听写把来源写入每个完成段。连续历史来源一致时保存该来源，混用多个来源时保存“多个服务 / mixed”，避免关窗时误用当前路由。
- `SqliteSettingsStore.UpdateAsync` 在仓储锁和 SQLite 事务内完成设置的读—改—写；服务选择、模型页和设置页只更新各自字段，避免并发保存覆盖当前服务。

## 外部集成

- **whisper-server**：客户端按模型页配置启动子进程，通过 `HttpClient` 调用 `/inference` 等端点。
- **OpenAI 兼容 API**：客户端向完整端点单次发送 multipart 请求；空 Key 不发送 `Authorization`，非空 Key 使用 Bearer。每份配置可选择 HTTP、HTTPS、SOCKS4、SOCKS4a 或 SOCKS5 代理，测试与真实请求共用；代理认证暂不支持。远程服务生命周期始终由用户或提供方管理。
- **DPAPI**：`LocalAsrClient.App` 使用 Windows CurrentUser 范围且禁止 UI 提示的 DPAPI 实现 `ISecretProtector`；SQLite 只保存密文，返回给 ViewModel 的配置会移除密文，只携带“未配置 / 可用 / 需重新输入”状态；Core 不依赖 Windows 桌面 API。
- **服务状态同步**：`WhisperServerProcessManager` 发布启动、就绪、停止与失败状态变化；WPF ViewModel 在 UI Dispatcher 上接收并刷新首页及模型页，包含热键触发的后台启动路径。
- **Win32/WPF Clipboard**：热键钩子、前台窗口恢复、`SendInput` 与剪贴板粘贴回退。

## 关键架构决策

- Core/App 分离以支持无头测试与后续替换 UI 层。
- 当前 ASR 选择通过可替换路由统一注入单句与连续听写，避免两条链路分别判断本地/远程。
- 文本注入优先使用 Win32 控件直写；现代应用或未知控件无法直写时，使用“保存剪贴板 → 写入识别文本 → Ctrl+V → 恢复剪贴板”的兼容回退。
- 简繁后处理：`TranscriptionScriptPostProcessor` + OpenCC（`t2s` / `s2t`）；简中 / 繁中偏好时经 `ITranscriptionPunctuationPolicy` 判定后由 `CjkPunctuationNormalizer` 规范化标点；LLM 后处理接口仍保留。
- Whisper 词汇表是独立持久化实体，可创建多份但同一时间最多一份处于使用中；单句与连续听写在每次 ASR 请求前查询当前词汇表并构造 `prompt`。词汇表支持混合 Unicode 语言，只提供识别软偏向，不覆盖首选语言。
- 用户数据目录固定为 `%USERPROFILE%\.lessasr\`（`LessAsrPaths`），设置项仅存于该目录下的 SQLite，避免「路径配置与数据库位置」循环依赖。
- `--test-mode` 使用内存 SQLite，桌面验收与 UI 自动化不得接触用户的生产数据库。
- `--demo-mode` 只使用系统临时目录下固定的 `LessASR/demo/` 布局；每次启动先重建该演示数据库，再通过当前 SQLite 初始化与仓储 API 生成样例数据。演示模式不得接受指向生产目录的路径参数。

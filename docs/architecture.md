# Architecture

## 系统组成

```text
LocalAsrClient.App (WPF Shell)
  ├── 托盘、主窗口、听写浮窗
  ├── F10 热键监听（Win32 低级钩子）
  ├── NAudio 音频采集
  └── 文本注入（Win32 直写 / SendInput / 剪贴板粘贴回退）

LocalAsrClient.Core (Dictation Core)
  ├── DictationOrchestrator（听写状态机与编排）
  ├── ManagedWhisperServerBackend（ASR 后端）
  └── SQLite 持久化（设置、统计、文本历史）

whisper-server (外部进程)
  └── HTTP 语音转文字服务，由客户端托管生命周期
```

## 技术栈

| 层级 | 技术 |
| --- | --- |
| UI | WPF (.NET 8) |
| 托盘 | System.Windows.Forms.NotifyIcon |
| 音频 | NAudio |
| 存储 | Microsoft.Data.Sqlite |
| ASR | whisper-server HTTP API |
| 测试 | xUnit |

## 模块边界

- `LocalAsrClient.Core` 定义平台抽象（`IHotkeyListener`、`IAudioRecorder`、`ITextInjector` 等），不引用 WPF 或 WinForms。
- `LocalAsrClient.App` 实现上述抽象并负责 DI 引导（`AppServices`）。
- Core 测试可在无桌面会话环境运行。

## 数据流

1. 用户按下 F10 → 捕获输入焦点 → `IHotkeyListener` 通知 `DictationOrchestrator`。
2. 再次按下或超时 → 停止 `IAudioRecorder`，获得 WAV 数据。
3. `IAsrBackend` 确保 whisper-server 就绪后发送 HTTP 转写请求。
4. 识别文本经 `TranscriptionScriptPostProcessor`（简中 / 繁中偏好时 OpenCC 转换）后由 `ITextInjector` 注入。
5. 注入失败时进入 `ResultNeedsAction`，浮窗展示结果供复制。
6. 成功或失败后写入 `IStatsRepository`；若启用则写入 `ITextHistoryRepository`。

## 外部集成

- **whisper-server**：客户端按设置路径启动子进程，通过 `HttpClient` 调用 `/inference` 等端点。
- **Win32/WPF Clipboard**：热键钩子、前台窗口恢复、`SendInput` 与剪贴板粘贴回退。

## 关键架构决策

- Core/App 分离以支持无头测试与后续替换 UI 层。
- 文本注入优先使用 Win32 控件直写；现代应用或未知控件无法直写时，使用“保存剪贴板 → 写入识别文本 → Ctrl+V → 恢复剪贴板”的兼容回退。
- 简繁后处理：`TranscriptionScriptPostProcessor` + OpenCC（`t2s` / `s2t`）；LLM 后处理接口仍保留。
- 用户数据目录固定为 `%USERPROFILE%\.lessasr\`（`LessAsrPaths`），设置项仅存于该目录下的 SQLite，避免「路径配置与数据库位置」循环依赖。

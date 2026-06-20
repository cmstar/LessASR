# Development

## 技术栈

- .NET 8
- WPF（`net8.0-windows`）
- xUnit + Microsoft.NET.Test.Sdk
- Microsoft.Data.Sqlite 2.x
- NAudio 2.2.1

## 目录约定

```text
src/LocalAsrClient.Core/
  Abstractions/   # 平台与持久化接口
  Asr/            # whisper-server 客户端与后端
  Dictation/      # 状态机与编排
  Persistence/    # SQLite 与设置模型
  Text/           # 注入相关模型
  Utilities/      # 时钟、文本度量等

src/LocalAsrClient.App/
  Bootstrap/      # 服务组合根
  Audio/          # NAudio 录音实现
  Hotkeys/        # 全局热键钩子（键位见 DictationHotkey）
  TextInjection/  # SendInput 实现
  Tray/           # 托盘图标
  Overlay/        # 听写浮窗
  ViewModels/     # 主窗口各 Tab

tests/LocalAsrClient.Core.Tests/
```

- Core 项目不得引用 WPF、WinForms 或 NAudio。
- Win32 interop 仅存在于 `LocalAsrClient.App`。

## 编码约定

- 启用 nullable 与 implicit usings。
- 接口放在 `Abstractions/`，实现与领域类型按功能分子目录。
- 中文 UI 字符串放在 App 层 ViewModel 或 XAML，Core 层消息可用中文（与规格一致）。
- 注释使用完整句子并以句号结尾。

## 测试约定

- Core 业务逻辑采用 TDD：先写失败测试，再实现，再全量测试。
- 持久化测试使用 `SqliteDatabase.CreateInMemoryAsync()`。
- ASR 后端测试 mock `HttpClient` 或进程管理器，避免依赖真实 whisper-server。
- 提交前运行：`dotnet test LocalAsrClient.sln`

## 验证命令

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

针对单个测试类：

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter <TestClassName>
```

## 焦点诊断自动化测试

焦点诊断 E2E 测试只依赖本地 TestTarget，不依赖记事本、VS Code、浏览器或真实 whisper-server。

运行前先构建：

```powershell
dotnet build src/LocalAsrClient.App/LocalAsrClient.App.csproj
dotnet build tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj
dotnet build tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj
```

显式开启 UI E2E：

```powershell
$env:LESSASR_RUN_UI_E2E='1'
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter FocusDiagnosticsE2ETests
```

LessASR 诊断日志写入：

```text
%USERPROFILE%\.lessasr\diagnostics\diagnostics-YYYY-MM-DD-HHmmss-pPID.jsonl
```

测试音频固定来自 `tests/Resources/test-sound.wav`，测试模式下 ASR 固定返回测试文本，不验证 whisper-server 识别准确率。

## 实现计划

分步任务见 `docs/superpowers/plans/2026-06-07-windows-asr-client-mvp.md`，按 Task 1–13 顺序实施。

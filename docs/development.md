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
  Asr/            # 本地/远程 ASR 客户端、后端、运行时路由与服务协调
  Dictation/      # 状态机与编排
  Persistence/    # SQLite 与设置模型
  Text/           # 注入相关模型
  Utilities/      # 时钟、文本度量等

src/LocalAsrClient.App/
  Assets/Brand/    # A2 品牌图标 SVG 源文件与应用 / 托盘 ICO
  Bootstrap/      # 服务组合根
  Audio/          # NAudio 录音实现
  Hotkeys/        # 全局热键钩子（键位见 DictationHotkey）
  TextInjection/  # SendInput 实现
  Tray/           # 托盘图标
  Overlay/        # 听写浮窗
  Dialogs/        # 通用确认窗口及其配置模型
  Security/       # Windows DPAPI 等桌面安全实现
  ViewModels/     # 主窗口各 Tab
  Views/          # 模型页及远程配置详情等独立 UserControl

tests/LocalAsrClient.Core.Tests/
```

品牌图标由仓库内脚本从同一套 A2 几何参数生成，修改图标后执行：

```powershell
.\tools\Generate-LessAsrIcons.ps1
```

应用 ICO 包含 16–256 px 帧；托盘 ICO 包含 16–32 px 深浅两套线稿，由客户端按 Windows 任务栏主题选择。

- Core 项目不得引用 WPF、WinForms 或 NAudio。
- Win32 interop 仅存在于 `LocalAsrClient.App`。
- 全局热键在目标键完整按下并释放后触发；按键期间若有其他键参与则整次手势作废，修饰键热键必须继续传递给前台应用以保留组合键功能。

## 编码约定

- 启用 nullable 与 implicit usings。
- 接口放在 `Abstractions/`，实现与领域类型按功能分子目录。
- 中文 UI 字符串放在 App 层 ViewModel 或 XAML，Core 层消息可用中文（与规格一致）。
- 注释使用完整句子并以句号结尾。

## 测试约定

- Core 业务逻辑采用 TDD：先写失败测试，再实现，再全量测试。
- 持久化测试使用 `SqliteDatabase.CreateInMemoryAsync()`。
- ASR 后端测试 mock `HttpClient` 或进程管理器，避免依赖真实 whisper-server 或远程平台；远程请求测试必须验证无自动重试、可选 Bearer 和 multipart 字段。
- API Key 测试只使用测试字符串；不得把明文 Key 写入 SQLite、日志、截图或测试快照。WPF 只通过 `PasswordBox` 在保存操作中传递本次输入，不建立明文双向绑定。
- 服务变更测试必须覆盖共享 `AsrActivityGate`、whisper-server 启停竞态、设置并发更新、DPAPI 不可用状态，以及单句/连续历史的后端来源快照。
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

运行 UI E2E（默认 `dotnet test` 会跳过 `UiE2E` 分类）：

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter "Category=UiE2E"
```

LessASR 诊断日志写入：

```text
%USERPROFILE%\.lessasr\diagnostics\diagnostics-YYYY-MM-DD-HHmmss-pPID.jsonl
```

独立诊断模式（生产环境可不依赖 whisper-server 与 `--test-mode`，仅写入上述 JSONL）：

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj -- --diagnostics
```

测试模式启动（不依赖 whisper-server，自动写入上述 JSONL 诊断日志）：

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj -- --test-mode
```

测试模式下 ASR 固定返回默认测试文本，不验证 whisper-server 识别准确率。仍使用双按右 Ctrl 完整听写链路，仅替换录音与 ASR 后端。
测试模式使用内存 SQLite，不读取或修改用户正式的 `%USERPROFILE%\.lessasr\data\client.db`。

## 演示数据与文档截图

演示模式用于 README 与后续使用文档的真实界面截图。它使用系统临时目录下固定的
`LessASR\demo\` 数据根，每次启动都会从零创建数据库，不读取或修改
`%USERPROFILE%\.lessasr\`：

```powershell
.\tools\Start-LessAsrDemo.ps1
```

演示数据通过当前 `SqliteDatabase` 和仓储 API 生成，不提交预制 `.db` 文件。模型页演示配置不含 API Key，演示模式也不会发起远程网络请求。连续听写截图
复用 `ContinuousDictationSession`、内存录音替身与顺序演示 ASR，不依赖麦克风、模型文件或
`whisper-server`。

重新生成 README 与未来 Wiki 共用的产品截图：

```powershell
.\tools\Update-DocumentationScreenshots.ps1
```

截图命令需要 Windows 桌面会话；工具会临时置顶演示窗口，通过 WPF 视觉树导出约定尺寸，并分别选择本地与远程模型生成详情截图。

截图源文件统一放在 `docs/assets/screenshots/`，按内容场景分目录，不按 README、Wiki 等
发布渠道重复存放。未来若使用独立的 GitHub Wiki 仓库，应由发布流程复制 `docs/` 文档及其
`assets/`，仓库内文件仍是唯一来源。

以下改动必须同步检查演示场景，并运行截图更新命令：

- SQLite 表结构、迁移、仓储接口或保留策略变化；
- 首页指标、统计范围或历史分组变化；
- 设置项、导航、窗口尺寸、主题或通用控件样式变化；
- 模型页、模型列表、本地详情、远程 API 详情或地址安全提示变化；
- 连续听写状态、文案、布局或段落数量变化。

`StatsViewModel.SummaryDayCount` 与 `TrendDayCount` 是统计展示和演示数据的共同范围来源。
修改统计窗口时应先更新这些定义，再调整相应测试和文档说明。

### 进程生命周期说明

`LocalAsrClient.TestTarget` 本身是普通 WPF 窗口程序，**不会**在跑完后自动退出。通过 `dotnet test` 跑 UI E2E 时，测试框架里的 `ProcessRunner` 在断言结束后会依次 `CloseMainWindow()`，超时则 `Kill()`，因此你会看到两个窗口一闪就关——这是测试 runner 主动清理，不是 TestTarget 自己退出。

### 人工查看 E2E 结果

跑完 E2E 后保持 TestTarget 与 LessASR 窗口不关闭：

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter "Category=UiE2E" -- PauseAfterRun
```

`PauseAfterRun` 会让 TestTarget 以 `--pause` 启动；测试结束后 runner 不再 kill 子进程，可人工检查输入框文本与屏幕日志。确认完毕后手动关闭两个窗口即可。

### 完全手工联调

不跑 `dotnet test`，分别启动两个进程（窗口会一直保留，直到手动关闭）：

```powershell
dotnet run --project tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj -- --pause
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj -- --test-mode
```

在 TestTarget 中聚焦 Native 输入框，按两次右 Ctrl 完成一次听写，然后查看屏幕日志与 `%USERPROFILE%\.lessasr\diagnostics\` 下的 JSONL。

## 实现计划

分步任务见 `docs/superpowers/plans/2026-06-07-windows-asr-client-mvp.md`，按 Task 1–13 顺序实施。

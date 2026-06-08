# 语音输入（听写）流程 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成 LessASR 端到端语音输入：右 Ctrl 切换录音 → whisper-server 转写 → 焦点检测与 SendInput 注入，失败时浮窗展示错误并支持下一轮录音。

**Architecture:** 在现有 `DictationOrchestrator` + WPF 浮窗骨架上补齐状态恢复、防重入、注入回退与可观测性。Core 保持平台无关；焦点检测与 SendInput 留在 `LocalAsrClient.App`。ASR HTTP 与进程管理沿用 `ManagedWhisperServerBackend` / `WhisperServerProcessManager`。

**Tech Stack:** C# / .NET 8, WPF, xUnit, NAudio, Win32 (`user32.dll`), HttpClient, Microsoft.Data.Sqlite

---

## Reference Spec

实现依据：

`docs/superpowers/specs/2026-06-09-dictation-flow-design.md`

## File Map

| 文件 | 职责 |
| --- | --- |
| `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs` | 状态机、防重入、注入结果映射、错误统计 |
| `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs` | 编排器行为单测 |
| `src/LocalAsrClient.Core/Asr/WhisperServerClient.cs` | 转写请求、`language` 参数、HTTP 错误信息 |
| `tests/LocalAsrClient.Core.Tests/Asr/WhisperServerBackendTests.cs` | 客户端请求字段单测 |
| `src/LocalAsrClient.App/Bootstrap/AppServices.cs` | HttpClient 同步、热键异常日志 |
| `src/LocalAsrClient.App/Overlay/OverlayViewModel.cs` | 错误详情、复制按钮可见性 |
| `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml` | 错误详情 UI |
| `src/LocalAsrClient.App/ViewModels/MainViewModel.cs` | 向浮窗传递 `ErrorMessage` |
| `src/LocalAsrClient.App/TextInjection/Win32FocusNative.cs` | Win32 焦点探测 P/Invoke |
| `src/LocalAsrClient.App/TextInjection/EditableFocusDetector.cs` | 判断前台焦点是否可编辑 |
| `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs` | 注入前焦点检测 |
| `docs/api.md` | 补充 `language` 字段说明 |

---

### Task 1: 编排器状态恢复与防重入

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs`

- [ ] **Step 1: 写失败测试——Error 后重试开始录音**

在 `DictationOrchestratorTests.cs` 末尾添加：

```csharp
[Fact]
public async Task ToggleAsync_FromError_StartsNewRecording()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    fixture.Backend.TranscribeThrows = new InvalidOperationException("转写失败");
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.Error, fixture.LastStatus.State);

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
    Assert.Equal("正在聆听", fixture.LastStatus.Message);
    Assert.True(fixture.Recorder.Started);
}

[Fact]
public async Task ToggleAsync_FromResultNeedsAction_StartsNewRecording()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "没有输入框");

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
}

[Fact]
public async Task ToggleAsync_IgnoresPressWhileTranscribing()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Backend.TranscribeDelay = TimeSpan.FromMilliseconds(200);

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    var stopTask = fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    await stopTask;

    Assert.Equal(1, fixture.Backend.TranscribeCallCount);
}
```

在 `StubBackend` 中补充：

```csharp
public int TranscribeCallCount { get; private set; }
public Exception? TranscribeThrows { get; set; }
public TimeSpan TranscribeDelay { get; set; }

public async Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
{
    TranscribeCallCount++;
    if (TranscribeThrows is not null)
    {
        throw TranscribeThrows;
    }

    if (TranscribeDelay > TimeSpan.Zero)
    {
        await Task.Delay(TranscribeDelay, cancellationToken);
    }

    return new AsrResult("测试文本", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null);
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~DictationOrchestratorTests" -v minimal
```

Expected: 新测试 FAIL（`Recording` 断言失败或 `TranscribeCallCount` 为 2）。

- [ ] **Step 3: 实现编排器改动**

将 `DictationOrchestrator.cs` 替换为下列核心变更（保留现有字段与构造函数）：

```csharp
private readonly SemaphoreSlim _toggleLock = new(1, 1);

public async Task ToggleAsync(CancellationToken cancellationToken)
{
    if (!await _toggleLock.WaitAsync(0, cancellationToken))
    {
        return;
    }

    try
    {
        if (_state is DictationState.Transcribing or DictationState.Injecting or DictationState.EnsuringModelReady)
        {
            return;
        }

        if (_state is DictationState.Idle or DictationState.Ready or DictationState.Error or DictationState.ResultNeedsAction)
        {
            await StartRecordingAsync(cancellationToken);
            return;
        }

        if (_state == DictationState.Recording)
        {
            await StopAndTranscribeAsync(cancellationToken);
        }
    }
    finally
    {
        _toggleLock.Release();
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~DictationOrchestratorTests" -v minimal
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs
git commit -m "feat: allow dictation retry from error and result states"
```

---

### Task 2: 注入结果映射与空文本处理

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task ToggleAsync_WhenInjectionReturnsFailed_UsesInjectorMessage()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.Failed, "SendInput 被拒绝");

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
    Assert.Equal("SendInput 被拒绝", fixture.LastStatus.Message);
}

[Fact]
public async Task ToggleAsync_WhenTranscriptionEmpty_ShowsEmptyTextMessage()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Backend.TranscribeText = "";

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
    Assert.Equal("识别文本为空", fixture.LastStatus.Message);
    Assert.Equal(1, fixture.Stats.Recorded.Count);
    Assert.False(fixture.Stats.Recorded[0].Succeeded);
    Assert.Empty(fixture.History.Entries);
}
```

在 `StubBackend` 添加 `public string TranscribeText { get; set; } = "测试文本";`，`TranscribeAsync` 返回 `TranscribeText`。

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~ToggleAsync_WhenInjectionReturnsFailed|FullyQualifiedName~ToggleAsync_WhenTranscriptionEmpty" -v minimal
```

Expected: FAIL

- [ ] **Step 3: 实现 `StopAndTranscribeAsync` 注入与空文本分支**

在 `StopAndTranscribeAsync` 中，转写后、注入前插入：

```csharp
var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);

if (string.IsNullOrWhiteSpace(finalText))
{
    await PersistResultAsync(string.Empty, recording.Duration, asrResult.ProcessingDuration ?? TimeSpan.Zero, succeeded: false, cancellationToken);
    _state = DictationState.ResultNeedsAction;
    Publish("识别文本为空");
    return;
}
```

注入失败后：

```csharp
_state = DictationState.ResultNeedsAction;
var message = injection.Status == TextInjectionStatus.NoEditableTarget
    ? "未找到可输入位置"
    : injection.Message ?? "文本注入失败";
Publish(message, finalText);
```

在 `PersistResultAsync` 开头添加：若 `string.IsNullOrWhiteSpace(text)` 则跳过 `historyRepository.AddAsync`（统计仍记录）。

- [ ] **Step 4: 运行全部编排器测试**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~DictationOrchestratorTests" -v minimal
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs
git commit -m "feat: map injection failures and empty transcription to result state"
```

---

### Task 3: Error 路径记录失败统计

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task ToggleAsync_WhenTranscribeThrows_RecordsFailedStatsWithoutHistory()
{
    var fixture = new Fixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;

    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
    fixture.Backend.TranscribeThrows = new HttpRequestException("连接被拒绝");
    await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

    Assert.Equal(DictationState.Error, fixture.LastStatus.State);
    Assert.Single(fixture.Stats.Recorded);
    Assert.False(fixture.Stats.Recorded[0].Succeeded);
    Assert.Empty(fixture.History.Entries);
}
```

- [ ] **Step 2: 运行测试确认失败**

Expected: FAIL（统计未记录）

- [ ] **Step 3: 在 catch 块记录失败统计**

```csharp
catch (Exception ex)
{
    try
    {
        await PersistResultAsync(string.Empty, TimeSpan.Zero, TimeSpan.Zero, succeeded: false, cancellationToken);
    }
    catch
    {
        // 统计写入失败不再掩盖原始异常。
    }

    _state = DictationState.Error;
    Publish("输入失败", ErrorMessage: ex.Message);
}
```

- [ ] **Step 4: 运行测试确认通过**

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs
git commit -m "feat: record failed stats when dictation errors"
```

---

### Task 4: 浮窗展示错误详情

**Files:**
- Modify: `src/LocalAsrClient.App/Overlay/OverlayViewModel.cs`
- Modify: `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml`
- Modify: `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: 扩展 `OverlayViewModel`**

```csharp
private string _errorMessage = "";
private bool _showResultText;
private bool _showCopyButton;

public string ErrorMessage
{
    get => _errorMessage;
    set { _errorMessage = value; OnPropertyChanged(); }
}

public bool ShowResultText
{
    get => _showResultText;
    set { _showResultText = value; OnPropertyChanged(); }
}

public void ShowState(OverlayState state, string message, string resultText = "", string? errorMessage = null)
{
    State = state;
    Message = message;
    ResultText = resultText;
    ErrorMessage = errorMessage ?? string.Empty;
    ShowResultText = state is OverlayState.ResultNeedsAction or OverlayState.Error
        && !string.IsNullOrWhiteSpace(resultText);
    ShowCopyButton = ShowResultText;
}
```

- [ ] **Step 2: 更新 XAML**

在 `Message` `TextBlock` 下方添加错误详情：

```xml
<TextBlock Text="{Binding ErrorMessage}"
           TextWrapping="Wrap"
           FontSize="13"
           Foreground="#B91C1C"
           Margin="0,0,0,8"
           Visibility="{Binding ErrorMessage, Converter={StaticResource StringNotEmptyToVisibilityConverter}}"/>
```

将 `TextBox` 的 `Visibility` 绑定改为 `{Binding ShowResultText, Converter={StaticResource BooleanToVisibilityConverter}}`。

在 `App.xaml` 或窗口资源中注册 `StringNotEmptyToVisibilityConverter`（若无则新建 `src/LocalAsrClient.App/Infrastructure/StringNotEmptyToVisibilityConverter.cs`）。

- [ ] **Step 3: 更新 `DictationOverlayWindow.ShowOverlay` 签名**

```csharp
public void ShowOverlay(OverlayState state, string message, string resultText = "", string? errorMessage = null)
{
    _viewModel.ShowState(state, message, resultText, errorMessage);
    PositionBottomCenter();
    Show();
}
```

- [ ] **Step 4: `MainViewModel` 传递 `ErrorMessage`**

```csharp
_services.OverlayWindow.ShowOverlay(
    overlayState,
    status.Message,
    status.ResultText ?? "",
    status.ErrorMessage);
```

同步更新 `DebugViewModel` 中所有 `ShowOverlay` 调用（新参数可省略）。

- [ ] **Step 5: 构建验证**

```powershell
dotnet build LocalAsrClient.sln
```

Expected: Build succeeded

- [ ] **Step 6: Commit**

```powershell
git add src/LocalAsrClient.App/Overlay src/LocalAsrClient.App/ViewModels/MainViewModel.cs src/LocalAsrClient.App/Infrastructure
git commit -m "feat: show dictation error details in overlay"
```

---

### Task 5: Whisper 转写请求补充 language 与 HTTP 错误信息

**Files:**
- Modify: `src/LocalAsrClient.Core/Asr/WhisperServerClient.cs`
- Modify: `docs/api.md`
- Test: `tests/LocalAsrClient.Core.Tests/Asr/WhisperServerBackendTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task Client_SendsLanguageField_WhenProvidedInRequest()
{
    var handler = new StubHttpHandler("""{"text":"你好"}""");
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
    var client = new WhisperServerClient(httpClient);

    await client.TranscribeAsync(new InMemoryAudioInput(
        Encoding.UTF8.GetBytes("fake"), "wav", 16000, 1), "zh", CancellationToken.None);

    Assert.Contains("name=\"language\"", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("zh", handler.LastRequestBody, StringComparison.Ordinal);
}
```

扩展 `StubHttpHandler` 记录 `LastRequestBody`（`ReadAsStringAsync` 内容）。

将 `WhisperServerClient.TranscribeAsync` 签名改为：

```csharp
public async Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, string? language, CancellationToken cancellationToken)
```

`ManagedWhisperServerBackend.TranscribeAsync` 调用时传入 `request.Language`。

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~Client_SendsLanguageField" -v minimal
```

Expected: FAIL

- [ ] **Step 3: 实现客户端**

```csharp
content.Add(new StringContent("json"), "response_format");
if (!string.IsNullOrWhiteSpace(language))
{
    content.Add(new StringContent(language), "language");
}

using var response = await _httpClient.PostAsync("/v1/audio/transcriptions", content, cancellationToken);
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    throw new HttpRequestException(
        $"whisper-server 转写失败：{(int)response.StatusCode} {response.ReasonPhrase}。{Truncate(body, 200)}");
}
```

添加私有 `Truncate` 帮助方法。

更新 `docs/api.md` 的字段列表，加入 `language`（可选，推荐 `zh`）。

- [ ] **Step 4: 运行 ASR 测试**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~WhisperServerBackendTests" -v minimal
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Asr docs/api.md tests/LocalAsrClient.Core.Tests/Asr
git commit -m "feat: send language to whisper-server and surface HTTP errors"
```

---

### Task 6: HttpClient 与设置同步

**Files:**
- Modify: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`

- [ ] **Step 1: 将 HttpClient 提升为实例字段**

```csharp
private AppServices(
    ...
    HttpClient httpClient,
    WhisperServerProcessManager serverManager)
{
    HttpClient = httpClient;
    ...
}

public HttpClient HttpClient { get; }
```

`CreateAsync` 中创建 `httpClient` 后传入构造函数。

- [ ] **Step 2: 在 `ApplyServerOptionsFromSettingsAsync` 同步 BaseAddress**

```csharp
public async Task ApplyServerOptionsFromSettingsAsync(CancellationToken cancellationToken = default)
{
    var settings = await SettingsStore.LoadAsync(cancellationToken);
    var options = new WhisperServerOptions(
        settings.WhisperServerPath,
        settings.ModelPath,
        "127.0.0.1",
        8080);
    ServerManager.UpdateOptions(options);
    HttpClient.BaseAddress = options.BaseUri;
}
```

- [ ] **Step 3: 构建验证**

```powershell
dotnet build LocalAsrClient.sln
```

Expected: Build succeeded

- [ ] **Step 4: Commit**

```powershell
git add src/LocalAsrClient.App/Bootstrap/AppServices.cs
git commit -m "fix: sync HttpClient base address when settings change"
```

---

### Task 7: 热键回调记录异常

**Files:**
- Modify: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`

- [ ] **Step 1: 替换空 catch**

```csharp
using LocalAsrClient.App.Infrastructure;

hotkeyListener.Triggered += () =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await orchestrator.ToggleAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "听写热键处理失败", showDialog: false);
        }
    });
};
```

- [ ] **Step 2: 构建验证**

```powershell
dotnet build LocalAsrClient.sln
```

Expected: Build succeeded

- [ ] **Step 3: Commit**

```powershell
git add src/LocalAsrClient.App/Bootstrap/AppServices.cs
git commit -m "fix: log exceptions from hotkey dictation handler"
```

---

### Task 8: 焦点检测与 SendInput 注入

**Files:**
- Create: `src/LocalAsrClient.App/TextInjection/Win32FocusNative.cs`
- Create: `src/LocalAsrClient.App/TextInjection/EditableFocusDetector.cs`
- Modify: `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs`

- [ ] **Step 1: 创建 Win32 P/Invoke**

`Win32FocusNative.cs`：

```csharp
using System.Runtime.InteropServices;

namespace LocalAsrClient.App.TextInjection;

internal static class Win32FocusNative
{
    public const uint GuiThreadInfoFlags = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public System.Drawing.Rectangle rcCaret;
    }

    [DllImport("user32.dll")]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo lpgui);
}
```

> 若 `System.Drawing.Rectangle` 引入额外依赖，改用 4 个 `int` 字段的等价结构。

- [ ] **Step 2: 实现 `EditableFocusDetector`**

```csharp
namespace LocalAsrClient.App.TextInjection;

internal sealed class EditableFocusDetector
{
    private static readonly HashSet<string> EditableClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit",
        "RICHEDIT",
        "RichEdit20W",
        "RichEdit50W",
        "RICHEDIT50W",
        "ThunderRT6TextBox",
    };

    public bool HasEditableFocus()
    {
        var foreground = Win32FocusNative.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var foregroundThread = Win32FocusNative.GetWindowThreadProcessId(foreground, out _);
        var currentThread = Win32PInvokeGetCurrentThreadId();
        var attached = false;
        if (foregroundThread != currentThread)
        {
            attached = Win32FocusNative.AttachThreadInput(currentThread, foregroundThread, attach: true);
        }

        try
        {
            var focus = Win32FocusNative.GetFocus();
            if (focus == IntPtr.Zero)
            {
                return false;
            }

            return IsEditableClassName(focus);
        }
        finally
        {
            if (attached)
            {
                Win32FocusNative.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }
    }

    private static bool IsEditableClassName(IntPtr hwnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        return Win32FocusNative.GetClassName(hwnd, buffer, buffer.Capacity) > 0
            && EditableClassNames.Contains(buffer.ToString());
    }

    [DllImport("kernel32.dll")]
    private static extern uint Win32PInvokeGetCurrentThreadId();
}
```

- [ ] **Step 3: 更新 `SendInputTextInjector`**

```csharp
private readonly EditableFocusDetector _focusDetector = new();

public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(text))
    {
        return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。"));
    }

    if (!_focusDetector.HasEditableFocus())
    {
        return Task.FromResult(new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "未找到可输入位置。"));
    }

    // 现有 SendInput 逻辑不变
}
```

- [ ] **Step 4: 构建验证**

```powershell
dotnet build LocalAsrClient.sln
```

Expected: Build succeeded

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.App/TextInjection
git commit -m "feat: detect editable focus before SendInput injection"
```

---

### Task 9: 全量测试与手动端到端验收

**Files:**
- 无代码变更（除非验收暴露缺陷）

- [ ] **Step 1: 运行全量测试**

```powershell
dotnet test LocalAsrClient.sln -v minimal
```

Expected: All tests passed

- [ ] **Step 2: Release 构建**

```powershell
dotnet build LocalAsrClient.sln -c Release
```

Expected: Build succeeded

- [ ] **Step 3: 记事本基本流程**

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

1. 在设置页配置模型与 whisper-server 路径并保存。
2. 模型页启动服务，健康检查通过。
3. 打开记事本，焦点在编辑区。
4. 右 Ctrl →「正在聆听」→ 说中文 → 右 Ctrl。
5. 确认：浮窗「识别中」→「正在输入」→「已输入」→ 消失；文本写入记事本。

- [ ] **Step 4: 无焦点回退**

1. 焦点切到桌面。
2. 完成一轮录音识别。
3. 确认：浮窗保留，显示识别文本与「复制」，文案「未找到可输入位置」。
4. 再按右 Ctrl →「正在聆听」。

- [ ] **Step 5: 错误恢复**

1. 停止 whisper-server。
2. 右 Ctrl 录音 → 右 Ctrl 停止。
3. 确认：浮窗「输入失败」+ HTTP 错误详情。
4. 启动服务后，再按右 Ctrl → 正常录音，无需重启应用。

- [ ] **Step 6: 若有缺陷则修复并提交**

```powershell
git add src tests docs
git commit -m "fix: complete dictation flow end-to-end verification"
```

若无需代码修改，不创建空提交。

---

## Spec Coverage Checklist

| Spec 要求 | Task |
| --- | --- |
| Error/ResultNeedsAction 后右 Ctrl 重试 | Task 1 |
| Transcribing/Injecting/Ensuring 忽略按键 | Task 1 |
| 防重入 | Task 1 |
| 注入失败 → ResultNeedsAction（非 Error） | Task 2 |
| 空文本处理 | Task 2 |
| Error 记录失败统计、不写历史 | Task 3 |
| 浮窗展示 ErrorMessage + 复制 | Task 4 |
| language=zh 转写参数 | Task 5 |
| HttpClient 与设置同步 | Task 6 |
| 热键异常日志 | Task 7 |
| 焦点检测 + NoEditableTarget | Task 8 |
| 端到端验收 | Task 9 |
| 浮窗 80px WorkArea 定位 | 已实现，Task 9 验证 |
| 成功 700ms 隐藏 | 已实现，Task 9 验证 |

---

## Completion Checklist

- [ ] `dotnet test LocalAsrClient.sln` 全部通过
- [ ] `dotnet build LocalAsrClient.sln -c Release` 成功
- [ ] 记事本注入成功
- [ ] 桌面焦点显示复制回退
- [ ] 转写失败后右 Ctrl 可重试
- [ ] 浮窗错误详情可见
- [ ] 日志文件记录热键/转写异常（`%USERPROFILE%\.lessasr\logs\`）

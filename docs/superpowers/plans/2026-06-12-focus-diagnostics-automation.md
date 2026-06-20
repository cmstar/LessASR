# Focus Diagnostics Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local automated focus/input diagnostics harness that can reproduce LessASR's F10 dictation flow against a controlled test window and produce enough evidence to locate focus or text injection failures.

**Architecture:** The implementation adds a test-only target app, a diagnostic JSONL event sink in `LocalAsrClient.App`, and a test mode that uses `tests/Resources/test-sound.wav` plus a fake ASR result while keeping the real hotkey listener, overlay, and text injector. The first E2E test starts TestTarget and LessASR, sends real F10 key events, then asserts the fixed ASR text reaches TestTarget's native input box while collecting TestTarget, UI Automation, and LessASR diagnostic timelines.

**Tech Stack:** .NET 8, WPF, Windows Forms hosted in WPF, xUnit, FlaUI UIA3, Win32 `SendInput`, JSONL diagnostics.

---

## File Structure

Create or modify these files:

- Modify `src/LocalAsrClient.Core/LessAsrPaths.cs`: add `%USERPROFILE%\.lessasr\diagnostics`.
- Modify `tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj`: add FlaUI packages and copy `tests/Resources/test-sound.wav` to output.
- Track `tests/Resources/test-sound.wav`: fixed 4-second test audio.
- Create `src/LocalAsrClient.App/Diagnostics/DiagnosticEvent.cs`: JSON-serializable event model.
- Create `src/LocalAsrClient.App/Diagnostics/DiagnosticWindowSnapshot.cs`: foreground/focus/active/caret snapshot model.
- Create `src/LocalAsrClient.App/Diagnostics/IDiagnosticEventSink.cs`: diagnostic sink abstraction.
- Create `src/LocalAsrClient.App/Diagnostics/NullDiagnosticEventSink.cs`: no-op default sink.
- Create `src/LocalAsrClient.App/Diagnostics/JsonlDiagnosticEventSink.cs`: file sink under `.lessasr/diagnostics`.
- Create `src/LocalAsrClient.App/Diagnostics/DiagnosticSnapshotCollector.cs`: Win32 snapshot and HWND metadata collector.
- Create `src/LocalAsrClient.App/TestMode/TestModeOptions.cs`: reads test-mode environment variables.
- Create `src/LocalAsrClient.App/TestMode/TestAudioRecorder.cs`: returns `test-sound.wav`.
- Create `src/LocalAsrClient.App/TestMode/TestAsrBackend.cs`: returns fixed text.
- Modify `src/LocalAsrClient.App/Bootstrap/AppServices.cs`: wire diagnostic sink and test-mode fakes.
- Modify `src/LocalAsrClient.App/Hotkeys/GlobalHotkeyListener.cs`: record hotkey callback, match, and suppression events.
- Modify `src/LocalAsrClient.App/TextInjection/InjectionTargetCapture.cs`: record capture before/after events.
- Modify `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml.cs`: record overlay show before/after events.
- Modify `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs`: record injection before/strategy/after events.
- Create `tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj`: local probe window app.
- Create `tests/LocalAsrClient.TestTarget/App.xaml` and `App.xaml.cs`: WPF app entry.
- Create `tests/LocalAsrClient.TestTarget/MainWindow.xaml` and `MainWindow.xaml.cs`: test inputs and visible log panel.
- Create `tests/LocalAsrClient.TestTarget/Diagnostics/TargetEvent.cs`: TestTarget event model.
- Create `tests/LocalAsrClient.TestTarget/Diagnostics/TargetEventRecorder.cs`: sequence and screen log writer.
- Create `tests/LocalAsrClient.TestTarget/Controls/LoggingWinFormsTextBox.cs`: native input that records Win32 messages.
- Modify `LocalAsrClient.sln`: include TestTarget project.
- Create `tests/LocalAsrClient.App.Tests/E2E/UiE2EFactAttribute.cs`: opt-in UI E2E test attribute.
- Create `tests/LocalAsrClient.App.Tests/E2E/ProcessRunner.cs`: starts and disposes app processes.
- Create `tests/LocalAsrClient.App.Tests/E2E/KeyboardInput.cs`: sends F10 via Win32 `SendInput`.
- Create `tests/LocalAsrClient.App.Tests/E2E/DiagnosticLogReader.cs`: reads newest LessASR diagnostics JSONL.
- Create `tests/LocalAsrClient.App.Tests/E2E/FocusDiagnosticsE2ETests.cs`: first full E2E test.

## Task 1: Register Diagnostics Directory and Test Audio Resource

**Files:**
- Modify: `src/LocalAsrClient.Core/LessAsrPaths.cs`
- Modify: `tests/LocalAsrClient.Core.Tests/LessAsrPathsTests.cs`
- Modify: `tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj`
- Track: `tests/Resources/test-sound.wav`

- [ ] **Step 1: Add failing path expectation**

Add this assertion to `LessAsrPathsTests`:

```csharp
Assert.Equal(Path.Combine(LessAsrPaths.AppDataRoot, "diagnostics"), LessAsrPaths.DiagnosticsDirectory);
```

- [ ] **Step 2: Run the path test to verify it fails**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter LessAsrPathsTests
```

Expected: fail because `LessAsrPaths.DiagnosticsDirectory` does not exist.

- [ ] **Step 3: Add diagnostics directory path**

Update `LessAsrPaths`:

```csharp
public const string DiagnosticsDirectoryName = "diagnostics";

public static string DiagnosticsDirectory => Path.Combine(AppDataRoot, DiagnosticsDirectoryName);
```

- [ ] **Step 4: Configure App.Tests to copy test audio**

Add this item group to `tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj`:

```xml
<ItemGroup>
  <Content Include="..\Resources\test-sound.wav" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Run focused verification**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter LessAsrPathsTests
dotnet build tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj
```

Expected: path test passes; App.Tests build output contains `test-sound.wav`.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/LocalAsrClient.Core/LessAsrPaths.cs tests/LocalAsrClient.Core.Tests/LessAsrPathsTests.cs tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj tests/Resources/test-sound.wav
git commit -m "Add diagnostics path and test audio resource"
```

## Task 2: Add Diagnostic Event Sink

**Files:**
- Create: `src/LocalAsrClient.App/Diagnostics/DiagnosticEvent.cs`
- Create: `src/LocalAsrClient.App/Diagnostics/DiagnosticWindowSnapshot.cs`
- Create: `src/LocalAsrClient.App/Diagnostics/IDiagnosticEventSink.cs`
- Create: `src/LocalAsrClient.App/Diagnostics/NullDiagnosticEventSink.cs`
- Create: `src/LocalAsrClient.App/Diagnostics/JsonlDiagnosticEventSink.cs`
- Create: `src/LocalAsrClient.App/Diagnostics/DiagnosticSnapshotCollector.cs`
- Test: `tests/LocalAsrClient.App.Tests/Diagnostics/JsonlDiagnosticEventSinkTests.cs`

- [ ] **Step 1: Write failing JSONL sink test**

Create `JsonlDiagnosticEventSinkTests`:

```csharp
using System.Text.Json;
using LocalAsrClient.App.Diagnostics;

namespace LocalAsrClient.App.Tests.Diagnostics;

public sealed class JsonlDiagnosticEventSinkTests
{
    [Fact]
    public async Task WriteAsyncCreatesJsonLineWithEventNameAndSequence()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var sink = JsonlDiagnosticEventSink.Create(directory);

        await sink.WriteAsync(new DiagnosticEvent(
            SequenceId: 0,
            Timestamp: DateTimeOffset.MinValue,
            EventName: "Test.Event",
            State: "Idle",
            ThreadId: 123,
            Snapshot: DiagnosticWindowSnapshot.Empty,
            Properties: new Dictionary<string, string?> { ["key"] = "value" }));

        var line = Assert.Single(File.ReadAllLines(sink.FilePath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("sequenceId").GetInt64());
        Assert.Equal("Test.Event", document.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("value", document.RootElement.GetProperty("properties").GetProperty("key").GetString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter JsonlDiagnosticEventSinkTests
```

Expected: fail because diagnostics classes do not exist.

- [ ] **Step 3: Add event models and sink interfaces**

Create `DiagnosticWindowSnapshot.cs`:

```csharp
namespace LocalAsrClient.App.Diagnostics;

public sealed record DiagnosticWindowSnapshot(
    DiagnosticWindowInfo ForegroundWindow,
    DiagnosticWindowInfo FocusWindow,
    DiagnosticWindowInfo ActiveWindow,
    DiagnosticWindowInfo CaretWindow)
{
    public static DiagnosticWindowSnapshot Empty { get; } = new(
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty);
}

public sealed record DiagnosticWindowInfo(
    string Hwnd,
    string ClassName,
    int ProcessId,
    string ProcessName,
    string WindowTitle)
{
    public static DiagnosticWindowInfo Empty { get; } = new("0x0", string.Empty, 0, string.Empty, string.Empty);
}
```

Create `DiagnosticEvent.cs`:

```csharp
namespace LocalAsrClient.App.Diagnostics;

public sealed record DiagnosticEvent(
    long SequenceId,
    DateTimeOffset Timestamp,
    string EventName,
    string? State,
    int ThreadId,
    DiagnosticWindowSnapshot Snapshot,
    IReadOnlyDictionary<string, string?> Properties);
```

Create `IDiagnosticEventSink.cs`:

```csharp
namespace LocalAsrClient.App.Diagnostics;

public interface IDiagnosticEventSink : IAsyncDisposable
{
    string? FilePath { get; }

    Task WriteAsync(DiagnosticEvent diagnosticEvent);
}
```

Create `NullDiagnosticEventSink.cs`:

```csharp
namespace LocalAsrClient.App.Diagnostics;

public sealed class NullDiagnosticEventSink : IDiagnosticEventSink
{
    public static NullDiagnosticEventSink Instance { get; } = new();

    private NullDiagnosticEventSink()
    {
    }

    public string? FilePath => null;

    public Task WriteAsync(DiagnosticEvent diagnosticEvent) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 4: Add JSONL sink**

Create `JsonlDiagnosticEventSink.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace LocalAsrClient.App.Diagnostics;

public sealed class JsonlDiagnosticEventSink : IDiagnosticEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _sequenceId;

    private JsonlDiagnosticEventSink(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static JsonlDiagnosticEventSink Create(string diagnosticsDirectory)
    {
        Directory.CreateDirectory(diagnosticsDirectory);
        var fileName = $"diagnostics-{DateTime.Now:yyyy-MM-dd-HHmmss}-p{Environment.ProcessId}.jsonl";
        return new JsonlDiagnosticEventSink(Path.Combine(diagnosticsDirectory, fileName));
    }

    public async Task WriteAsync(DiagnosticEvent diagnosticEvent)
    {
        var next = diagnosticEvent with
        {
            SequenceId = Interlocked.Increment(ref _sequenceId),
            Timestamp = diagnosticEvent.Timestamp == DateTimeOffset.MinValue
                ? DateTimeOffset.Now
                : diagnosticEvent.Timestamp
        };

        var line = JsonSerializer.Serialize(next, JsonOptions);
        await _writeLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(FilePath, line + Environment.NewLine);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writeLock.WaitAsync();
        _writeLock.Release();
        _writeLock.Dispose();
    }
}
```

- [ ] **Step 5: Add snapshot collector**

Create `DiagnosticSnapshotCollector.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Diagnostics;

public static class DiagnosticSnapshotCollector
{
    public static DiagnosticWindowSnapshot Capture()
    {
        var foreground = Win32FocusNative.GetForegroundWindow();
        var info = GetGuiThreadInfo(foreground);
        return new DiagnosticWindowSnapshot(
            Describe(foreground),
            Describe(info.HwndFocus),
            Describe(info.HwndActive),
            Describe(info.HwndCaret));
    }

    public static DiagnosticWindowInfo Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32FocusNative.IsWindow(hwnd))
        {
            return DiagnosticWindowInfo.Empty;
        }

        Win32FocusNative.GetWindowThreadProcessId(hwnd, out var processId);
        var processName = string.Empty;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
        }

        return new DiagnosticWindowInfo(
            $"0x{hwnd.ToInt64():X}",
            GetClassName(hwnd),
            (int)processId,
            processName,
            GetWindowTitle(hwnd));
    }

    private static Win32FocusNative.GuiThreadInfo GetGuiThreadInfo(IntPtr foreground)
    {
        var info = new Win32FocusNative.GuiThreadInfo
        {
            CbSize = Marshal.SizeOf<Win32FocusNative.GuiThreadInfo>()
        };

        if (foreground == IntPtr.Zero)
        {
            return info;
        }

        var threadId = Win32FocusNative.GetWindowThreadProcessId(foreground, out _);
        Win32FocusNative.GetGUIThreadInfo(threadId, ref info);
        return info;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        return Win32FocusNative.GetClassName(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
```

- [ ] **Step 6: Run focused verification**

Run:

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter JsonlDiagnosticEventSinkTests
```

Expected: pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/LocalAsrClient.App/Diagnostics tests/LocalAsrClient.App.Tests/Diagnostics
git commit -m "Add JSONL diagnostic event sink"
```

## Task 3: Add LessASR Test Mode Fakes

**Files:**
- Create: `src/LocalAsrClient.App/TestMode/TestModeOptions.cs`
- Create: `src/LocalAsrClient.App/TestMode/TestAudioRecorder.cs`
- Create: `src/LocalAsrClient.App/TestMode/TestAsrBackend.cs`
- Test: `tests/LocalAsrClient.App.Tests/TestMode/TestModeTests.cs`

- [ ] **Step 1: Write failing test-mode tests**

Create `TestModeTests.cs`:

```csharp
using LocalAsrClient.App.TestMode;

namespace LocalAsrClient.App.Tests.TestMode;

public sealed class TestModeTests
{
    [Fact]
    public async Task TestAudioRecorderReturnsConfiguredWavFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-sound.wav");
        Assert.True(File.Exists(path), $"Missing copied test audio: {path}");

        var recorder = new TestAudioRecorder(path);
        await recorder.StartAsync(CancellationToken.None);
        var result = await recorder.StopAsync(CancellationToken.None);

        Assert.True(result.WavData.Length > 44);
        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task TestAsrBackendReturnsConfiguredText()
    {
        var backend = new TestAsrBackend("LessASR 自动化测试文本");

        var result = await backend.TranscribeAsync(
            new LocalAsrClient.Core.Asr.AsrRequest(
                new LocalAsrClient.Core.Asr.InMemoryAudioInput([1, 2, 3], "wav", 16000, 1),
                "zh",
                null,
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal("LessASR 自动化测试文本", result.Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter TestModeTests
```

Expected: fail because test-mode classes do not exist.

- [ ] **Step 3: Add test mode options**

Create `TestModeOptions.cs`:

```csharp
namespace LocalAsrClient.App.TestMode;

public sealed record TestModeOptions(bool Enabled, bool DiagnosticsEnabled, string AudioPath, string AsrText)
{
    public const string DefaultAsrText = "LessASR 自动化测试文本";

    public static TestModeOptions FromEnvironment()
    {
        var enabled = string.Equals(Environment.GetEnvironmentVariable("LESSASR_TEST_MODE"), "1", StringComparison.Ordinal);
        var diagnosticsEnabled = string.Equals(Environment.GetEnvironmentVariable("LESSASR_DIAGNOSTICS"), "1", StringComparison.Ordinal);
        var audioPath = Environment.GetEnvironmentVariable("LESSASR_TEST_AUDIO")
            ?? Path.Combine(AppContext.BaseDirectory, "test-sound.wav");
        var asrText = Environment.GetEnvironmentVariable("LESSASR_FAKE_ASR_TEXT")
            ?? DefaultAsrText;

        return new TestModeOptions(enabled, diagnosticsEnabled, audioPath, asrText);
    }
}
```

- [ ] **Step 4: Add fake recorder**

Create `TestAudioRecorder.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.TestMode;

public sealed class TestAudioRecorder : IAudioRecorder
{
    private readonly string _wavPath;
    private DateTimeOffset _startedAt;

    public TestAudioRecorder(string wavPath)
    {
        _wavPath = wavPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_wavPath))
        {
            throw new FileNotFoundException("测试音频不存在。", _wavPath);
        }

        _startedAt = DateTimeOffset.Now;
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(_wavPath);
        var duration = DateTimeOffset.Now - _startedAt;
        if (duration < TimeSpan.FromSeconds(4))
        {
            duration = TimeSpan.FromSeconds(4);
        }

        return Task.FromResult(new RecordingResult(bytes, duration, 16000, 1));
    }
}
```

- [ ] **Step 5: Add fake ASR backend**

Create `TestAsrBackend.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.TestMode;

public sealed class TestAsrBackend : IAsrBackend
{
    private readonly string _text;

    public TestAsrBackend(string text)
    {
        _text = text;
    }

    public string Name => "test-asr";

    public AsrBackendStatus Status => AsrBackendStatus.Ready;

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AsrResult(_text, null, TimeSpan.FromMilliseconds(25), null));
    }
}
```

- [ ] **Step 6: Run focused verification**

Run:

```powershell
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter TestModeTests
```

Expected: pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/LocalAsrClient.App/TestMode tests/LocalAsrClient.App.Tests/TestMode
git commit -m "Add LessASR test mode fakes"
```

## Task 4: Wire Diagnostics and Test Mode into AppServices

**Files:**
- Modify: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`
- Modify: `src/LocalAsrClient.App/App.xaml.cs`

- [ ] **Step 1: Wire options and diagnostic sink in AppServices**

At the start of `CreateAsync`, read options and create the sink:

```csharp
var testMode = TestModeOptions.FromEnvironment();
IDiagnosticEventSink diagnosticSink = testMode.DiagnosticsEnabled
    ? JsonlDiagnosticEventSink.Create(LessAsrPaths.DiagnosticsDirectory)
    : NullDiagnosticEventSink.Instance;
```

When constructing services, choose fakes in test mode:

```csharp
IAudioRecorder recorder = testMode.Enabled
    ? new TestAudioRecorder(testMode.AudioPath)
    : new NAudioMemoryRecorder();

IAsrBackend backend = testMode.Enabled
    ? new TestAsrBackend(testMode.AsrText)
    : new ManagedWhisperServerBackend(serverManager, new WhisperServerClient(httpClient));
```

- [ ] **Step 2: Store and dispose diagnostic sink**

Add a property and constructor parameter:

```csharp
public IDiagnosticEventSink DiagnosticSink { get; }
```

Dispose it:

```csharp
await DiagnosticSink.DisposeAsync();
```

- [ ] **Step 3: Publish orchestrator state changes to diagnostics**

After creating the orchestrator:

```csharp
orchestrator.StatusChanged += status =>
{
    _ = diagnosticSink.WriteAsync(new DiagnosticEvent(
        0,
        DateTimeOffset.Now,
        "Dictation.StateChanged",
        status.State.ToString(),
        Environment.CurrentManagedThreadId,
        DiagnosticSnapshotCollector.Capture(),
        new Dictionary<string, string?>
        {
            ["message"] = status.Message,
            ["resultTextLength"] = status.ResultText?.Length.ToString(),
            ["errorMessage"] = status.ErrorMessage
        }));
};
```

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build LocalAsrClient.sln
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/LocalAsrClient.App/Bootstrap/AppServices.cs src/LocalAsrClient.App/App.xaml.cs
git commit -m "Wire diagnostics and test mode"
```

## Task 5: Instrument Hotkey, Capture, Overlay, and Injection Boundaries

**Files:**
- Modify: `src/LocalAsrClient.App/Hotkeys/GlobalHotkeyListener.cs`
- Modify: `src/LocalAsrClient.App/TextInjection/InjectionTargetCapture.cs`
- Modify: `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml.cs`
- Modify: `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs`
- Test: `tests/LocalAsrClient.App.Tests/Diagnostics/DiagnosticInstrumentationTests.cs`

- [ ] **Step 1: Add a recording sink test double**

Create `DiagnosticInstrumentationTests.cs`:

```csharp
using LocalAsrClient.App.Diagnostics;

namespace LocalAsrClient.App.Tests.Diagnostics;

public sealed class RecordingDiagnosticSink : IDiagnosticEventSink
{
    public List<DiagnosticEvent> Events { get; } = [];

    public string? FilePath => null;

    public Task WriteAsync(DiagnosticEvent diagnosticEvent)
    {
        Events.Add(diagnosticEvent);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 2: Instrument `InjectionTargetCapture`**

Add constructor injection:

```csharp
private readonly IDiagnosticEventSink _diagnostics;

public InjectionTargetCapture()
    : this(NullDiagnosticEventSink.Instance)
{
}

public InjectionTargetCapture(IDiagnosticEventSink diagnostics)
{
    _diagnostics = diagnostics;
}
```

At the start and end of `Capture()` write:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("InjectionTargetCapture.Before", null));
```

and:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("InjectionTargetCapture.After", new Dictionary<string, string?>
{
    ["foregroundWindow"] = $"0x{ForegroundWindow.ToInt64():X}",
    ["focusWindow"] = $"0x{FocusWindow.ToInt64():X}",
    ["focusClassName"] = EditableFocusDetector.GetClassName(FocusWindow)
}));
```

- [ ] **Step 3: Instrument `GlobalHotkeyListener`**

Add optional sink constructor overload:

```csharp
private readonly IDiagnosticEventSink _diagnostics;

public GlobalHotkeyListener(int virtualKeyCode)
    : this(virtualKeyCode, NullDiagnosticEventSink.Instance)
{
}

public GlobalHotkeyListener(int virtualKeyCode, IDiagnosticEventSink diagnostics)
{
    _virtualKeyCode = virtualKeyCode;
    _diagnostics = diagnostics;
    _callback = HookCallback;
}
```

In `HookCallback`, record callback, match, and suppression:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("Hotkey.Callback.Enter", message, data, suppressed: false));
```

Before returning `(IntPtr)1`, record:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("Hotkey.Suppressed", message, data, suppressed: true));
```

- [ ] **Step 4: Instrument overlay show**

Change `DictationOverlayWindow` to accept the sink:

```csharp
private readonly IDiagnosticEventSink _diagnostics;

public DictationOverlayWindow()
    : this(NullDiagnosticEventSink.Instance)
{
}

public DictationOverlayWindow(IDiagnosticEventSink diagnostics)
{
    _diagnostics = diagnostics;
    ...
}
```

In `ShowOverlay`, write before and after:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.Before", state));
...
_ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.After", state));
```

- [ ] **Step 5: Instrument text injection**

Add sink to `SendInputTextInjector` constructor:

```csharp
private readonly IDiagnosticEventSink _diagnostics;

public SendInputTextInjector(InjectionTargetCapture targetCapture)
    : this(targetCapture, NullDiagnosticEventSink.Instance)
{
}

public SendInputTextInjector(InjectionTargetCapture targetCapture, IDiagnosticEventSink diagnostics)
{
    _targetCapture = targetCapture;
    _diagnostics = diagnostics;
}
```

Write events:

```csharp
_ = _diagnostics.WriteAsync(CreateEvent("TextInjection.Before", text.Length, null));
_ = _diagnostics.WriteAsync(CreateEvent("TextInjection.StrategySelected", text.Length, method.ToString()));
_ = _diagnostics.WriteAsync(CreateEvent("TextInjection.After", text.Length, result.Status.ToString()));
```

- [ ] **Step 6: Wire instrumented constructors**

In `AppServices.CreateAsync`, construct:

```csharp
var injectionTargetCapture = new InjectionTargetCapture(diagnosticSink);
var injector = new SendInputTextInjector(injectionTargetCapture, diagnosticSink);
var overlayWindow = new DictationOverlayWindow(diagnosticSink);
var hotkeyListener = new GlobalHotkeyListener(DictationHotkey.ToggleVirtualKey, diagnosticSink);
```

- [ ] **Step 7: Run verification**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter DiagnosticInstrumentationTests
```

Expected: build succeeds. `DiagnosticInstrumentationTests` compiles with the shared `RecordingDiagnosticSink`, and boundary behavior is verified by the E2E test in Task 8 because the global hook and overlay require a desktop session.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src/LocalAsrClient.App/Hotkeys src/LocalAsrClient.App/TextInjection src/LocalAsrClient.App/Overlay src/LocalAsrClient.App/Bootstrap tests/LocalAsrClient.App.Tests/Diagnostics
git commit -m "Instrument focus diagnostics boundaries"
```

## Task 6: Add Local TestTarget App

**Files:**
- Create: `tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj`
- Create: `tests/LocalAsrClient.TestTarget/App.xaml`
- Create: `tests/LocalAsrClient.TestTarget/App.xaml.cs`
- Create: `tests/LocalAsrClient.TestTarget/MainWindow.xaml`
- Create: `tests/LocalAsrClient.TestTarget/MainWindow.xaml.cs`
- Create: `tests/LocalAsrClient.TestTarget/Diagnostics/TargetEvent.cs`
- Create: `tests/LocalAsrClient.TestTarget/Diagnostics/TargetEventRecorder.cs`
- Create: `tests/LocalAsrClient.TestTarget/Controls/LoggingWinFormsTextBox.cs`
- Modify: `LocalAsrClient.sln`

- [ ] **Step 1: Create project file**

Create `LocalAsrClient.TestTarget.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add App entry**

Create `App.xaml`:

```xml
<Application x:Class="LocalAsrClient.TestTarget.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
```

Create `App.xaml.cs`:

```csharp
using System.Windows;

namespace LocalAsrClient.TestTarget;

public partial class App : Application
{
}
```

- [ ] **Step 3: Add event model and recorder**

Create `TargetEvent.cs`:

```csharp
namespace LocalAsrClient.TestTarget.Diagnostics;

public sealed record TargetEvent(long SequenceId, DateTimeOffset Timestamp, string EventName, string Details);
```

Create `TargetEventRecorder.cs`:

```csharp
using System.Collections.ObjectModel;

namespace LocalAsrClient.TestTarget.Diagnostics;

public sealed class TargetEventRecorder
{
    private long _sequenceId;

    public ObservableCollection<string> Lines { get; } = [];

    public void Record(string eventName, string details)
    {
        var id = Interlocked.Increment(ref _sequenceId);
        Lines.Add($"{id:000} {DateTime.Now:HH:mm:ss.fff} {eventName} {details}");
    }

    public void Clear()
    {
        Lines.Clear();
        _sequenceId = 0;
    }
}
```

- [ ] **Step 4: Add native logging text box**

Create `LoggingWinFormsTextBox.cs`:

```csharp
using System.Windows.Forms;
using LocalAsrClient.TestTarget.Diagnostics;

namespace LocalAsrClient.TestTarget.Controls;

public sealed class LoggingWinFormsTextBox : TextBox
{
    private readonly TargetEventRecorder _recorder;

    public LoggingWinFormsTextBox(TargetEventRecorder recorder)
    {
        _recorder = recorder;
        Multiline = true;
        AcceptsReturn = true;
        Width = 420;
        Height = 90;
    }

    protected override void WndProc(ref Message m)
    {
        const int wmSetFocus = 0x0007;
        const int wmKillFocus = 0x0008;
        const int wmKeyDown = 0x0100;
        const int wmKeyUp = 0x0101;
        const int wmChar = 0x0102;
        const int wmSysKeyDown = 0x0104;
        const int wmSysKeyUp = 0x0105;
        const int wmPaste = 0x0302;

        if (m.Msg is wmSetFocus or wmKillFocus or wmKeyDown or wmKeyUp or wmChar or wmSysKeyDown or wmSysKeyUp or wmPaste)
        {
            _recorder.Record($"NativeTextBox.WM_0x{m.Msg:X4}", $"wParam=0x{m.WParam.ToInt64():X}");
        }

        base.WndProc(ref m);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _recorder.Record("Target.NativeTextBox.TextChanged", $"length={Text.Length}");
        base.OnTextChanged(e);
    }
}
```

- [ ] **Step 5: Add TestTarget UI**

Create `MainWindow.xaml`:

```xml
<Window x:Class="LocalAsrClient.TestTarget.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wfi="clr-namespace:System.Windows.Forms.Integration;assembly=WindowsFormsIntegration"
        Title="LessASR TestTarget"
        Width="900"
        Height="650">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
            <Button x:Name="ClearButton"
                    AutomationProperties.AutomationId="ClearButton"
                    Content="Clear"
                    Width="80"
                    Margin="0,0,8,0"
                    Click="ClearButton_Click"/>
            <Button x:Name="FocusNativeButton"
                    AutomationProperties.AutomationId="FocusNativeButton"
                    Content="Focus Native"
                    Width="120"
                    Click="FocusNativeButton_Click"/>
        </StackPanel>
        <Grid Grid.Row="1" Margin="0,0,0,12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0" Margin="0,0,12,0">
                <TextBlock Text="Native / WinForms TextBox"/>
                <wfi:WindowsFormsHost x:Name="NativeTextBoxHost"
                                      AutomationProperties.AutomationId="NativeTextBoxHost"
                                      Height="100"/>
            </StackPanel>
            <StackPanel Grid.Column="1">
                <TextBlock Text="WPF TextBox"/>
                <TextBox x:Name="WpfTextBox"
                         AutomationProperties.AutomationId="WpfTextBox"
                         Height="100"
                         AcceptsReturn="True"
                         TextChanged="WpfTextBox_TextChanged"
                         GotKeyboardFocus="Control_GotKeyboardFocus"
                         LostKeyboardFocus="Control_LostKeyboardFocus"/>
                <TextBlock Text="Read-only TextBox" Margin="0,12,0,0"/>
                <TextBox x:Name="ReadOnlyTextBox"
                         AutomationProperties.AutomationId="ReadOnlyTextBox"
                         IsReadOnly="True"
                         Text="Read only target"/>
            </StackPanel>
        </Grid>
        <TextBox Grid.Row="2"
                 x:Name="ScreenLogTextBox"
                 AutomationProperties.AutomationId="ScreenLogTextBox"
                 IsReadOnly="True"
                 VerticalScrollBarVisibility="Auto"
                 TextWrapping="NoWrap"/>
    </Grid>
</Window>
```

- [ ] **Step 6: Add MainWindow code-behind**

Create `MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LocalAsrClient.TestTarget.Controls;
using LocalAsrClient.TestTarget.Diagnostics;

namespace LocalAsrClient.TestTarget;

public partial class MainWindow : Window
{
    private readonly TargetEventRecorder _recorder = new();
    private readonly LoggingWinFormsTextBox _nativeTextBox;

    public MainWindow()
    {
        InitializeComponent();
        _nativeTextBox = new LoggingWinFormsTextBox(_recorder);
        NativeTextBoxHost.Child = _nativeTextBox;
        _recorder.Lines.CollectionChanged += (_, _) =>
        {
            ScreenLogTextBox.Text = string.Join(Environment.NewLine, _recorder.Lines);
            ScreenLogTextBox.ScrollToEnd();
        };
        Activated += (_, _) => _recorder.Record("Target.Window.Activated", string.Empty);
        Deactivated += (_, _) => _recorder.Record("Target.Window.Deactivated", string.Empty);
        Loaded += (_, _) => FocusNativeInput();
    }

    public string NativeText => _nativeTextBox.Text;

    protected override void OnClosing(CancelEventArgs e)
    {
        _recorder.Record("Target.Window.Closing", string.Empty);
        base.OnClosing(e);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _nativeTextBox.Clear();
        WpfTextBox.Clear();
        _recorder.Clear();
        FocusNativeInput();
    }

    private void FocusNativeButton_Click(object sender, RoutedEventArgs e)
    {
        FocusNativeInput();
    }

    private void FocusNativeInput()
    {
        _nativeTextBox.Focus();
        _recorder.Record("Target.NativeTextBox.FocusRequested", string.Empty);
    }

    private void Control_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.GotKeyboardFocus", string.Empty);
    }

    private void Control_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.LostKeyboardFocus", string.Empty);
    }

    private void WpfTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.TextChanged", $"length={WpfTextBox.Text.Length}");
    }
}
```

- [ ] **Step 7: Add project to solution**

Run:

```powershell
dotnet sln LocalAsrClient.sln add tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj
```

Expected: solution includes TestTarget under the default solution folder; Visual Studio may not nest it under `tests`, which is acceptable for now.

- [ ] **Step 8: Build TestTarget**

Run:

```powershell
dotnet build tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

Run:

```powershell
git add LocalAsrClient.sln tests/LocalAsrClient.TestTarget
git commit -m "Add local focus diagnostics test target"
```

## Task 7: Add UI E2E Test Harness

**Files:**
- Modify: `tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj`
- Create: `tests/LocalAsrClient.App.Tests/E2E/UiE2EFactAttribute.cs`
- Create: `tests/LocalAsrClient.App.Tests/E2E/ProcessRunner.cs`
- Create: `tests/LocalAsrClient.App.Tests/E2E/KeyboardInput.cs`
- Create: `tests/LocalAsrClient.App.Tests/E2E/DiagnosticLogReader.cs`

- [ ] **Step 1: Add FlaUI packages**

Run:

```powershell
dotnet add tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj package FlaUI.Core
dotnet add tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj package FlaUI.UIA3
```

Expected: package references are added to the App test project.

- [ ] **Step 2: Add opt-in E2E fact attribute**

Create `UiE2EFactAttribute.cs`:

```csharp
namespace LocalAsrClient.App.Tests.E2E;

public sealed class UiE2EFactAttribute : FactAttribute
{
    public UiE2EFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LESSASR_RUN_UI_E2E"), "1", StringComparison.Ordinal))
        {
            Skip = "Set LESSASR_RUN_UI_E2E=1 to run desktop UI E2E tests.";
        }
    }
}
```

- [ ] **Step 3: Add process runner**

Create `ProcessRunner.cs`:

```csharp
using System.Diagnostics;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class ProcessRunner : IAsyncDisposable
{
    private readonly List<Process> _processes = [];

    public Process Start(string fileName, string arguments = "", IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
        };

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        _processes.Add(process);
        return process;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Add F10 SendInput helper**

Create `KeyboardInput.cs`:

```csharp
using System.Runtime.InteropServices;

namespace LocalAsrClient.App.Tests.E2E;

public static class KeyboardInput
{
    private const ushort VirtualKeyF10 = 0x79;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    public static void PressF10()
    {
        var inputs = new[]
        {
            Create(VirtualKeyF10, keyUp: false),
            Create(VirtualKeyF10, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} inputs.");
        }
    }

    private static Input Create(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            KeyboardInput = new KeyboardInputData
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyEventFKeyUp : 0
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInputData KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
```

- [ ] **Step 5: Add diagnostics log reader**

Create `DiagnosticLogReader.cs`:

```csharp
namespace LocalAsrClient.App.Tests.E2E;

public static class DiagnosticLogReader
{
    public static string GetNewestDiagnosticsFile()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lessasr",
            "diagnostics");

        var file = Directory
            .EnumerateFiles(directory, "diagnostics-*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return file ?? throw new FileNotFoundException($"No LessASR diagnostic log found in {directory}.");
    }

    public static string ReadAll(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
```

- [ ] **Step 6: Build App.Tests**

Run:

```powershell
dotnet build tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj tests/LocalAsrClient.App.Tests/E2E
git commit -m "Add UI E2E harness"
```

## Task 8: Add First Focus Diagnostics E2E Test

**Files:**
- Create: `tests/LocalAsrClient.App.Tests/E2E/FocusDiagnosticsE2ETests.cs`

- [ ] **Step 1: Add E2E test**

Create `FocusDiagnosticsE2ETests.cs`:

```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class FocusDiagnosticsE2ETests
{
    private const string ExpectedText = "LessASR 自动化测试文本";

    [UiE2EFact]
    public async Task F10DictationInjectsFakeAsrTextIntoNativeTarget()
    {
        await using var runner = new ProcessRunner();
        var repo = FindRepoRoot();
        var targetExe = Path.Combine(repo, "tests", "LocalAsrClient.TestTarget", "bin", "Debug", "net8.0-windows", "LocalAsrClient.TestTarget.exe");
        var appExe = Path.Combine(repo, "src", "LocalAsrClient.App", "bin", "Debug", "net8.0-windows", "LocalAsrClient.App.exe");
        var audioPath = Path.Combine(AppContext.BaseDirectory, "test-sound.wav");

        Assert.True(File.Exists(targetExe), $"Build TestTarget first: {targetExe}");
        Assert.True(File.Exists(appExe), $"Build LessASR App first: {appExe}");
        Assert.True(File.Exists(audioPath), $"Copied audio missing: {audioPath}");

        using var automation = new UIA3Automation();
        var targetProcess = runner.Start(targetExe);
        var targetWindow = await WaitForWindowAsync(automation, targetProcess.Id, "LessASR TestTarget");

        var clearButton = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("ClearButton"))!.AsButton();
        clearButton.Invoke();

        var environment = new Dictionary<string, string>
        {
            ["LESSASR_TEST_MODE"] = "1",
            ["LESSASR_DIAGNOSTICS"] = "1",
            ["LESSASR_TEST_AUDIO"] = audioPath,
            ["LESSASR_FAKE_ASR_TEXT"] = ExpectedText
        };

        runner.Start(appExe, environment: environment);
        await Task.Delay(1000);

        targetWindow.Focus();
        var focusButton = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("FocusNativeButton"))!.AsButton();
        focusButton.Invoke();

        KeyboardInput.PressF10();
        await WaitForDiagnosticsEventAsync("Dictation.StateChanged", "Recording", TimeSpan.FromSeconds(5));

        KeyboardInput.PressF10();
        await WaitForDiagnosticsEventAsync("TextInjection.After", null, TimeSpan.FromSeconds(10));

        await WaitUntilAsync(() =>
        {
            targetWindow.Focus();
            var screenLog = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("ScreenLogTextBox"))!.AsTextBox();
            return screenLog.Text.Contains("TextChanged", StringComparison.Ordinal)
                || screenLog.Text.Contains("WM_0x0102", StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        var diagnosticsPath = DiagnosticLogReader.GetNewestDiagnosticsFile();
        var diagnostics = DiagnosticLogReader.ReadAll(diagnosticsPath);
        Assert.Contains("InjectionTargetCapture.After", diagnostics);
        Assert.Contains("Overlay.Show.After", diagnostics);
        Assert.Contains("TextInjection.After", diagnostics);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LocalAsrClient.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static async Task<Window> WaitForWindowAsync(UIA3Automation automation, int processId, string title)
    {
        Window? window = null;
        await WaitUntilAsync(() =>
        {
            var desktop = automation.GetDesktop();
            window = desktop.FindFirstChild(cf => cf.ByProcessId(processId).And(cf.ByName(title)))?.AsWindow();
            return window is not null;
        }, TimeSpan.FromSeconds(10));

        return window!;
    }

    private static async Task WaitForDiagnosticsEventAsync(string eventName, string? state, TimeSpan timeout)
    {
        await WaitUntilAsync(() =>
        {
            try
            {
                var path = DiagnosticLogReader.GetNewestDiagnosticsFile();
                var text = DiagnosticLogReader.ReadAll(path);
                return text.Contains(eventName, StringComparison.Ordinal)
                    && (state is null || text.Contains(state, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }, timeout);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition was not met within {timeout}.");
    }
}
```

- [ ] **Step 2: Build required executables**

Run:

```powershell
dotnet build src/LocalAsrClient.App/LocalAsrClient.App.csproj
dotnet build tests/LocalAsrClient.TestTarget/LocalAsrClient.TestTarget.csproj
dotnet build tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj
```

Expected: all three builds succeed.

- [ ] **Step 3: Run E2E test explicitly**

Run:

```powershell
$env:LESSASR_RUN_UI_E2E='1'
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter FocusDiagnosticsE2ETests
```

Expected: initially this may fail if the existing focus bug reproduces. If it fails, the test output and `.lessasr/diagnostics` JSONL log should identify the failing boundary.

- [ ] **Step 4: Commit**

Run:

```powershell
git add tests/LocalAsrClient.App.Tests/E2E/FocusDiagnosticsE2ETests.cs
git commit -m "Add focus diagnostics E2E test"
```

## Task 9: Document How to Run Diagnostics

**Files:**
- Modify: `docs/development.md`
- Modify: `docs/superpowers/specs/2026-06-12-focus-diagnostics-automation-design.md`

- [ ] **Step 1: Add development documentation**

Add this section to `docs/development.md`:

````markdown
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
````

- [ ] **Step 2: Verify documentation commands**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: normal tests pass. UI E2E is skipped unless `LESSASR_RUN_UI_E2E=1` is set.

- [ ] **Step 3: Commit**

Run:

```powershell
git add docs/development.md docs/superpowers/specs/2026-06-12-focus-diagnostics-automation-design.md
git commit -m "Document focus diagnostics test workflow"
```

## Final Verification

- [ ] Run normal test suite:

```powershell
dotnet test LocalAsrClient.sln
```

Expected: all non-E2E tests pass; UI E2E test is skipped unless explicitly enabled.

- [ ] Run explicit UI E2E on a Windows desktop session:

```powershell
$env:LESSASR_RUN_UI_E2E='1'
dotnet test tests/LocalAsrClient.App.Tests/LocalAsrClient.App.Tests.csproj --filter FocusDiagnosticsE2ETests
```

Expected: either passes by injecting `LessASR 自动化测试文本` into TestTarget, or fails with a complete timeline proving which boundary broke.

- [ ] Inspect newest diagnostics log:

```powershell
Get-ChildItem "$env:USERPROFILE\.lessasr\diagnostics" -Filter diagnostics-*.jsonl | Sort-Object LastWriteTime -Descending | Select-Object -First 1
```

Expected: newest file contains `Hotkey`, `InjectionTargetCapture`, `Overlay`, `TextInjection`, and `Dictation.StateChanged` events for the run.

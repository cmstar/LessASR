# 连续听写模式 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 F9 连续听写专用窗口：分段录音、50 段识别队列、右 Ctrl 句子边界、关窗合并历史，且连续窗口开着时单句听写不可用。

**Architecture:** Core 新增 `ContinuousDictationSession`（段列表 + 单路录音 + FIFO 转写队列）与可复用的 `TranscriptionPipeline`（ASR + 后处理 + 按段统计）；App 新增 `ContinuousDictationCoordinator`、F9 热键与 WPF 窗口，在 `AppServices` 中按「连续窗口是否打开」路由右 Ctrl / Esc。

**Tech Stack:** C# / .NET 8, WPF, xUnit, NAudio, Win32 低级键盘钩子

---

## Reference Spec

`docs/superpowers/specs/2026-06-23-continuous-dictation-design.md`

## File Map

| 文件 | 职责 |
| --- | --- |
| `src/LocalAsrClient.Core/Dictation/ContinuousDictationSegmentState.cs` | 段状态枚举 |
| `src/LocalAsrClient.Core/Dictation/ContinuousDictationSegment.cs` | 段模型（Id/State/Text/ErrorMessage） |
| `src/LocalAsrClient.Core/Dictation/ContinuousDictationSnapshot.cs` | 会话快照（段列表、是否录制中、计数、横幅消息） |
| `src/LocalAsrClient.Core/Dictation/ContinuousDictationTextMerge.cs` | Completed 段 `\n` 合并（复制/关窗历史） |
| `src/LocalAsrClient.Core/Dictation/TranscriptionPipeline.cs` | 单段 WAV → ASR → 后处理 → 统计 |
| `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs` | 连续听写 Core 编排 |
| `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationSessionTests.cs` | 会话行为单测 |
| `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationTextMergeTests.cs` | 文本合并单测 |
| `src/LocalAsrClient.App/Hotkeys/ContinuousDictationHotkey.cs` | F9 键位常量 |
| `src/LocalAsrClient.App/Hotkeys/Win32HotkeyNative.cs` | 新增 `VkF9` |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationStrings.cs` | 界面中文文案 |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationCoordinator.cs` | 窗口生命周期、热键、关窗历史 |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationViewModel.cs` | 窗口 VM |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousSegmentViewModel.cs` | 单段 VM（Placeholder/IsEditable） |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationWindow.xaml` | 连续听写 UI |
| `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationWindow.xaml.cs` | 窗口代码隐藏 |
| `src/LocalAsrClient.App/Bootstrap/AppServices.cs` | 双录音器、双热键、Esc 路由 |
| `src/LocalAsrClient.App/App.xaml.cs` | 启动 F9 监听 |
| `docs/domain.md` | 连续听写领域规则 |
| `docs/architecture.md` | 模块与数据流 |

---

### Task 1: 段模型与文本合并

**Files:**
- Create: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSegmentState.cs`
- Create: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSegment.cs`
- Create: `src/LocalAsrClient.Core/Dictation/ContinuousDictationTextMerge.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationTextMergeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationTextMergeTests.cs
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class ContinuousDictationTextMergeTests
{
    [Fact]
    public void MergeCompletedSegments_JoinsWithNewLine_SkipsNonCompleted()
    {
        var segments = new[]
        {
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Completed, "第一句", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Transcribing, "", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Completed, "第二句", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Failed, "", "超时")
        };

        var merged = ContinuousDictationTextMerge.MergeCompletedSegments(segments);

        Assert.Equal("第一句\n第二句", merged);
    }

    [Fact]
    public void MergeCompletedSegments_WhenNoneCompleted_ReturnsEmpty()
    {
        var segments = new[]
        {
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null)
        };

        Assert.Equal(string.Empty, ContinuousDictationTextMerge.MergeCompletedSegments(segments));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~ContinuousDictationTextMergeTests" -v minimal
```

Expected: FAIL（类型未定义）。

- [ ] **Step 3: 实现模型与合并**

```csharp
// src/LocalAsrClient.Core/Dictation/ContinuousDictationSegmentState.cs
namespace LocalAsrClient.Core.Dictation;

public enum ContinuousSegmentState
{
    WaitingInput,
    Transcribing,
    Completed,
    Failed
}
```

```csharp
// src/LocalAsrClient.Core/Dictation/ContinuousDictationSegment.cs
namespace LocalAsrClient.Core.Dictation;

public sealed record ContinuousDictationSegment(
    Guid Id,
    ContinuousSegmentState State,
    string Text,
    string? ErrorMessage);
```

```csharp
// src/LocalAsrClient.Core/Dictation/ContinuousDictationTextMerge.cs
namespace LocalAsrClient.Core.Dictation;

public static class ContinuousDictationTextMerge
{
    public static string MergeCompletedSegments(IEnumerable<ContinuousDictationSegment> segments)
    {
        return string.Join(
            "\n",
            segments
                .Where(s => s.State == ContinuousSegmentState.Completed && !string.IsNullOrWhiteSpace(s.Text))
                .Select(s => s.Text));
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~ContinuousDictationTextMergeTests" -v minimal
```

Expected: PASS（2 tests）。

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Dictation/ContinuousDictationSegmentState.cs src/LocalAsrClient.Core/Dictation/ContinuousDictationSegment.cs src/LocalAsrClient.Core/Dictation/ContinuousDictationTextMerge.cs tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationTextMergeTests.cs
git commit -m "feat(core): 添加连续听写段模型与 Completed 文本合并。"
```

---

### Task 2: TranscriptionPipeline（单段转写 + 统计）

**Files:**
- Create: `src/LocalAsrClient.Core/Dictation/TranscriptionPipeline.cs`
- Create: `src/LocalAsrClient.Core/Dictation/TranscriptionPipelineResult.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/TranscriptionPipelineTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/LocalAsrClient.Core.Tests/Dictation/TranscriptionPipelineTests.cs
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class TranscriptionPipelineTests
{
    [Fact]
    public async Task TranscribeAsync_OnSuccess_RecordsSucceededStats()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "你好" };
        var stats = new StubStatsRepository();
        var settings = new StubSettingsStore();
        var pipeline = new TranscriptionPipeline(backend, settings, new NoOpTextPostProcessor(), stats, new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("你好", result.Text);
        Assert.Single(stats.Recorded);
        Assert.True(stats.Recorded[0].Succeeded);
    }

    [Fact]
    public async Task TranscribeAsync_OnEmptyText_RecordsFailedStats()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "  " };
        var stats = new StubStatsRepository();
        var pipeline = new TranscriptionPipeline(backend, new StubSettingsStore(), new NoOpTextPostProcessor(), stats, new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(stats.Recorded);
        Assert.False(stats.Recorded[0].Succeeded);
    }

    // 复用 DictationOrchestratorTests 内 StubBackend / StubStatsRepository / StubSettingsStore / StubClock，
    // 可将它们提取到 tests/LocalAsrClient.Core.Tests/Dictation/DictationTestDoubles.cs 供本文件与后续测试共用。
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~TranscriptionPipelineTests" -v minimal
```

Expected: FAIL。

- [ ] **Step 3: 实现 Pipeline**

```csharp
// src/LocalAsrClient.Core/Dictation/TranscriptionPipelineResult.cs
namespace LocalAsrClient.Core.Dictation;

public sealed record TranscriptionPipelineResult(
    bool Succeeded,
    string Text,
    string? ErrorMessage,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration);
```

```csharp
// src/LocalAsrClient.Core/Dictation/TranscriptionPipeline.cs
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Dictation;

public sealed class TranscriptionPipeline
{
    private readonly IAsrBackend _asrBackend;
    private readonly ISettingsStore _settingsStore;
    private readonly ITextPostProcessor _postProcessor;
    private readonly IStatsRepository _statsRepository;
    private readonly IClock _clock;

    public TranscriptionPipeline(
        IAsrBackend asrBackend,
        ISettingsStore settingsStore,
        ITextPostProcessor postProcessor,
        IStatsRepository statsRepository,
        IClock clock)
    {
        _asrBackend = asrBackend;
        _settingsStore = settingsStore;
        _postProcessor = postProcessor;
        _statsRepository = statsRepository;
        _clock = clock;
    }

    public async Task<TranscriptionPipelineResult> TranscribeAsync(
        RecordingResult recording,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            var language = TranscriptionLanguageCatalog.ResolveLanguage(settings.PreferredTranscriptionLanguageId);
            var asrResult = await _asrBackend.TranscribeAsync(
                new AsrRequest(
                    new InMemoryAudioInput(recording.WavData, "wav", recording.SampleRate, recording.Channels),
                    Language: language,
                    Options: new Dictionary<string, string>()),
                cancellationToken);

            var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);
            var processingDuration = asrResult.ProcessingDuration ?? TimeSpan.Zero;
            var succeeded = !string.IsNullOrWhiteSpace(finalText);

            await RecordStatsAsync(finalText, recording.Duration, processingDuration, succeeded, cancellationToken);

            return succeeded
                ? new TranscriptionPipelineResult(true, finalText, null, recording.Duration, processingDuration)
                : new TranscriptionPipelineResult(false, string.Empty, "识别文本为空", recording.Duration, processingDuration);
        }
        catch (Exception ex)
        {
            await RecordStatsAsync(string.Empty, recording.Duration, TimeSpan.Zero, succeeded: false, cancellationToken);
            return new TranscriptionPipelineResult(false, string.Empty, ex.Message, recording.Duration, TimeSpan.Zero);
        }
    }

    private async Task RecordStatsAsync(
        string text,
        TimeSpan recordingDuration,
        TimeSpan processingDuration,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _statsRepository.RecordAsync(
            new DailyStatsDelta(
                _clock.Today,
                succeeded,
                recordingDuration,
                processingDuration,
                TextMetrics.CountCharacters(text),
                TextMetrics.CountWords(text)),
            cancellationToken);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~TranscriptionPipelineTests" -v minimal
```

Expected: PASS。

- [ ] **Step 5: Commit**

```powershell
git add src/LocalAsrClient.Core/Dictation/TranscriptionPipeline.cs src/LocalAsrClient.Core/Dictation/TranscriptionPipelineResult.cs tests/LocalAsrClient.Core.Tests/Dictation/TranscriptionPipelineTests.cs tests/LocalAsrClient.Core.Tests/Dictation/DictationTestDoubles.cs
git commit -m "feat(core): 抽取 TranscriptionPipeline 供连续听写按段转写与统计。"
```

---

### Task 3: ContinuousDictationSession — 录制开关（F9）

**Files:**
- Create: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSnapshot.cs`
- Create: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationSessionTests.cs`

- [ ] **Step 1: 写失败测试 — F9 开录与再开录追加**

```csharp
[Fact]
public async Task ToggleRecordingAsync_WhenInactive_StartsFirstWaitingSegment()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;

    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    var snap = fixture.LastSnapshot;
    Assert.True(snap.IsRecordingActive);
    Assert.Single(snap.Segments);
    Assert.Equal(ContinuousSegmentState.WaitingInput, snap.Segments[0].State);
    Assert.True(fixture.Recorder.Started);
}

[Fact]
public async Task ToggleRecordingAsync_WhenActive_StopsRecordingAndQueuesSegment()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    Assert.False(fixture.LastSnapshot.IsRecordingActive);
    Assert.Equal(ContinuousSegmentState.Transcribing, fixture.LastSnapshot.Segments[0].State);
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter "FullyQualifiedName~ContinuousDictationSessionTests.ToggleRecordingAsync" -v minimal
```

- [ ] **Step 3: 实现 Session 骨架（F9 + 快照）**

```csharp
// src/LocalAsrClient.Core/Dictation/ContinuousDictationSnapshot.cs
namespace LocalAsrClient.Core.Dictation;

public sealed record ContinuousDictationSnapshot(
    IReadOnlyList<ContinuousDictationSegment> Segments,
    bool IsRecordingActive,
    int CompletedCount,
    int TotalCount,
    string? BannerMessage);
```

```csharp
// ContinuousDictationSession 核心字段与方法（节选）
public sealed class ContinuousDictationSession
{
    public const int MaxQueueDepth = 50;
    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromMilliseconds(300);

    private readonly List<ContinuousDictationSegment> _segments = new();
    private readonly IAudioRecorder _recorder;
    private readonly TranscriptionPipeline _pipeline;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _isRecordingActive;
    private CancellationTokenSource? _workerCts;

    public event Action<ContinuousDictationSnapshot>? Changed;

    public bool IsRecordingActive => _isRecordingActive;
    public bool IsWindowOpen { get; private set; } = true;

    public async Task ToggleRecordingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_isRecordingActive)
            {
                await StartRecordingInternalAsync(cancellationToken);
            }
            else
            {
                await CommitCurrentSegmentInternalAsync(startNext: false, cancellationToken);
                _isRecordingActive = false;
            }
            Publish();
        }
        finally { _gate.Release(); }
    }

    private async Task StartRecordingInternalAsync(CancellationToken cancellationToken)
    {
        _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
        _isRecordingActive = true;
        await _recorder.StartAsync(cancellationToken);
    }

    private void Publish(string? banner = null)
    {
        Changed?.Invoke(new ContinuousDictationSnapshot(
            _segments.ToList(),
            _isRecordingActive,
            _segments.Count(s => s.State == ContinuousSegmentState.Completed),
            _segments.Count,
            banner));
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(core): ContinuousDictationSession 支持 F9 录制开关。"
```

---

### Task 4: 句子边界（右 Ctrl）与短段过滤

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationSessionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task CommitSegmentBoundaryAsync_EnqueuesAndStartsNextWaitingSegment()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.Session.CommitSegmentBoundaryAsync(CancellationToken.None);

    Assert.True(fixture.LastSnapshot.IsRecordingActive);
    Assert.Equal(2, fixture.LastSnapshot.Segments.Count);
    Assert.Equal(ContinuousSegmentState.Transcribing, fixture.LastSnapshot.Segments[0].State);
    Assert.Equal(ContinuousSegmentState.WaitingInput, fixture.LastSnapshot.Segments[1].State);
}

[Fact]
public async Task CommitSegmentBoundaryAsync_WhenTooShort_RemovesWaitingSegment()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Recorder.DurationOverride = TimeSpan.FromMilliseconds(100);
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.Session.CommitSegmentBoundaryAsync(CancellationToken.None);

    Assert.Single(fixture.LastSnapshot.Segments);
    Assert.Equal(ContinuousSegmentState.WaitingInput, fixture.LastSnapshot.Segments[0].State);
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现 `CommitSegmentBoundaryAsync` 与 `ShouldSkipSegment`**

```csharp
public async Task CommitSegmentBoundaryAsync(CancellationToken cancellationToken)
{
    await _gate.WaitAsync(cancellationToken);
    try
    {
        if (!_isRecordingActive)
        {
            return;
        }

        var hasNext = await CommitCurrentSegmentInternalAsync(startNext: true, cancellationToken);
        if (!hasNext)
        {
            _isRecordingActive = false;
        }
        Publish();
    }
    finally { _gate.Release(); }
}

private async Task<bool> CommitCurrentSegmentInternalAsync(bool startNext, CancellationToken cancellationToken)
{
    var waitingIndex = _segments.FindLastIndex(s => s.State == ContinuousSegmentState.WaitingInput);
    if (waitingIndex < 0)
    {
        return false;
    }

    var recording = await _recorder.StopAsync(cancellationToken);
    if (recording.Duration < MinRecordingDuration)
    {
        _segments.RemoveAt(waitingIndex);
        if (startNext)
        {
            await _recorder.StartAsync(cancellationToken);
            _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
            return true;
        }
        return false;
    }

    var segmentId = _segments[waitingIndex].Id;
    _segments[waitingIndex] = _segments[waitingIndex] with { State = ContinuousSegmentState.Transcribing };
    EnqueueTranscription(segmentId, recording);

    if (startNext)
    {
        if (GetPendingTranscriptionCount() >= MaxQueueDepth)
        {
            Publish("已达识别上限（50 段），已停止录制");
            return false;
        }

        await _recorder.StartAsync(cancellationToken);
        _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
        return true;
    }

    return false;
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(core): 连续听写支持 Ctrl 分段与短段过滤。"
```

---

### Task 5: 转写队列 Worker 与段状态回填

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationSessionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task QueueWorker_OnSuccess_MarksSegmentCompleted()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Backend.TranscribeText = "段落一";
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.WaitForQueueDrainAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(ContinuousSegmentState.Completed, fixture.LastSnapshot.Segments[0].State);
    Assert.Equal("段落一", fixture.LastSnapshot.Segments[0].Text);
}

[Fact]
public async Task QueueWorker_OnFailure_MarksSegmentFailed()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    fixture.Backend.TranscribeThrows = new InvalidOperationException("连接失败");
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.WaitForQueueDrainAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(ContinuousSegmentState.Failed, fixture.LastSnapshot.Segments[0].State);
    Assert.Contains("连接失败", fixture.LastSnapshot.Segments[0].ErrorMessage, StringComparison.Ordinal);
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现后台队列**

```csharp
private readonly Queue<(Guid SegmentId, RecordingResult Recording)> _transcriptionQueue = new();
private Task? _workerTask;

private void EnqueueTranscription(Guid segmentId, RecordingResult recording)
{
    _transcriptionQueue.Enqueue((segmentId, recording));
    _workerCts ??= new CancellationTokenSource();
    _workerTask ??= Task.Run(() => ProcessQueueAsync(_workerCts.Token));
}

private int GetPendingTranscriptionCount() =>
    _segments.Count(s => s.State == ContinuousSegmentState.Transcribing);

private async Task ProcessQueueAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        (Guid segmentId, RecordingResult recording)? job = null;
        lock (_transcriptionQueue)
        {
            if (_transcriptionQueue.Count > 0)
            {
                job = _transcriptionQueue.Dequeue();
            }
        }

        if (job is null)
        {
            await Task.Delay(20, cancellationToken);
            continue;
        }

        var result = await _pipeline.TranscribeAsync(job.Value.Recording, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = _segments.FindIndex(s => s.Id == job.Value.SegmentId);
            if (index >= 0)
            {
                _segments[index] = result.Succeeded
                    ? _segments[index] with { State = ContinuousSegmentState.Completed, Text = result.Text }
                    : _segments[index] with { State = ContinuousSegmentState.Failed, ErrorMessage = result.ErrorMessage };
            }
            Publish();
        }
        finally { _gate.Release(); }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(core): 连续听写 FIFO 转写队列与段状态回填。"
```

---

### Task 6: Esc / 结束录制 / 终止 / 关窗数据

**Files:**
- Modify: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs`
- Test: `tests/LocalAsrClient.Core.Tests/Dictation/ContinuousDictationSessionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Fact]
public async Task CancelCurrentSegmentAsync_RemovesWaitingInputWithoutQueueing()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.Session.CancelCurrentSegmentAsync(CancellationToken.None);

    Assert.False(fixture.LastSnapshot.IsRecordingActive);
    Assert.Empty(fixture.LastSnapshot.Segments);
    Assert.Equal(0, fixture.Backend.TranscribeCallCount);
}

[Fact]
public async Task TerminateAsync_ClearsSegmentsAndCancelsWorker()
{
    var fixture = new SessionFixture();
    fixture.Backend.Status = AsrBackendStatus.Ready;
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
    await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

    await fixture.Session.TerminateAsync(CancellationToken.None);

    Assert.Empty(fixture.LastSnapshot.Segments);
}

[Fact]
public void BuildHistoryText_UsesEditedCompletedSegmentsOnly()
{
    var fixture = new SessionFixture();
    fixture.Session.ApplySegmentTextForTest(Guid.NewGuid(), "用户改过的字");
    var text = fixture.Session.BuildHistoryText();
    Assert.Contains("用户改过的字", text);
}
```

`ApplySegmentTextForTest` 为 `internal` 测试钩子，或通过 public `UpdateSegmentText(Guid id, string text)` 供 VM 调用后再测。

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现取消/终止/历史文本**

```csharp
public async Task CancelCurrentSegmentAsync(CancellationToken cancellationToken)
{
    await _gate.WaitAsync(cancellationToken);
    try
    {
        if (!_isRecordingActive)
        {
            return;
        }

        await _recorder.StopAsync(cancellationToken);
        var waitingIndex = _segments.FindLastIndex(s => s.State == ContinuousSegmentState.WaitingInput);
        if (waitingIndex >= 0)
        {
            _segments.RemoveAt(waitingIndex);
        }
        _isRecordingActive = false;
        Publish();
    }
    finally { _gate.Release(); }
}

public async Task TerminateAsync(CancellationToken cancellationToken)
{
    _workerCts?.Cancel();
    if (_isRecordingActive)
    {
        try { await _recorder.StopAsync(cancellationToken); } catch { /* 忽略 */ }
    }
    _segments.Clear();
    lock (_transcriptionQueue) { _transcriptionQueue.Clear(); }
    _isRecordingActive = false;
    _workerCts = null;
    _workerTask = null;
    Publish();
}

public void UpdateSegmentText(Guid segmentId, string text)
{
    var index = _segments.FindIndex(s => s.Id == segmentId);
    if (index >= 0 && _segments[index].State == ContinuousSegmentState.Completed)
    {
        _segments[index] = _segments[index] with { Text = text };
        Publish();
    }
}

public string BuildHistoryText() => ContinuousDictationTextMerge.MergeCompletedSegments(_segments);
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(core): 连续听写取消、终止与关窗历史文本构建。"
```

---

### Task 7: F9 热键与 ContinuousDictationHotkey

**Files:**
- Modify: `src/LocalAsrClient.App/Hotkeys/Win32HotkeyNative.cs`
- Create: `src/LocalAsrClient.App/Hotkeys/ContinuousDictationHotkey.cs`

- [ ] **Step 1: 添加 VkF9 常量**

```csharp
// Win32HotkeyNative.cs 增加
public const int VkF9 = 0x78;
```

```csharp
// ContinuousDictationHotkey.cs
namespace LocalAsrClient.App.Hotkeys;

public static class ContinuousDictationHotkey
{
    public const int ToggleVirtualKey = Win32HotkeyNative.VkF9;
    public const string DisplayName = "F9";
}
```

- [ ] **Step 2: 构建确认**

```powershell
dotnet build src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Expected: 成功。

- [ ] **Step 3: Commit**

```powershell
git commit -m "feat(app): 添加 F9 连续听写热键常量。"
```

---

### Task 8: ContinuousDictationCoordinator 与 AppServices 热键路由

**Files:**
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationStrings.cs`
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationCoordinator.cs`
- Modify: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`
- Modify: `src/LocalAsrClient.App/App.xaml.cs`

- [ ] **Step 1: 中文文案常量**

```csharp
// ContinuousDictationStrings.cs
namespace LocalAsrClient.App.ContinuousDictation;

internal static class ContinuousDictationStrings
{
    public const string WindowTitlePrefix = "连续听写模式";
    public const string PlaceholderWaiting = "等待输入…";
    public const string PlaceholderTranscribing = "识别中…";
    public const string PlaceholderFailedPrefix = "识别失败";
    public const string ButtonTerminate = "终止";
    public const string ButtonEndRecording = "结束录制";
    public const string ButtonCopy = "复制";
    public const string ModelLoading = "模型加载中…";
    public const string QueueFullBanner = "已达识别上限（50 段），已停止录制";
}
```

- [ ] **Step 2: Coordinator 职责**

```csharp
// ContinuousDictationCoordinator.cs（节选）
public sealed class ContinuousDictationCoordinator : IDisposable
{
    public bool IsWindowOpen => _window?.IsLoaded == true;

    public void HandleF9()
    {
        if (_window is null)
        {
            OpenWindow(startRecording: true);
            return;
        }

        _ = RunSessionAsync(session => session.ToggleRecordingAsync(CancellationToken.None));
    }

    public void HandleRightControl()
    {
        if (!IsWindowOpen)
        {
            return;
        }

        _ = RunSessionAsync(session => session.CommitSegmentBoundaryAsync(CancellationToken.None));
    }

    public void HandleEscape()
    {
        if (!IsWindowOpen || !_session.IsRecordingActive)
        {
            return;
        }

        _ = RunSessionAsync(session => session.CancelCurrentSegmentAsync(CancellationToken.None));
    }

    private async Task OnWindowClosingAsync(bool isTerminate)
    {
        if (!isTerminate)
        {
            var historyText = _session.BuildHistoryText();
            if (!string.IsNullOrWhiteSpace(historyText))
            {
                await WriteHistoryAsync(historyText);
            }
        }
        else
        {
            await _session.TerminateAsync(CancellationToken.None);
        }
    }
}
```

- [ ] **Step 3: 修改 AppServices — 双录音器与路由**

```csharp
// 在 CreateAsync 中：
IAudioRecorder singleRecorder = testMode.Enabled ? new SimulatedAudioRecorder() : new NAudioMemoryRecorder();
IAudioRecorder continuousRecorder = testMode.Enabled ? new SimulatedAudioRecorder() : new NAudioMemoryRecorder();

var transcriptionPipeline = new TranscriptionPipeline(
    backend, settingsStore, new TranscriptionScriptPostProcessor(settingsStore), statsRepository, new SystemClock());

var continuousSession = new ContinuousDictationSession(continuousRecorder, transcriptionPipeline);
var continuousCoordinator = new ContinuousDictationCoordinator(
    continuousSession, historyRepository, settingsStore, new SystemClock());

var f9Listener = new GlobalHotkeyListener(ContinuousDictationHotkey.ToggleVirtualKey, diagnosticSink);

hotkeyListener.Triggered += () =>
{
    if (continuousCoordinator.IsWindowOpen)
    {
        continuousCoordinator.HandleRightControl();
        return;
    }
    // 现有单句 orchestrator 逻辑…
};

f9Listener.Triggered += () => continuousCoordinator.HandleF9();

escapeCancelListener 改为：
new EscapeCancelListener(() =>
    continuousCoordinator.IsWindowOpen && continuousSession.IsRecordingActive
        ? true
        : orchestrator.State == DictationState.Recording);
// CancelRequested 内先 continuousCoordinator.HandleEscape()，否则 orchestrator.CancelRecordingAsync
```

- [ ] **Step 4: App.xaml.cs 启动 F9 监听**

```csharp
_services.ContinuousDictationHotkeyListener.Start();
```

并在 `AppServices.DisposeAsync` 中 Dispose。

- [ ] **Step 5: 构建 + Commit**

```powershell
dotnet build LocalAsrClient.sln
git commit -m "feat(app): F9/右 Ctrl/Esc 路由至连续听写 Coordinator。"
```

---

### Task 9: ContinuousDictationWindow UI

**Files:**
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousSegmentViewModel.cs`
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationViewModel.cs`
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationWindow.xaml`
- Create: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationWindow.xaml.cs`

- [ ] **Step 1: SegmentViewModel**

```csharp
public sealed class ContinuousSegmentViewModel : INotifyPropertyChanged
{
    public Guid Id { get; }
    public ContinuousSegmentState State { get; private set; }
    public string Text { get; set; }
    public bool IsEditable => State == ContinuousSegmentState.Completed;
    public string Placeholder => State switch
    {
        ContinuousSegmentState.WaitingInput => ContinuousDictationStrings.PlaceholderWaiting,
        ContinuousSegmentState.Transcribing => ContinuousDictationStrings.PlaceholderTranscribing,
        ContinuousSegmentState.Failed => $"{ContinuousDictationStrings.PlaceholderFailedPrefix}：{ErrorMessage}",
        _ => string.Empty
    };
}
```

- [ ] **Step 2: Window XAML（节选）**

```xml
<Window Title="连续听写"
        Topmost="True"
        ShowInTaskbar="True"
        Width="520" Height="640"
        MinWidth="400" MinHeight="400">
  <DockPanel Margin="12">
    <TextBlock DockPanel.Dock="Top"
               Text="{Binding HeaderText}"
               FontSize="16" FontWeight="SemiBold" Margin="0,0,0,8"/>
    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
      <Button Content="终止" Command="{Binding TerminateCommand}" Margin="0,0,8,0"/>
      <Button Content="结束录制" Command="{Binding EndRecordingCommand}" Margin="0,0,8,0"/>
      <Button Content="复制" Command="{Binding CopyCommand}"/>
    </StackPanel>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
      <ItemsControl ItemsSource="{Binding Segments}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Margin="0,0,0,8" Padding="8" CornerRadius="6"
                    BorderBrush="#CBD5E1" BorderThickness="1">
              <TextBox Text="{Binding Text, UpdateSourceTrigger=PropertyChanged}"
                       IsReadOnly="{Binding IsEditable, Converter={StaticResource InverseBoolConverter}}"
                       Tag="{Binding Placeholder}"
                       Style="{StaticResource PlaceholderTextBoxStyle}"/>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </DockPanel>
</Window>
```

实现 `PlaceholderTextBoxStyle`（可用现有 Infrastructure 或轻量 Attached Property），保证 Placeholder 不进 `Text`。

- [ ] **Step 3: ViewModel 绑定 Session.Changed → 刷新 Segments；Completed 文本变更回调 `UpdateSegmentText`**

- [ ] **Step 4: 构建**

```powershell
dotnet build src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(app): 连续听写 WPF 窗口与段列表 UI（中文 Placeholder、只读规则）。"
```

---

### Task 10: 关窗写历史、复制、模型加载

**Files:**
- Modify: `src/LocalAsrClient.App/ContinuousDictation/ContinuousDictationCoordinator.cs`
- Modify: `src/LocalAsrClient.Core/Dictation/ContinuousDictationSession.cs`

- [ ] **Step 1: 关窗写一条 TextHistoryEntry**

```csharp
private async Task WriteHistoryAsync(string text, CancellationToken cancellationToken)
{
    var settings = await _settingsStore.LoadAsync(cancellationToken);
    if (settings.TranscriptRetentionPolicy == TranscriptRetentionPolicy.Disabled)
    {
        return;
    }

    var characterCount = TextMetrics.CountCharacters(text);
    var wordCount = TextMetrics.CountWords(text);
    await _historyRepository.AddAsync(
        new TextHistoryEntry(
            Guid.NewGuid(),
            _clock.Now,
            text,
            characterCount,
            wordCount,
            TimeSpan.Zero,
            TimeSpan.Zero,
            "continuous-dictation",
            null),
        cancellationToken);
    await _historyRepository.PruneAsync(_clock.Now, settings.TranscriptRetentionPolicy, cancellationToken);
}
```

- [ ] **Step 2: 复制按钮 — Clipboard.SetText(BuildHistoryText())**

- [ ] **Step 3: 首次 F9 若 ASR 未就绪 — Session 或 Coordinator 显示 Banner `模型加载中…`，EnsureReady 后再 StartRecording**

- [ ] **Step 4: 手动验证清单**

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

1. F9 打开窗口并开始首段（Placeholder「等待输入…」）。
2. 右 Ctrl 分段，上一段变「识别中…」，新段「等待输入…」。
3. 识别完成后段变为可编辑正文；识别中段 TextBox 只读。
4. F9 结束录制状态；Esc / 「结束录制」丢弃当前 Waiting 段。
5. 「终止」清空；关窗（X）写入历史（仅 Completed，换行合并）。
6. 连续窗口开着时右 Ctrl 不触发单句浮窗。

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(app): 连续听写关窗历史、复制与模型加载横幅。"
```

---

### Task 11: 文档更新

**Files:**
- Modify: `docs/domain.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: domain.md 增加连续听写规则（F9、右 Ctrl 双模式、50 段队列、关窗历史）**

- [ ] **Step 2: architecture.md 增加 ContinuousDictationSession / Coordinator / Window**

- [ ] **Step 3: 全量测试**

```powershell
dotnet test LocalAsrClient.sln --filter "Category!=UiE2E"
```

Expected: 全部 PASS。

- [ ] **Step 4: Commit**

```powershell
git commit -m "docs: 更新 domain 与 architecture 描述连续听写模式。"
```

---

## Spec Coverage Checklist

| 规格要求 | 对应 Task |
| --- | --- |
| F9 开窗口 / 追加录制 / 结束录制 | Task 3, 8 |
| 右 Ctrl 句子边界 | Task 4, 8 |
| Esc / 结束录制 | Task 6, 8 |
| 50 段队列上限 | Task 4 |
| 短段 <0.3s 移除 | Task 4 |
| 识别失败保留 Failed | Task 5 |
| 按段统计 | Task 2, 5 |
| 关窗合并历史 | Task 1, 6, 10 |
| 终止不写历史 | Task 6, 8 |
| 连续窗口开时单句不可用 | Task 8 |
| UI 中文 + Placeholder 只读 | Task 9 |
| 复制仅 Completed | Task 1, 10 |
| 窗口置顶、首次聚焦 | Task 9 |
| domain/architecture 文档 | Task 11 |

## 验收命令

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln --filter "Category!=UiE2E"
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

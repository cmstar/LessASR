# Windows ASR Client MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows WPF dictation client that stays in the tray, uses right Alt as a toggle key, talks to a managed local `whisper-server`, injects recognized text without using the clipboard by default, and stores stats/history according to the approved MVP spec.

**Architecture:** The app is split into a WPF shell and a testable core library. The core owns state machines, ASR orchestration, storage, and abstractions for platform features; the WPF app adapts those abstractions to Windows tray, overlay, hotkey, audio, and text injection.

**Tech Stack:** C#/.NET 8, WPF, xUnit, Microsoft.Data.Sqlite, NAudio, Win32 interop, `System.Windows.Forms.NotifyIcon`, `HttpClient`.

---

## Reference Spec

Implement against:

`docs/superpowers/specs/2026-06-07-windows-asr-client-mvp-design.md`

## File Structure

Create this solution structure:

```text
LocalAsrClient.sln
src/
  LocalAsrClient.Core/
    LocalAsrClient.Core.csproj
    Abstractions/
      IAsrBackend.cs
      IAudioRecorder.cs
      IClock.cs
      IHotkeyListener.cs
      ISettingsStore.cs
      IStatsRepository.cs
      ITextHistoryRepository.cs
      ITextInjector.cs
    Asr/
      AsrModels.cs
      ManagedWhisperServerBackend.cs
      WhisperServerClient.cs
      WhisperServerOptions.cs
      WhisperServerProcessManager.cs
    Dictation/
      DictationOrchestrator.cs
      DictationSessionModels.cs
      DictationState.cs
      NoOpTextPostProcessor.cs
    Persistence/
      AppSettings.cs
      RetentionPolicy.cs
      SqliteDatabase.cs
      SqliteSettingsStore.cs
      SqliteStatsRepository.cs
      SqliteTextHistoryRepository.cs
    Text/
      TextInjectionModels.cs
    Utilities/
      SystemClock.cs
      TextMetrics.cs
  LocalAsrClient.App/
    LocalAsrClient.App.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Bootstrap/
      AppServices.cs
    Audio/
      NAudioMemoryRecorder.cs
    Hotkeys/
      RightAltHotkeyListener.cs
      Win32HotkeyNative.cs
    TextInjection/
      SendInputTextInjector.cs
      Win32InputNative.cs
    Tray/
      TrayIconService.cs
    Overlay/
      DictationOverlayWindow.xaml
      DictationOverlayWindow.xaml.cs
      OverlayState.cs
      OverlayViewModel.cs
    ViewModels/
      MainViewModel.cs
      StatusViewModel.cs
      HistoryViewModel.cs
      StatsViewModel.cs
      ModelViewModel.cs
      SettingsViewModel.cs
      DebugViewModel.cs
tests/
  LocalAsrClient.Core.Tests/
    LocalAsrClient.Core.Tests.csproj
    Dictation/
      DictationOrchestratorTests.cs
    Persistence/
      SqliteRepositoryTests.cs
    Asr/
      WhisperServerBackendTests.cs
    Utilities/
      TextMetricsTests.cs
```

Keep WPF-specific code out of `LocalAsrClient.Core`. Core tests must run without a desktop session.

---

### Task 1: Scaffold Solution And Projects

**Files:**
- Create: `LocalAsrClient.sln`
- Create: `src/LocalAsrClient.Core/LocalAsrClient.Core.csproj`
- Create: `src/LocalAsrClient.App/LocalAsrClient.App.csproj`
- Create: `tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

Run:

```powershell
dotnet new sln -n LocalAsrClient
dotnet new classlib -n LocalAsrClient.Core -o src/LocalAsrClient.Core -f net8.0
dotnet new wpf -n LocalAsrClient.App -o src/LocalAsrClient.App -f net8.0-windows
dotnet new xunit -n LocalAsrClient.Core.Tests -o tests/LocalAsrClient.Core.Tests -f net8.0
dotnet sln LocalAsrClient.sln add src/LocalAsrClient.Core/LocalAsrClient.Core.csproj
dotnet sln LocalAsrClient.sln add src/LocalAsrClient.App/LocalAsrClient.App.csproj
dotnet sln LocalAsrClient.sln add tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj
dotnet add src/LocalAsrClient.App/LocalAsrClient.App.csproj reference src/LocalAsrClient.Core/LocalAsrClient.Core.csproj
dotnet add tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj reference src/LocalAsrClient.Core/LocalAsrClient.Core.csproj
```

Expected: each command exits with code 0.

- [ ] **Step 2: Add package dependencies**

Run:

```powershell
dotnet add src/LocalAsrClient.Core/LocalAsrClient.Core.csproj package Microsoft.Data.Sqlite
dotnet add src/LocalAsrClient.App/LocalAsrClient.App.csproj package NAudio
dotnet add tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj package Microsoft.NET.Test.Sdk
dotnet add tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj package xunit.runner.visualstudio
```

Expected: packages are restored successfully.

- [ ] **Step 3: Enable Windows Forms for tray support**

Modify `src/LocalAsrClient.App/LocalAsrClient.App.csproj`:

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

  <ItemGroup>
    <ProjectReference Include="..\LocalAsrClient.Core\LocalAsrClient.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.2.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Remove template placeholder class**

Delete `src/LocalAsrClient.Core/Class1.cs`.

- [ ] **Step 5: Verify build and tests**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: build succeeds and the generated xUnit template test passes.

- [ ] **Step 6: Commit**

```powershell
git add LocalAsrClient.sln src tests
git commit -m "chore: scaffold Windows ASR client solution"
```

---

### Task 2: Core Models, Settings, And Text Metrics

**Files:**
- Create: `src/LocalAsrClient.Core/Asr/AsrModels.cs`
- Create: `src/LocalAsrClient.Core/Dictation/DictationState.cs`
- Create: `src/LocalAsrClient.Core/Dictation/DictationSessionModels.cs`
- Create: `src/LocalAsrClient.Core/Persistence/AppSettings.cs`
- Create: `src/LocalAsrClient.Core/Persistence/RetentionPolicy.cs`
- Create: `src/LocalAsrClient.Core/Utilities/TextMetrics.cs`
- Create: `tests/LocalAsrClient.Core.Tests/Utilities/TextMetricsTests.cs`

- [ ] **Step 1: Write failing text metrics tests**

Create `tests/LocalAsrClient.Core.Tests/Utilities/TextMetricsTests.cs`:

```csharp
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Tests.Utilities;

public sealed class TextMetricsTests
{
    [Fact]
    public void CountCharacters_ExcludesWhitespace()
    {
        Assert.Equal(7, TextMetrics.CountCharacters("你好 world"));
    }

    [Fact]
    public void CountWords_CountsEnglishWordsAndChineseCharacters()
    {
        Assert.Equal(4, TextMetrics.CountWords("你好 world test"));
    }

    [Fact]
    public void CountWords_ReturnsZeroForBlankText()
    {
        Assert.Equal(0, TextMetrics.CountWords("   "));
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter TextMetricsTests
```

Expected: FAIL because `TextMetrics` does not exist.

- [ ] **Step 3: Add ASR models**

Create `src/LocalAsrClient.Core/Asr/AsrModels.cs`:

```csharp
namespace LocalAsrClient.Core.Asr;

public abstract record AudioInput(string Format, int SampleRate, int Channels);

public sealed record InMemoryAudioInput(
    byte[] Data,
    string Format,
    int SampleRate,
    int Channels) : AudioInput(Format, SampleRate, Channels);

public sealed record FileAudioInput(
    string Path,
    string Format,
    int SampleRate,
    int Channels) : AudioInput(Format, SampleRate, Channels);

public sealed record AsrRequest(
    AudioInput Audio,
    string? Language,
    string? Prompt,
    IReadOnlyDictionary<string, string> Options);

public sealed record AsrResult(
    string Text,
    TimeSpan? AudioDuration,
    TimeSpan? ProcessingDuration,
    double? Confidence);
```

- [ ] **Step 4: Add dictation state models**

Create `src/LocalAsrClient.Core/Dictation/DictationState.cs`:

```csharp
namespace LocalAsrClient.Core.Dictation;

public enum DictationState
{
    Idle,
    EnsuringModelReady,
    Ready,
    Recording,
    Transcribing,
    Injecting,
    ResultNeedsAction,
    Error
}
```

Create `src/LocalAsrClient.Core/Dictation/DictationSessionModels.cs`:

```csharp
namespace LocalAsrClient.Core.Dictation;

public sealed record DictationStatus(
    DictationState State,
    string Message,
    string? ResultText = null,
    string? ErrorMessage = null);

public sealed record RecordingResult(
    byte[] WavData,
    TimeSpan Duration,
    int SampleRate,
    int Channels);
```

- [ ] **Step 5: Add settings and retention models**

Create `src/LocalAsrClient.Core/Persistence/RetentionPolicy.cs`:

```csharp
namespace LocalAsrClient.Core.Persistence;

public enum TranscriptRetentionPolicy
{
    Disabled = 0,
    OneDay = 1,
    SevenDays = 7,
    OneMonth = 30
}

public static class TranscriptRetentionPolicyExtensions
{
    public static TimeSpan? ToTimeSpan(this TranscriptRetentionPolicy policy)
    {
        return policy switch
        {
            TranscriptRetentionPolicy.Disabled => null,
            TranscriptRetentionPolicy.OneDay => TimeSpan.FromDays(1),
            TranscriptRetentionPolicy.SevenDays => TimeSpan.FromDays(7),
            TranscriptRetentionPolicy.OneMonth => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(7)
        };
    }
}
```

Create `src/LocalAsrClient.Core/Persistence/AppSettings.cs`:

```csharp
namespace LocalAsrClient.Core.Persistence;

public sealed record AppSettings(
    string ModelPath,
    string WhisperServerPath,
    string DataDirectory,
    TranscriptRetentionPolicy TranscriptRetentionPolicy,
    bool StartModelOnAppStartup)
{
    public static AppSettings CreateDefault(string localAppData)
    {
        var dataDirectory = Path.Combine(localAppData, "LocalAsrClient", "data");
        return new AppSettings(
            ModelPath: string.Empty,
            WhisperServerPath: string.Empty,
            DataDirectory: dataDirectory,
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
            StartModelOnAppStartup: false);
    }
}
```

- [ ] **Step 6: Implement text metrics**

Create `src/LocalAsrClient.Core/Utilities/TextMetrics.cs`:

```csharp
using System.Globalization;

namespace LocalAsrClient.Core.Utilities;

public static class TextMetrics
{
    public static int CountCharacters(string text)
    {
        return text.Count(c => !char.IsWhiteSpace(c));
    }

    public static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var inLatinWord = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsCjk(rune))
            {
                if (inLatinWord)
                {
                    inLatinWord = false;
                }

                count++;
                continue;
            }

            if (IsLatinWordRune(rune))
            {
                if (!inLatinWord)
                {
                    count++;
                    inLatinWord = true;
                }

                continue;
            }

            inLatinWord = false;
        }

        return count;
    }

    private static bool IsLatinWordRune(Rune rune)
    {
        return Rune.IsLetterOrDigit(rune) || rune.Value == '_' || rune.Value == '-';
    }

    private static bool IsCjk(Rune rune)
    {
        return rune.Value is >= 0x4E00 and <= 0x9FFF;
    }
}
```

- [ ] **Step 7: Verify tests**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter TextMetricsTests
dotnet build LocalAsrClient.sln
```

Expected: tests pass and build succeeds.

- [ ] **Step 8: Commit**

```powershell
git add src/LocalAsrClient.Core tests/LocalAsrClient.Core.Tests
git commit -m "feat: add core dictation models"
```

---

### Task 3: SQLite Settings, Stats, And History

**Files:**
- Create: `src/LocalAsrClient.Core/Abstractions/IClock.cs`
- Create: `src/LocalAsrClient.Core/Abstractions/ISettingsStore.cs`
- Create: `src/LocalAsrClient.Core/Abstractions/IStatsRepository.cs`
- Create: `src/LocalAsrClient.Core/Abstractions/ITextHistoryRepository.cs`
- Create: `src/LocalAsrClient.Core/Persistence/SqliteDatabase.cs`
- Create: `src/LocalAsrClient.Core/Persistence/SqliteSettingsStore.cs`
- Create: `src/LocalAsrClient.Core/Persistence/SqliteStatsRepository.cs`
- Create: `src/LocalAsrClient.Core/Persistence/SqliteTextHistoryRepository.cs`
- Create: `src/LocalAsrClient.Core/Utilities/SystemClock.cs`
- Create: `tests/LocalAsrClient.Core.Tests/Persistence/SqliteRepositoryTests.cs`

- [ ] **Step 1: Write failing repository tests**

Create `tests/LocalAsrClient.Core.Tests/Persistence/SqliteRepositoryTests.cs`:

```csharp
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Persistence;

public sealed class SqliteRepositoryTests
{
    [Fact]
    public async Task SettingsStore_RoundTripsSettings()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var settings = new AppSettings(
            ModelPath: @"D:\models\ggml-large-v3-turbo-q5_0.bin",
            WhisperServerPath: @"D:\tools\whisper-server.exe",
            DataDirectory: @"D:\asr-data",
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.OneMonth,
            StartModelOnAppStartup: true);

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task StatsRepository_AccumulatesDailyStats()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteStatsRepository(database);

        var date = new DateOnly(2026, 6, 7);
        await repository.RecordAsync(new DailyStatsDelta(
            Date: date,
            Succeeded: true,
            RecordingDuration: TimeSpan.FromSeconds(3),
            ProcessingDuration: TimeSpan.FromSeconds(2),
            CharacterCount: 12,
            WordCount: 5), CancellationToken.None);

        await repository.RecordAsync(new DailyStatsDelta(
            Date: date,
            Succeeded: false,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.Zero,
            CharacterCount: 0,
            WordCount: 0), CancellationToken.None);

        var stats = await repository.GetRangeAsync(date, date, CancellationToken.None);

        var day = Assert.Single(stats);
        Assert.Equal(2, day.InputCount);
        Assert.Equal(1, day.SuccessCount);
        Assert.Equal(1, day.FailedCount);
        Assert.Equal(4, day.RecordingSeconds);
        Assert.Equal(2, day.ProcessingSeconds);
        Assert.Equal(12, day.CharacterCount);
        Assert.Equal(5, day.WordCount);
    }

    [Fact]
    public async Task TextHistoryRepository_RespectsRetentionAndDisabledPolicy()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteTextHistoryRepository(database);

        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

        await repository.AddAsync(new TextHistoryEntry(
            Id: Guid.NewGuid(),
            CreatedAt: now.AddDays(-8),
            Text: "旧记录",
            CharacterCount: 3,
            WordCount: 3,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.FromSeconds(1),
            BackendId: "whisper-server",
            ModelId: "large-v3-turbo"), CancellationToken.None);

        await repository.AddAsync(new TextHistoryEntry(
            Id: Guid.NewGuid(),
            CreatedAt: now,
            Text: "新记录",
            CharacterCount: 3,
            WordCount: 3,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.FromSeconds(1),
            BackendId: "whisper-server",
            ModelId: "large-v3-turbo"), CancellationToken.None);

        await repository.PruneAsync(now, TranscriptRetentionPolicy.SevenDays, CancellationToken.None);
        var retained = await repository.GetRecentAsync(10, CancellationToken.None);

        var entry = Assert.Single(retained);
        Assert.Equal("新记录", entry.Text);

        await repository.PruneAsync(now, TranscriptRetentionPolicy.Disabled, CancellationToken.None);
        Assert.Empty(await repository.GetRecentAsync(10, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter SqliteRepositoryTests
```

Expected: FAIL because repository types do not exist.

- [ ] **Step 3: Add abstractions and records**

Create `src/LocalAsrClient.Core/Abstractions/ISettingsStore.cs`:

```csharp
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
```

Create `src/LocalAsrClient.Core/Abstractions/IStatsRepository.cs`:

```csharp
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface IStatsRepository
{
    Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task PruneAsync(DateOnly today, CancellationToken cancellationToken);
}
```

Create `src/LocalAsrClient.Core/Abstractions/ITextHistoryRepository.cs`:

```csharp
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface ITextHistoryRepository
{
    Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken);
}
```

Create `src/LocalAsrClient.Core/Abstractions/IClock.cs`:

```csharp
namespace LocalAsrClient.Core.Abstractions;

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}
```

Create `src/LocalAsrClient.Core/Utilities/SystemClock.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Utilities;

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
```

- [ ] **Step 4: Add SQLite persistence models and database**

Create `src/LocalAsrClient.Core/Persistence/SqliteDatabase.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public SqliteConnection Connection => _connection;

    public static async Task<SqliteDatabase> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        var database = new SqliteDatabase(connection);
        await database.InitializeAsync(cancellationToken);
        return database;
    }

    public static async Task<SqliteDatabase> CreateInMemoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var database = new SqliteDatabase(connection);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS daily_stats (
                date TEXT PRIMARY KEY,
                input_count INTEGER NOT NULL,
                success_count INTEGER NOT NULL,
                failed_count INTEGER NOT NULL,
                recording_seconds REAL NOT NULL,
                processing_seconds REAL NOT NULL,
                character_count INTEGER NOT NULL,
                word_count INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transcript_history (
                id TEXT PRIMARY KEY,
                created_at TEXT NOT NULL,
                text TEXT NOT NULL,
                character_count INTEGER NOT NULL,
                word_count INTEGER NOT NULL,
                recording_seconds REAL NOT NULL,
                processing_seconds REAL NOT NULL,
                backend_id TEXT NOT NULL,
                model_id TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}

public sealed record DailyStatsDelta(
    DateOnly Date,
    bool Succeeded,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration,
    int CharacterCount,
    int WordCount);

public sealed record DailyStatsSnapshot(
    DateOnly Date,
    int InputCount,
    int SuccessCount,
    int FailedCount,
    double RecordingSeconds,
    double ProcessingSeconds,
    int CharacterCount,
    int WordCount);

public sealed record TextHistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Text,
    int CharacterCount,
    int WordCount,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration,
    string BackendId,
    string ModelId);
```

- [ ] **Step 5: Implement repositories**

Create `src/LocalAsrClient.Core/Persistence/SqliteSettingsStore.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteDatabase _database;

    public SqliteSettingsStore(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        var defaults = AppSettings.CreateDefault(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return defaults with
        {
            ModelPath = values.GetValueOrDefault("ModelPath", defaults.ModelPath),
            WhisperServerPath = values.GetValueOrDefault("WhisperServerPath", defaults.WhisperServerPath),
            DataDirectory = values.GetValueOrDefault("DataDirectory", defaults.DataDirectory),
            TranscriptRetentionPolicy = Enum.TryParse<TranscriptRetentionPolicy>(
                values.GetValueOrDefault("TranscriptRetentionPolicy"),
                out var policy) ? policy : defaults.TranscriptRetentionPolicy,
            StartModelOnAppStartup = bool.TryParse(values.GetValueOrDefault("StartModelOnAppStartup"), out var start) && start
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["ModelPath"] = settings.ModelPath,
            ["WhisperServerPath"] = settings.WhisperServerPath,
            ["DataDirectory"] = settings.DataDirectory,
            ["TranscriptRetentionPolicy"] = settings.TranscriptRetentionPolicy.ToString(),
            ["StartModelOnAppStartup"] = settings.StartModelOnAppStartup.ToString()
        };

        foreach (var pair in values)
        {
            var command = _database.Connection.CreateCommand();
            command.CommandText = """
                INSERT INTO settings(key, value)
                VALUES($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            command.Parameters.AddWithValue("$key", pair.Key);
            command.Parameters.AddWithValue("$value", pair.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
```

Create `src/LocalAsrClient.Core/Persistence/SqliteStatsRepository.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteStatsRepository : IStatsRepository
{
    private readonly SqliteDatabase _database;

    public SqliteStatsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_stats(
                date, input_count, success_count, failed_count, recording_seconds,
                processing_seconds, character_count, word_count)
            VALUES($date, 1, $success, $failed, $recording, $processing, $characters, $words)
            ON CONFLICT(date) DO UPDATE SET
                input_count = input_count + 1,
                success_count = success_count + $success,
                failed_count = failed_count + $failed,
                recording_seconds = recording_seconds + $recording,
                processing_seconds = processing_seconds + $processing,
                character_count = character_count + $characters,
                word_count = word_count + $words
            """;
        command.Parameters.AddWithValue("$date", delta.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$success", delta.Succeeded ? 1 : 0);
        command.Parameters.AddWithValue("$failed", delta.Succeeded ? 0 : 1);
        command.Parameters.AddWithValue("$recording", delta.RecordingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$processing", delta.ProcessingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$characters", delta.CharacterCount);
        command.Parameters.AddWithValue("$words", delta.WordCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT date, input_count, success_count, failed_count, recording_seconds,
                   processing_seconds, character_count, word_count
            FROM daily_stats
            WHERE date >= $start AND date <= $end
            ORDER BY date
            """;
        command.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var result = new List<DailyStatsSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DailyStatsSnapshot(
                DateOnly.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }

        return result;
    }

    public async Task PruneAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var cutoff = today.AddDays(-62);
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM daily_stats WHERE date < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("yyyy-MM-dd"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

Create `src/LocalAsrClient.Core/Persistence/SqliteTextHistoryRepository.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteTextHistoryRepository : ITextHistoryRepository
{
    private readonly SqliteDatabase _database;

    public SqliteTextHistoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcript_history(
                id, created_at, text, character_count, word_count,
                recording_seconds, processing_seconds, backend_id, model_id)
            VALUES($id, $created_at, $text, $characters, $words, $recording, $processing, $backend, $model)
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$text", entry.Text);
        command.Parameters.AddWithValue("$characters", entry.CharacterCount);
        command.Parameters.AddWithValue("$words", entry.WordCount);
        command.Parameters.AddWithValue("$recording", entry.RecordingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$processing", entry.ProcessingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$backend", entry.BackendId);
        command.Parameters.AddWithValue("$model", entry.ModelId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, text, character_count, word_count,
                   recording_seconds, processing_seconds, backend_id, model_id
            FROM transcript_history
            ORDER BY created_at DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<TextHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TextHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                TimeSpan.FromSeconds(reader.GetDouble(5)),
                TimeSpan.FromSeconds(reader.GetDouble(6)),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return result;
    }

    public async Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken)
    {
        if (policy == TranscriptRetentionPolicy.Disabled)
        {
            var deleteAll = _database.Connection.CreateCommand();
            deleteAll.CommandText = "DELETE FROM transcript_history";
            await deleteAll.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var retention = policy.ToTimeSpan() ?? TimeSpan.Zero;
        var cutoff = now.Subtract(retention).ToUniversalTime().ToString("O");
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM transcript_history WHERE created_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

- [ ] **Step 6: Verify tests**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter SqliteRepositoryTests
dotnet test LocalAsrClient.sln
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/LocalAsrClient.Core tests/LocalAsrClient.Core.Tests
git commit -m "feat: add SQLite persistence"
```

---

### Task 4: Managed Whisper Server Backend

**Files:**
- Create: `src/LocalAsrClient.Core/Abstractions/IAsrBackend.cs`
- Create: `src/LocalAsrClient.Core/Asr/WhisperServerOptions.cs`
- Create: `src/LocalAsrClient.Core/Asr/WhisperServerClient.cs`
- Create: `src/LocalAsrClient.Core/Asr/WhisperServerProcessManager.cs`
- Create: `src/LocalAsrClient.Core/Asr/ManagedWhisperServerBackend.cs`
- Create: `tests/LocalAsrClient.Core.Tests/Asr/WhisperServerBackendTests.cs`

- [ ] **Step 1: Write failing ASR backend tests**

Create `tests/LocalAsrClient.Core.Tests/Asr/WhisperServerBackendTests.cs`:

```csharp
using System.Net;
using System.Text;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerBackendTests
{
    [Fact]
    public async Task Client_ParsesOpenAiCompatibleTextResponse()
    {
        var handler = new StubHttpHandler("""{"text":"你好，世界"}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        var result = await client.TranscribeAsync(new InMemoryAudioInput(
            Data: Encoding.UTF8.GetBytes("fake wav"),
            Format: "wav",
            SampleRate: 16000,
            Channels: 1), CancellationToken.None);

        Assert.Equal("你好，世界", result.Text);
        Assert.Equal("/v1/audio/transcriptions", handler.LastRequestPath);
    }

    [Fact]
    public async Task Backend_EnsuresServerBeforeTranscription()
    {
        var manager = new StubWhisperServerManager();
        var client = new StubWhisperServerClient("测试文本");
        var backend = new ManagedWhisperServerBackend(manager, client);

        var result = await backend.TranscribeAsync(new AsrRequest(
            new InMemoryAudioInput(Array.Empty<byte>(), "wav", 16000, 1),
            Language: "zh",
            Prompt: null,
            Options: new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(manager.Started);
        Assert.Equal("测试文本", result.Text);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHttpHandler(string body)
        {
            _body = body;
        }

        public string? LastRequestPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.AbsolutePath;
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("form-data", request.Content.Headers.ContentType!.MediaType);
            Assert.NotEmpty(content);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubWhisperServerManager : IWhisperServerManager
    {
        public bool Started { get; private set; }
        public WhisperServerStatus Status { get; private set; } = WhisperServerStatus.Stopped;
        public Uri BaseUri => new("http://127.0.0.1:8080");

        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            Started = true;
            Status = WhisperServerStatus.Ready;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Status = WhisperServerStatus.Stopped;
            return Task.CompletedTask;
        }
    }

    private sealed class StubWhisperServerClient : IWhisperServerClient
    {
        private readonly string _text;

        public StubWhisperServerClient(string text)
        {
            _text = text;
        }

        public Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsrResult(_text, null, TimeSpan.FromMilliseconds(50), null));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter WhisperServerBackendTests
```

Expected: FAIL because ASR backend types do not exist.

- [ ] **Step 3: Add ASR abstractions and options**

Create `src/LocalAsrClient.Core/Abstractions/IAsrBackend.cs`:

```csharp
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Abstractions;

public interface IAsrBackend
{
    string Name { get; }
    AsrBackendStatus Status { get; }
    Task EnsureReadyAsync(CancellationToken cancellationToken);
    Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken);
}
```

Create `src/LocalAsrClient.Core/Asr/WhisperServerOptions.cs`:

```csharp
namespace LocalAsrClient.Core.Asr;

public sealed record WhisperServerOptions(
    string ServerExecutablePath,
    string ModelPath,
    string Host,
    int Port)
{
    public Uri BaseUri => new($"http://{Host}:{Port}");
}

public enum WhisperServerStatus
{
    Stopped,
    Starting,
    Ready,
    Transcribing,
    Failed
}

public enum AsrBackendStatus
{
    Stopped,
    Starting,
    Ready,
    Transcribing,
    Failed
}
```

- [ ] **Step 4: Implement HTTP client**

Create `src/LocalAsrClient.Core/Asr/WhisperServerClient.cs`:

```csharp
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerClient
{
    Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, CancellationToken cancellationToken);
}

public sealed class WhisperServerClient : IWhisperServerClient
{
    private readonly HttpClient _httpClient;

    public WhisperServerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.Data);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "dictation.wav");
        content.Add(new StringContent("json"), "response_format");

        using var response = await _httpClient.PostAsync("/v1/audio/transcriptions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        stopwatch.Stop();
        return new AsrResult(text, null, stopwatch.Elapsed, null);
    }
}
```

- [ ] **Step 5: Implement process manager**

Create `src/LocalAsrClient.Core/Asr/WhisperServerProcessManager.cs`:

```csharp
using System.Diagnostics;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerManager
{
    WhisperServerStatus Status { get; }
    Uri BaseUri { get; }
    Task EnsureStartedAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class WhisperServerProcessManager : IWhisperServerManager
{
    private readonly WhisperServerOptions _options;
    private Process? _process;

    public WhisperServerProcessManager(WhisperServerOptions options)
    {
        _options = options;
    }

    public WhisperServerStatus Status { get; private set; } = WhisperServerStatus.Stopped;
    public Uri BaseUri => _options.BaseUri;

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Status == WhisperServerStatus.Ready && _process is { HasExited: false })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ServerExecutablePath) || !File.Exists(_options.ServerExecutablePath))
        {
            Status = WhisperServerStatus.Failed;
            throw new FileNotFoundException("未找到 whisper-server 可执行文件。", _options.ServerExecutablePath);
        }

        if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
        {
            Status = WhisperServerStatus.Failed;
            throw new FileNotFoundException("未找到 Whisper 模型文件。", _options.ModelPath);
        }

        Status = WhisperServerStatus.Starting;
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ServerExecutablePath,
            Arguments = $"--host {_options.Host} --port {_options.Port} -m \"{_options.ModelPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 whisper-server。");

        await WaitUntilReadyAsync(cancellationToken);
        Status = WhisperServerStatus.Ready;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }

        _process = null;
        Status = WhisperServerStatus.Stopped;
        return Task.CompletedTask;
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = _options.BaseUri, Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
            {
                Status = WhisperServerStatus.Failed;
                throw new InvalidOperationException("whisper-server 已退出。");
            }

            try
            {
                using var response = await httpClient.GetAsync("/", cancellationToken);
                if ((int)response.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(500, cancellationToken);
        }

        Status = WhisperServerStatus.Failed;
        throw new TimeoutException("等待 whisper-server 启动超时。");
    }
}
```

- [ ] **Step 6: Implement backend wrapper**

Create `src/LocalAsrClient.Core/Asr/ManagedWhisperServerBackend.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Asr;

public sealed class ManagedWhisperServerBackend : IAsrBackend
{
    private readonly IWhisperServerManager _manager;
    private readonly IWhisperServerClient _client;

    public ManagedWhisperServerBackend(IWhisperServerManager manager, IWhisperServerClient client)
    {
        _manager = manager;
        _client = client;
    }

    public string Name => "Whisper Server";
    public AsrBackendStatus Status => _manager.Status switch
    {
        WhisperServerStatus.Stopped => AsrBackendStatus.Stopped,
        WhisperServerStatus.Starting => AsrBackendStatus.Starting,
        WhisperServerStatus.Ready => AsrBackendStatus.Ready,
        WhisperServerStatus.Transcribing => AsrBackendStatus.Transcribing,
        WhisperServerStatus.Failed => AsrBackendStatus.Failed,
        _ => AsrBackendStatus.Failed
    };

    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        return _manager.EnsureStartedAsync(cancellationToken);
    }

    public async Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        if (request.Audio is not InMemoryAudioInput audio)
        {
            throw new NotSupportedException("MVP 仅支持内存音频输入。");
        }

        await EnsureReadyAsync(cancellationToken);
        return await _client.TranscribeAsync(audio, cancellationToken);
    }
}
```

- [ ] **Step 7: Verify tests**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter WhisperServerBackendTests
dotnet test LocalAsrClient.sln
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/LocalAsrClient.Core tests/LocalAsrClient.Core.Tests
git commit -m "feat: add managed Whisper server backend"
```

---

### Task 5: Dictation Orchestrator

**Files:**
- Create: `src/LocalAsrClient.Core/Abstractions/IAudioRecorder.cs`
- Create: `src/LocalAsrClient.Core/Abstractions/ITextInjector.cs`
- Create: `src/LocalAsrClient.Core/Dictation/NoOpTextPostProcessor.cs`
- Create: `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs`
- Create: `src/LocalAsrClient.Core/Text/TextInjectionModels.cs`
- Create: `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs`

- [ ] **Step 1: Write failing orchestrator tests**

Create `tests/LocalAsrClient.Core.Tests/Dictation/DictationOrchestratorTests.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class DictationOrchestratorTests
{
    [Fact]
    public async Task ToggleAsync_FirstPressEnsuresModel_WhenModelIsStopped()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Stopped;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.True(fixture.Backend.EnsureReadyCalled);
        Assert.Equal(DictationState.Ready, fixture.LastStatus.State);
        Assert.False(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_FirstPressStartsRecording_WhenModelReady()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
        Assert.True(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_SecondPressTranscribesAndInjectsText()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal("测试文本", fixture.Injector.Text);
        Assert.Equal(DictationState.Idle, fixture.LastStatus.State);
        Assert.Equal(1, fixture.Stats.Recorded.Count);
        Assert.Single(fixture.History.Entries);
    }

    [Fact]
    public async Task ToggleAsync_WhenInjectionFailsLeavesResultNeedsAction()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "没有输入框");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
        Assert.Equal("测试文本", fixture.LastStatus.ResultText);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Recorder = new StubRecorder();
            Backend = new StubBackend();
            Injector = new StubInjector();
            Stats = new StubStatsRepository();
            History = new StubHistoryRepository();
            Settings = new StubSettingsStore();
            Clock = new StubClock();
            Orchestrator = new DictationOrchestrator(Recorder, Backend, Injector, Stats, History, Settings, Clock);
            Orchestrator.StatusChanged += status => LastStatus = status;
        }

        public StubRecorder Recorder { get; }
        public StubBackend Backend { get; }
        public StubInjector Injector { get; }
        public StubStatsRepository Stats { get; }
        public StubHistoryRepository History { get; }
        public StubSettingsStore Settings { get; }
        public StubClock Clock { get; }
        public DictationOrchestrator Orchestrator { get; }
        public DictationStatus LastStatus { get; private set; } = new(DictationState.Idle, "空闲");
    }

    private sealed class StubRecorder : IAudioRecorder
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RecordingResult(new byte[] { 1, 2, 3 }, TimeSpan.FromSeconds(2), 16000, 1));
        }
    }

    private sealed class StubBackend : IAsrBackend
    {
        public string Name => "Whisper Server";
        public AsrBackendStatus Status { get; set; } = AsrBackendStatus.Ready;
        public bool EnsureReadyCalled { get; private set; }

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureReadyCalled = true;
            Status = AsrBackendStatus.Ready;
            return Task.CompletedTask;
        }

        public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsrResult("测试文本", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null));
        }
    }

    private sealed class StubInjector : ITextInjector
    {
        public TextInjectionResult Result { get; set; } = new(TextInjectionStatus.Success, null);
        public string? Text { get; private set; }

        public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubStatsRepository : IStatsRepository
    {
        public List<DailyStatsDelta> Recorded { get; } = new();

        public Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken)
        {
            Recorded.Add(delta);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DailyStatsSnapshot>>(Array.Empty<DailyStatsSnapshot>());
        }

        public Task PruneAsync(DateOnly today, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubHistoryRepository : ITextHistoryRepository
    {
        public List<TextHistoryEntry> Entries { get; } = new();

        public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<TextHistoryEntry>>(Entries);
        }

        public Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        private readonly AppSettings _settings = new(
            ModelPath: "model.bin",
            WhisperServerPath: "whisper-server.exe",
            DataDirectory: "data",
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
            StartModelOnAppStartup: false);

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset Now => new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 6, 7);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter DictationOrchestratorTests
```

Expected: FAIL because dictation abstractions and orchestrator do not exist.

- [ ] **Step 3: Add recorder and injector abstractions**

Create `src/LocalAsrClient.Core/Abstractions/IAudioRecorder.cs`:

```csharp
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Abstractions;

public interface IAudioRecorder
{
    Task StartAsync(CancellationToken cancellationToken);
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken);
}
```

Create `src/LocalAsrClient.Core/Text/TextInjectionModels.cs`:

```csharp
namespace LocalAsrClient.Core.Text;

public enum TextInjectionStatus
{
    Success,
    NoEditableTarget,
    PermissionDenied,
    UnsupportedTarget,
    Failed
}

public sealed record TextInjectionResult(TextInjectionStatus Status, string? Message)
{
    public bool Succeeded => Status == TextInjectionStatus.Success;
}
```

Create `src/LocalAsrClient.Core/Abstractions/ITextInjector.cs`:

```csharp
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Abstractions;

public interface ITextInjector
{
    Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add no-op post processor**

Create `src/LocalAsrClient.Core/Dictation/NoOpTextPostProcessor.cs`:

```csharp
namespace LocalAsrClient.Core.Dictation;

public interface ITextPostProcessor
{
    Task<string> ProcessAsync(string text, CancellationToken cancellationToken);
}

public sealed class NoOpTextPostProcessor : ITextPostProcessor
{
    public Task<string> ProcessAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(text);
    }
}
```

- [ ] **Step 5: Implement orchestrator**

Create `src/LocalAsrClient.Core/Dictation/DictationOrchestrator.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Dictation;

public sealed class DictationOrchestrator
{
    private readonly IAudioRecorder _recorder;
    private readonly IAsrBackend _asrBackend;
    private readonly ITextInjector _textInjector;
    private readonly IStatsRepository _statsRepository;
    private readonly ITextHistoryRepository _historyRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IClock _clock;
    private readonly ITextPostProcessor _postProcessor;
    private DictationState _state = DictationState.Idle;

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock)
        : this(recorder, asrBackend, textInjector, statsRepository, historyRepository, settingsStore, clock, new NoOpTextPostProcessor())
    {
    }

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock,
        ITextPostProcessor postProcessor)
    {
        _recorder = recorder;
        _asrBackend = asrBackend;
        _textInjector = textInjector;
        _statsRepository = statsRepository;
        _historyRepository = historyRepository;
        _settingsStore = settingsStore;
        _clock = clock;
        _postProcessor = postProcessor;
    }

    public event Action<DictationStatus>? StatusChanged;

    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (_state == DictationState.Idle || _state == DictationState.Ready)
        {
            await StartRecordingAsync(cancellationToken);
            return;
        }

        if (_state == DictationState.Recording)
        {
            await StopAndTranscribeAsync(cancellationToken);
        }
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        if (_asrBackend.Status != AsrBackendStatus.Ready)
        {
            _state = DictationState.EnsuringModelReady;
            Publish("模型加载中");
            await _asrBackend.EnsureReadyAsync(cancellationToken);
            _state = DictationState.Ready;
            Publish("可录音");
            return;
        }

        _state = DictationState.Recording;
        Publish("正在聆听");
        await _recorder.StartAsync(cancellationToken);
    }

    private async Task StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _state = DictationState.Transcribing;
            Publish("识别中");
            var recording = await _recorder.StopAsync(cancellationToken);
            var asrResult = await _asrBackend.TranscribeAsync(new AsrRequest(
                new InMemoryAudioInput(recording.WavData, "wav", recording.SampleRate, recording.Channels),
                Language: "zh",
                Prompt: null,
                Options: new Dictionary<string, string>()), cancellationToken);

            var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);

            _state = DictationState.Injecting;
            Publish("正在输入", finalText);
            var injection = await _textInjector.TryInjectAsync(finalText, cancellationToken);

            await PersistResultAsync(finalText, recording.Duration, asrResult.ProcessingDuration ?? TimeSpan.Zero, injection.Succeeded, cancellationToken);

            if (injection.Succeeded)
            {
                _state = DictationState.Idle;
                Publish("已输入");
                return;
            }

            _state = DictationState.ResultNeedsAction;
            Publish("未找到可输入位置", finalText);
        }
        catch (Exception ex)
        {
            _state = DictationState.Error;
            Publish("输入失败", ErrorMessage: ex.Message);
        }
    }

    private async Task PersistResultAsync(
        string text,
        TimeSpan recordingDuration,
        TimeSpan processingDuration,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var characterCount = TextMetrics.CountCharacters(text);
        var wordCount = TextMetrics.CountWords(text);

        await _statsRepository.RecordAsync(new DailyStatsDelta(
            _clock.Today,
            succeeded,
            recordingDuration,
            processingDuration,
            characterCount,
            wordCount), cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.TranscriptRetentionPolicy != TranscriptRetentionPolicy.Disabled)
        {
            await _historyRepository.AddAsync(new TextHistoryEntry(
                Guid.NewGuid(),
                _clock.Now,
                text,
                characterCount,
                wordCount,
                recordingDuration,
                processingDuration,
                "whisper-server",
                Path.GetFileNameWithoutExtension(settings.ModelPath)), cancellationToken);
            await _historyRepository.PruneAsync(_clock.Now, settings.TranscriptRetentionPolicy, cancellationToken);
        }

        await _statsRepository.PruneAsync(_clock.Today, cancellationToken);
    }

    private void Publish(string message, string? resultText = null, string? ErrorMessage = null)
    {
        StatusChanged?.Invoke(new DictationStatus(_state, message, resultText, ErrorMessage));
    }
}
```

- [ ] **Step 6: Verify tests**

Run:

```powershell
dotnet test tests/LocalAsrClient.Core.Tests/LocalAsrClient.Core.Tests.csproj --filter DictationOrchestratorTests
dotnet test LocalAsrClient.sln
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/LocalAsrClient.Core tests/LocalAsrClient.Core.Tests
git commit -m "feat: add dictation orchestrator"
```

---

### Task 6: WPF Shell, Tray, And Main Window Tabs

**Files:**
- Modify: `src/LocalAsrClient.App/App.xaml`
- Modify: `src/LocalAsrClient.App/App.xaml.cs`
- Modify: `src/LocalAsrClient.App/MainWindow.xaml`
- Modify: `src/LocalAsrClient.App/MainWindow.xaml.cs`
- Create: `src/LocalAsrClient.App/Tray/TrayIconService.cs`
- Create: `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/StatusViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/HistoryViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/StatsViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/ModelViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/SettingsViewModel.cs`
- Create: `src/LocalAsrClient.App/ViewModels/DebugViewModel.cs`

- [ ] **Step 1: Add tab view models**

Create `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`:

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class MainViewModel
{
    public StatusViewModel Status { get; } = new();
    public HistoryViewModel History { get; } = new();
    public StatsViewModel Stats { get; } = new();
    public ModelViewModel Model { get; } = new();
    public SettingsViewModel Settings { get; } = new();
    public DebugViewModel Debug { get; } = new();
}
```

Create `src/LocalAsrClient.App/ViewModels/StatusViewModel.cs`:

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class StatusViewModel
{
    public string CurrentState { get; set; } = "空闲";
    public string CurrentModel { get; set; } = "未选择模型";
    public string ServiceState { get; set; } = "未启动";
    public string Hotkey { get; set; } = "右 Alt";
    public string LastResult { get; set; } = "";
}
```

Create the remaining view model files with these minimal classes:

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class HistoryViewModel
{
}
```

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class StatsViewModel
{
}
```

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class ModelViewModel
{
}
```

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel
{
}
```

```csharp
namespace LocalAsrClient.App.ViewModels;

public sealed class DebugViewModel
{
}
```

- [ ] **Step 2: Build main window tabs in Chinese**

Replace `src/LocalAsrClient.App/MainWindow.xaml`:

```xml
<Window x:Class="LocalAsrClient.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="本地语音输入"
        Width="760"
        Height="520"
        MinWidth="640"
        MinHeight="420"
        WindowStartupLocation="CenterScreen">
    <Grid Margin="12">
        <TabControl>
            <TabItem Header="状态">
                <StackPanel Margin="16" Width="420" HorizontalAlignment="Left">
                    <TextBlock Text="当前状态" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,12"/>
                    <TextBlock Text="{Binding Status.CurrentState}" Margin="0,0,0,8"/>
                    <TextBlock Text="{Binding Status.CurrentModel}" Margin="0,0,0,8"/>
                    <TextBlock Text="{Binding Status.ServiceState}" Margin="0,0,0,8"/>
                    <TextBlock Text="{Binding Status.Hotkey}" Margin="0,0,0,8"/>
                    <TextBlock Text="{Binding Status.LastResult}" TextWrapping="Wrap"/>
                </StackPanel>
            </TabItem>
            <TabItem Header="历史">
                <TextBlock Text="暂无历史记录" Margin="16"/>
            </TabItem>
            <TabItem Header="统计">
                <TextBlock Text="暂无统计数据" Margin="16"/>
            </TabItem>
            <TabItem Header="模型">
                <TextBlock Text="未启动模型服务" Margin="16"/>
            </TabItem>
            <TabItem Header="设置">
                <TextBlock Text="请配置模型路径和数据路径" Margin="16"/>
            </TabItem>
            <TabItem Header="Debug">
                <TextBlock Text="请选择调试操作" Margin="16"/>
            </TabItem>
        </TabControl>
    </Grid>
</Window>
```

- [ ] **Step 3: Wire DataContext and hide-on-close behavior**

Replace `src/LocalAsrClient.App/MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Windows;
using LocalAsrClient.App.ViewModels;

namespace LocalAsrClient.App;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }
}
```

- [ ] **Step 4: Add tray icon service**

Create `src/LocalAsrClient.App/Tray/TrayIconService.cs`:

```csharp
using System.Windows;
using Forms = System.Windows.Forms;

namespace LocalAsrClient.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(MainWindow window)
    {
        _window = window;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "本地语音输入",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开窗口", null, (_, _) => ShowWindow());
        menu.Items.Add("退出程序", null, (_, _) => ExitApplication());
        return menu;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.Activate();
    }

    private void ExitApplication()
    {
        _window.AllowClose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
```

- [ ] **Step 5: Wire app startup and shutdown**

Replace `src/LocalAsrClient.App/App.xaml`:

```xml
<Application x:Class="LocalAsrClient.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Replace `src/LocalAsrClient.App/App.xaml.cs`:

```csharp
using System.Windows;
using LocalAsrClient.App.Tray;

namespace LocalAsrClient.App;

public partial class App : Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _trayIconService = new TrayIconService(_mainWindow);
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 6: Verify manually**

Run:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Expected:

- Main window opens with Chinese tabs.
- Minimize hides the window.
- Close button hides the window.
- Single-click tray icon restores the window.
- Right-click tray icon shows “打开窗口” and “退出程序”.
- “退出程序” exits the process.

- [ ] **Step 7: Verify build**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: build and tests pass.

- [ ] **Step 8: Commit**

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: add WPF shell and tray behavior"
```

---

### Task 7: Overlay Window And Debug State Simulation

**Files:**
- Create: `src/LocalAsrClient.App/Overlay/OverlayState.cs`
- Create: `src/LocalAsrClient.App/Overlay/OverlayViewModel.cs`
- Create: `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml`
- Create: `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/DebugViewModel.cs`
- Modify: `src/LocalAsrClient.App/MainWindow.xaml`

- [ ] **Step 1: Add overlay models**

Create `src/LocalAsrClient.App/Overlay/OverlayState.cs`:

```csharp
namespace LocalAsrClient.App.Overlay;

public enum OverlayState
{
    LoadingModel,
    Ready,
    Recording,
    Transcribing,
    Injecting,
    Injected,
    ResultNeedsAction,
    Error
}
```

Create `src/LocalAsrClient.App/Overlay/OverlayViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace LocalAsrClient.App.Overlay;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private OverlayState _state;
    private string _message = "可录音";
    private string _resultText = "";
    private bool _showCopyButton;

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); }
    }

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public string ResultText
    {
        get => _resultText;
        set { _resultText = value; OnPropertyChanged(); }
    }

    public bool ShowCopyButton
    {
        get => _showCopyButton;
        set { _showCopyButton = value; OnPropertyChanged(); }
    }

    public ICommand CopyCommand => new RelayCommand(() =>
    {
        if (!string.IsNullOrWhiteSpace(ResultText))
        {
            Clipboard.SetText(ResultText);
        }
    });

    public void ShowState(OverlayState state, string message, string resultText = "")
    {
        State = state;
        Message = message;
        ResultText = resultText;
        ShowCopyButton = state == OverlayState.ResultNeedsAction;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
```

- [ ] **Step 2: Add overlay XAML**

Create `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml`:

```xml
<Window x:Class="LocalAsrClient.App.Overlay.DictationOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="520"
        SizeToContent="Height"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize">
    <Border Background="#F8FAFC"
            BorderBrush="#CBD5E1"
            BorderThickness="1"
            CornerRadius="8"
            Padding="16">
        <StackPanel>
            <TextBlock Text="{Binding Message}"
                       FontSize="16"
                       FontWeight="SemiBold"
                       Foreground="#0F172A"
                       Margin="0,0,0,8"/>
            <TextBox Text="{Binding ResultText}"
                     TextWrapping="Wrap"
                     MinHeight="80"
                     MaxHeight="180"
                     IsReadOnly="True"
                     VerticalScrollBarVisibility="Auto"
                     Visibility="{Binding ShowCopyButton, Converter={StaticResource BooleanToVisibilityConverter}}"/>
            <Button Content="复制"
                    Width="88"
                    HorizontalAlignment="Right"
                    Margin="0,12,0,0"
                    Command="{Binding CopyCommand}"
                    Visibility="{Binding ShowCopyButton, Converter={StaticResource BooleanToVisibilityConverter}}"/>
        </StackPanel>
    </Border>
</Window>
```

Add a Boolean converter resource in `src/LocalAsrClient.App/App.xaml`:

```xml
<Application x:Class="LocalAsrClient.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Add non-activating overlay code-behind**

Create `src/LocalAsrClient.App/Overlay/DictationOverlayWindow.xaml.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalAsrClient.App.Overlay;

public partial class DictationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private readonly OverlayViewModel _viewModel = new();

    public DictationOverlayWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public void ShowOverlay(OverlayState state, string message, string resultText = "")
    {
        _viewModel.ShowState(state, message, resultText);
        PositionBottomCenter();
        Show();
    }

    public void HideOverlay()
    {
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExNoActivate);
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - ActualHeight - 80;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
```

- [ ] **Step 4: Add Debug view model commands**

Replace `src/LocalAsrClient.App/ViewModels/DebugViewModel.cs`:

```csharp
using System.Windows.Input;
using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.ViewModels;

public sealed class DebugViewModel
{
    private readonly DictationOverlayWindow _overlayWindow;

    public DebugViewModel()
        : this(new DictationOverlayWindow())
    {
    }

    public DebugViewModel(DictationOverlayWindow overlayWindow)
    {
        _overlayWindow = overlayWindow;
        SampleText = "这是一段模拟的语音识别结果，用来测试浮窗宽度、换行和复制按钮。";
    }

    public string SampleText { get; set; }

    public ICommand ShowLoadingCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.LoadingModel, "模型加载中"));
    public ICommand ShowReadyCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Ready, "可录音"));
    public ICommand ShowRecordingCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Recording, "正在聆听"));
    public ICommand ShowTranscribingCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Transcribing, "识别中"));
    public ICommand ShowInjectingCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Injecting, "正在输入"));
    public ICommand ShowInjectedCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Injected, "已输入"));
    public ICommand ShowResultCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.ResultNeedsAction, "未找到可输入位置", SampleText));
    public ICommand ShowErrorCommand => new RelayCommand(() => _overlayWindow.ShowOverlay(OverlayState.Error, "输入失败"));
    public ICommand HideCommand => new RelayCommand(_overlayWindow.HideOverlay);

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
```

- [ ] **Step 5: Replace Debug tab content**

In `src/LocalAsrClient.App/MainWindow.xaml`, replace the Debug tab body:

```xml
<TabItem Header="Debug">
    <StackPanel Margin="16" Width="520" HorizontalAlignment="Left">
        <TextBlock Text="浮窗状态模拟" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,12"/>
        <WrapPanel>
            <Button Content="模型加载中" Command="{Binding Debug.ShowLoadingCommand}" Margin="0,0,8,8"/>
            <Button Content="可录音" Command="{Binding Debug.ShowReadyCommand}" Margin="0,0,8,8"/>
            <Button Content="正在聆听" Command="{Binding Debug.ShowRecordingCommand}" Margin="0,0,8,8"/>
            <Button Content="识别中" Command="{Binding Debug.ShowTranscribingCommand}" Margin="0,0,8,8"/>
            <Button Content="正在输入" Command="{Binding Debug.ShowInjectingCommand}" Margin="0,0,8,8"/>
            <Button Content="已输入" Command="{Binding Debug.ShowInjectedCommand}" Margin="0,0,8,8"/>
            <Button Content="识别结果" Command="{Binding Debug.ShowResultCommand}" Margin="0,0,8,8"/>
            <Button Content="错误状态" Command="{Binding Debug.ShowErrorCommand}" Margin="0,0,8,8"/>
            <Button Content="隐藏浮窗" Command="{Binding Debug.HideCommand}" Margin="0,0,8,8"/>
        </WrapPanel>
    </StackPanel>
</TabItem>
```

- [ ] **Step 6: Verify manually**

Run:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Expected:

- Debug tab shows Chinese buttons.
- Each button shows the bottom-centered overlay.
- “识别结果” shows sample text and a “复制” button.
- Overlay does not take focus away from the main window when shown.
- “隐藏浮窗” hides the overlay.

- [ ] **Step 7: Build and commit**

Run:

```powershell
dotnet build LocalAsrClient.sln
```

Expected: build succeeds.

Commit:

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: add dictation overlay and debug controls"
```

---

### Task 8: Audio Recorder And Text Injector

**Files:**
- Create: `src/LocalAsrClient.App/Audio/NAudioMemoryRecorder.cs`
- Create: `src/LocalAsrClient.App/TextInjection/Win32InputNative.cs`
- Create: `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs`

- [ ] **Step 1: Implement memory audio recorder**

Create `src/LocalAsrClient.App/Audio/NAudioMemoryRecorder.cs`:

```csharp
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Dictation;
using NAudio.Wave;

namespace LocalAsrClient.App.Audio;

public sealed class NAudioMemoryRecorder : IAudioRecorder, IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private DateTimeOffset _startedAt;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _startedAt = DateTimeOffset.Now;
        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        if (_waveIn is null || _writer is null || _buffer is null)
        {
            throw new InvalidOperationException("录音尚未开始。");
        }

        _waveIn.StopRecording();
        _writer.Flush();
        _writer.Dispose();
        var data = _buffer.ToArray();
        var duration = DateTimeOffset.Now - _startedAt;

        _waveIn.Dispose();
        _buffer.Dispose();
        _waveIn = null;
        _writer = null;
        _buffer = null;

        return Task.FromResult(new RecordingResult(data, duration, 16000, 1));
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _waveIn?.Dispose();
        _buffer?.Dispose();
    }
}
```

- [ ] **Step 2: Implement Win32 SendInput wrapper**

Create `src/LocalAsrClient.App/TextInjection/Win32InputNative.cs`:

```csharp
using System.Runtime.InteropServices;

namespace LocalAsrClient.App.TextInjection;

internal static class Win32InputNative
{
    public const int InputKeyboard = 1;
    public const ushort KeyEventFUnicode = 0x0004;
    public const ushort KeyEventFKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
```

- [ ] **Step 3: Implement SendInput text injector**

Create `src/LocalAsrClient.App/TextInjection/SendInputTextInjector.cs`:

```csharp
using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.App.TextInjection;

public sealed class SendInputTextInjector : ITextInjector
{
    public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, "识别文本为空。"));
        }

        var inputs = new List<Win32InputNative.Input>(text.Length * 2);
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs.Add(CreateUnicodeInput(ch, keyUp: false));
            inputs.Add(CreateUnicodeInput(ch, keyUp: true));
        }

        var sent = Win32InputNative.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32InputNative.Input>());
        if (sent != inputs.Count)
        {
            return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Failed, $"SendInput 只发送了 {sent}/{inputs.Count} 个输入事件。"));
        }

        return Task.FromResult(new TextInjectionResult(TextInjectionStatus.Success, null));
    }

    private static Win32InputNative.Input CreateUnicodeInput(char ch, bool keyUp)
    {
        return new Win32InputNative.Input
        {
            Type = Win32InputNative.InputKeyboard,
            Union = new Win32InputNative.InputUnion
            {
                KeyboardInput = new Win32InputNative.KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = ch,
                    Flags = Win32InputNative.KeyEventFUnicode | (keyUp ? Win32InputNative.KeyEventFKeyUp : 0),
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}
```

- [ ] **Step 4: Manual injector verification**

Run the app:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

During Task 10 the Debug panel will expose a button for injection. For this task, only build verification is required.

- [ ] **Step 5: Build**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: build and tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: add audio recorder and text injector"
```

---

### Task 9: Right Alt Hotkey Listener

**Files:**
- Create: `src/LocalAsrClient.Core/Abstractions/IHotkeyListener.cs`
- Create: `src/LocalAsrClient.App/Hotkeys/Win32HotkeyNative.cs`
- Create: `src/LocalAsrClient.App/Hotkeys/RightAltHotkeyListener.cs`

- [ ] **Step 1: Add hotkey abstraction**

Create `src/LocalAsrClient.Core/Abstractions/IHotkeyListener.cs`:

```csharp
namespace LocalAsrClient.Core.Abstractions;

public interface IHotkeyListener : IDisposable
{
    event Action? Triggered;
    bool IsRunning { get; }
    void Start();
    void Stop();
}
```

- [ ] **Step 2: Add Win32 hook declarations**

Create `src/LocalAsrClient.App/Hotkeys/Win32HotkeyNative.cs`:

```csharp
using System.Runtime.InteropServices;

namespace LocalAsrClient.App.Hotkeys;

internal static class Win32HotkeyNative
{
    public const int WhKeyboardLl = 13;
    public const int WmKeyDown = 0x0100;
    public const int WmSysKeyDown = 0x0104;
    public const int VkRMenu = 0xA5;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? moduleName);
}
```

- [ ] **Step 3: Implement right Alt listener**

Create `src/LocalAsrClient.App/Hotkeys/RightAltHotkeyListener.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.App.Hotkeys;

public sealed class RightAltHotkeyListener : IHotkeyListener
{
    private readonly Win32HotkeyNative.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _isDown;

    public RightAltHotkeyListener()
    {
        _callback = HookCallback;
    }

    public event Action? Triggered;
    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = Win32HotkeyNative.GetModuleHandle(module.ModuleName);
        _hook = Win32HotkeyNative.SetWindowsHookEx(Win32HotkeyNative.WhKeyboardLl, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法注册右 Alt 全局监听。");
        }
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32HotkeyNative.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _isDown = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<Win32HotkeyNative.KbdLlHookStruct>(lParam);
            if ((message == Win32HotkeyNative.WmKeyDown || message == Win32HotkeyNative.WmSysKeyDown)
                && data.VkCode == Win32HotkeyNative.VkRMenu)
            {
                if (!_isDown)
                {
                    _isDown = true;
                    Triggered?.Invoke();
                }
            }
            else if (data.VkCode == Win32HotkeyNative.VkRMenu)
            {
                _isDown = false;
            }
        }

        return Win32HotkeyNative.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
```

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build LocalAsrClient.sln
```

Expected: build succeeds.

- [ ] **Step 5: Manual hotkey verification**

In Task 11 the listener will be wired to the orchestrator. For this task, verify only that the app still launches:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Expected: app launches without crashing.

- [ ] **Step 6: Commit**

```powershell
git add src/LocalAsrClient.Core src/LocalAsrClient.App
git commit -m "feat: add right Alt hotkey listener"
```

---

### Task 10: Application Service Bootstrap

**Files:**
- Create: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`
- Modify: `src/LocalAsrClient.App/App.xaml.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/ModelViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/DebugViewModel.cs`

- [ ] **Step 1: Add service bootstrap**

Create `src/LocalAsrClient.App/Bootstrap/AppServices.cs`:

```csharp
using LocalAsrClient.App.Audio;
using LocalAsrClient.App.Hotkeys;
using LocalAsrClient.App.Overlay;
using LocalAsrClient.App.TextInjection;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.App.Bootstrap;

public sealed class AppServices : IAsyncDisposable
{
    private AppServices(
        SqliteDatabase database,
        SqliteSettingsStore settingsStore,
        SqliteStatsRepository statsRepository,
        SqliteTextHistoryRepository historyRepository,
        DictationOverlayWindow overlayWindow,
        RightAltHotkeyListener hotkeyListener,
        DictationOrchestrator orchestrator,
        WhisperServerProcessManager serverManager)
    {
        Database = database;
        SettingsStore = settingsStore;
        StatsRepository = statsRepository;
        HistoryRepository = historyRepository;
        OverlayWindow = overlayWindow;
        HotkeyListener = hotkeyListener;
        Orchestrator = orchestrator;
        ServerManager = serverManager;
    }

    public SqliteDatabase Database { get; }
    public SqliteSettingsStore SettingsStore { get; }
    public SqliteStatsRepository StatsRepository { get; }
    public SqliteTextHistoryRepository HistoryRepository { get; }
    public DictationOverlayWindow OverlayWindow { get; }
    public RightAltHotkeyListener HotkeyListener { get; }
    public DictationOrchestrator Orchestrator { get; }
    public WhisperServerProcessManager ServerManager { get; }

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken)
    {
        var defaultSettings = AppSettings.CreateDefault(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var databasePath = Path.Combine(defaultSettings.DataDirectory, "client.db");
        var database = await SqliteDatabase.OpenAsync(databasePath, cancellationToken);
        var settingsStore = new SqliteSettingsStore(database);
        var settings = await settingsStore.LoadAsync(cancellationToken);

        var options = new WhisperServerOptions(
            settings.WhisperServerPath,
            settings.ModelPath,
            "127.0.0.1",
            8080);
        var serverManager = new WhisperServerProcessManager(options);
        var httpClient = new HttpClient { BaseAddress = options.BaseUri };
        var backend = new ManagedWhisperServerBackend(serverManager, new WhisperServerClient(httpClient));

        var statsRepository = new SqliteStatsRepository(database);
        var historyRepository = new SqliteTextHistoryRepository(database);
        var recorder = new NAudioMemoryRecorder();
        var injector = new SendInputTextInjector();
        var overlayWindow = new DictationOverlayWindow();
        var hotkeyListener = new RightAltHotkeyListener();
        var orchestrator = new DictationOrchestrator(
            recorder,
            backend,
            injector,
            statsRepository,
            historyRepository,
            settingsStore,
            new SystemClock());

        if (settings.StartModelOnAppStartup)
        {
            _ = serverManager.EnsureStartedAsync(CancellationToken.None);
        }

        return new AppServices(database, settingsStore, statsRepository, historyRepository, overlayWindow, hotkeyListener, orchestrator, serverManager);
    }

    public async ValueTask DisposeAsync()
    {
        HotkeyListener.Dispose();
        await ServerManager.StopAsync(CancellationToken.None);
        await Database.DisposeAsync();
    }
}
```

- [ ] **Step 2: Update MainViewModel to accept services**

Replace `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`:

```csharp
using LocalAsrClient.App.Bootstrap;

namespace LocalAsrClient.App.ViewModels;

public sealed class MainViewModel
{
    public MainViewModel(AppServices services)
    {
        Status = new StatusViewModel();
        History = new HistoryViewModel();
        Stats = new StatsViewModel();
        Model = new ModelViewModel(services);
        Settings = new SettingsViewModel();
        Debug = new DebugViewModel(services);
    }

    public StatusViewModel Status { get; }
    public HistoryViewModel History { get; }
    public StatsViewModel Stats { get; }
    public ModelViewModel Model { get; }
    public SettingsViewModel Settings { get; }
    public DebugViewModel Debug { get; }
}
```

- [ ] **Step 3: Add model service controls**

Replace `src/LocalAsrClient.App/ViewModels/ModelViewModel.cs`:

```csharp
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;

namespace LocalAsrClient.App.ViewModels;

public sealed class ModelViewModel
{
    private readonly AppServices _services;

    public ModelViewModel(AppServices services)
    {
        _services = services;
    }

    public string ServiceState => _services.ServerManager.Status.ToString();
    public string ServiceAddress => _services.ServerManager.BaseUri.ToString();

    public ICommand StartCommand => new RelayCommand(async () => await _services.ServerManager.EnsureStartedAsync(CancellationToken.None));
    public ICommand StopCommand => new RelayCommand(async () => await _services.ServerManager.StopAsync(CancellationToken.None));
    public ICommand RestartCommand => new RelayCommand(async () =>
    {
        await _services.ServerManager.StopAsync(CancellationToken.None);
        await _services.ServerManager.EnsureStartedAsync(CancellationToken.None);
    });
    public ICommand HealthCheckCommand => new RelayCommand(async () => await _services.ServerManager.EnsureStartedAsync(CancellationToken.None));

    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;

        public RelayCommand(Func<Task> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
```

- [ ] **Step 4: Update Debug view model to use shared overlay and services**

Replace `src/LocalAsrClient.App/ViewModels/DebugViewModel.cs`:

```csharp
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.ViewModels;

public sealed class DebugViewModel
{
    private readonly AppServices _services;

    public DebugViewModel(AppServices services)
    {
        _services = services;
        SampleText = "这是一段模拟的语音识别结果，用来测试浮窗宽度、换行和复制按钮。";
    }

    public string SampleText { get; set; }

    public ICommand ShowLoadingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.LoadingModel, "模型加载中"));
    public ICommand ShowReadyCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Ready, "可录音"));
    public ICommand ShowRecordingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Recording, "正在聆听"));
    public ICommand ShowTranscribingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Transcribing, "识别中"));
    public ICommand ShowInjectingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Injecting, "正在输入"));
    public ICommand ShowInjectedCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Injected, "已输入"));
    public ICommand ShowResultCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.ResultNeedsAction, "未找到可输入位置", SampleText));
    public ICommand ShowErrorCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Error, "输入失败"));
    public ICommand HideCommand => new RelayCommand(_services.OverlayWindow.HideOverlay);
    public ICommand TestInjectionCommand => new RelayCommand(async () => await _services.Orchestrator.ToggleAsync(CancellationToken.None));

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
```

- [ ] **Step 5: Update app startup**

Modify `src/LocalAsrClient.App/App.xaml.cs`:

```csharp
using System.Windows;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Tray;

namespace LocalAsrClient.App;

public partial class App : Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = await AppServices.CreateAsync(CancellationToken.None);
        _mainWindow = new MainWindow(_services);
        MainWindow = _mainWindow;
        _trayIconService = new TrayIconService(_mainWindow);
        _mainWindow.Show();
        _services.HotkeyListener.Start();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        base.OnExit(e);
    }
}
```

Modify `src/LocalAsrClient.App/MainWindow.xaml.cs` constructor:

```csharp
public MainWindow(AppServices services)
{
    InitializeComponent();
    DataContext = new MainViewModel(services);
}
```

Add these `using` statements:

```csharp
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;
```

- [ ] **Step 6: Build**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: build and tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: wire application services"
```

---

### Task 11: Connect Hotkey, Orchestrator, Overlay, And Main UI

**Files:**
- Modify: `src/LocalAsrClient.App/Bootstrap/AppServices.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/StatusViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add status update methods**

Replace `src/LocalAsrClient.App/ViewModels/StatusViewModel.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ViewModels;

public sealed class StatusViewModel : INotifyPropertyChanged
{
    private string _currentState = "空闲";
    private string _currentModel = "未选择模型";
    private string _serviceState = "未启动";
    private string _hotkey = "右 Alt";
    private string _lastResult = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentState { get => _currentState; set { _currentState = value; OnPropertyChanged(); } }
    public string CurrentModel { get => _currentModel; set { _currentModel = value; OnPropertyChanged(); } }
    public string ServiceState { get => _serviceState; set { _serviceState = value; OnPropertyChanged(); } }
    public string Hotkey { get => _hotkey; set { _hotkey = value; OnPropertyChanged(); } }
    public string LastResult { get => _lastResult; set { _lastResult = value; OnPropertyChanged(); } }

    public void Apply(DictationStatus status)
    {
        CurrentState = status.Message;
        LastResult = status.ResultText ?? LastResult;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

- [ ] **Step 2: Let MainViewModel expose status handling**

Modify `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`:

```csharp
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Overlay;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ViewModels;

public sealed class MainViewModel
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Status = new StatusViewModel();
        History = new HistoryViewModel();
        Stats = new StatsViewModel();
        Model = new ModelViewModel(services);
        Settings = new SettingsViewModel();
        Debug = new DebugViewModel(services);
        _services.Orchestrator.StatusChanged += OnDictationStatusChanged;
    }

    public StatusViewModel Status { get; }
    public HistoryViewModel History { get; }
    public StatsViewModel Stats { get; }
    public ModelViewModel Model { get; }
    public SettingsViewModel Settings { get; }
    public DebugViewModel Debug { get; }

    private void OnDictationStatusChanged(DictationStatus status)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Status.Apply(status);
            var overlayState = ToOverlayState(status.State);
            _services.OverlayWindow.ShowOverlay(overlayState, status.Message, status.ResultText ?? "");
            if (status.State == DictationState.Idle && status.Message == "已输入")
            {
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(700)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    _services.OverlayWindow.HideOverlay();
                };
                timer.Start();
            }
        });
    }

    private static OverlayState ToOverlayState(DictationState state)
    {
        return state switch
        {
            DictationState.EnsuringModelReady => OverlayState.LoadingModel,
            DictationState.Ready => OverlayState.Ready,
            DictationState.Recording => OverlayState.Recording,
            DictationState.Transcribing => OverlayState.Transcribing,
            DictationState.Injecting => OverlayState.Injecting,
            DictationState.ResultNeedsAction => OverlayState.ResultNeedsAction,
            DictationState.Error => OverlayState.Error,
            _ => OverlayState.Injected
        };
    }
}
```

- [ ] **Step 3: Wire hotkey to orchestrator**

Modify `src/LocalAsrClient.App/Bootstrap/AppServices.cs` after creating `orchestrator`:

```csharp
hotkeyListener.Triggered += () =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            await orchestrator.ToggleAsync(CancellationToken.None);
        }
        catch
        {
        }
    });
};
```

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
```

Expected: build and tests pass.

- [ ] **Step 5: Manual verification without ASR configured**

Run:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Press right Alt.

Expected:

- If paths are empty, overlay eventually shows an error in Chinese.
- App does not crash.
- Debug tab still simulates overlay states.

- [ ] **Step 6: Commit**

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: connect hotkey dictation flow"
```

---

### Task 12: Settings, Model, History, And Stats Panels

**Files:**
- Modify: `src/LocalAsrClient.App/ViewModels/HistoryViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/StatsViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/ModelViewModel.cs`
- Modify: `src/LocalAsrClient.App/ViewModels/MainViewModel.cs`
- Modify: `src/LocalAsrClient.App/MainWindow.xaml`

- [ ] **Step 1: Implement simple display view models**

Replace `src/LocalAsrClient.App/ViewModels/HistoryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class HistoryViewModel
{
    public ObservableCollection<TextHistoryEntry> Items { get; } = new();

    public ICommand CopyCommand => new RelayCommand<TextHistoryEntry>(entry =>
    {
        if (entry is not null)
        {
            Clipboard.SetText(entry.Text);
        }
    });

    public void Load(IEnumerable<TextHistoryEntry> entries)
    {
        Items.Clear();
        foreach (var entry in entries)
        {
            Items.Add(entry);
        }
    }

    private sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        public RelayCommand(Action<T?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute((T?)parameter);
    }
}
```

Replace `src/LocalAsrClient.App/ViewModels/StatsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class StatsViewModel
{
    public ObservableCollection<DailyStatsSnapshot> Days { get; } = new();

    public void Load(IEnumerable<DailyStatsSnapshot> days)
    {
        Days.Clear();
        foreach (var day in days)
        {
            Days.Add(day);
        }
    }
}
```

- [ ] **Step 2: Implement settings view model**

Replace `src/LocalAsrClient.App/ViewModels/SettingsViewModel.cs`:

```csharp
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
    }

    public string ModelPath { get; set; } = "";
    public string WhisperServerPath { get; set; } = "";
    public string DataDirectory { get; set; } = "";
    public TranscriptRetentionPolicy TranscriptRetentionPolicy { get; set; } = TranscriptRetentionPolicy.SevenDays;
    public bool StartModelOnAppStartup { get; set; }

    public ICommand SaveCommand => new RelayCommand(async () =>
    {
        await _services.SettingsStore.SaveAsync(new AppSettings(
            ModelPath,
            WhisperServerPath,
            DataDirectory,
            TranscriptRetentionPolicy,
            StartModelOnAppStartup), CancellationToken.None);
    });

    public async Task LoadAsync()
    {
        var settings = await _services.SettingsStore.LoadAsync(CancellationToken.None);
        ModelPath = settings.ModelPath;
        WhisperServerPath = settings.WhisperServerPath;
        DataDirectory = settings.DataDirectory;
        TranscriptRetentionPolicy = settings.TranscriptRetentionPolicy;
        StartModelOnAppStartup = settings.StartModelOnAppStartup;
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        public RelayCommand(Func<Task> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
```

Change `MainViewModel` settings construction:

```csharp
Settings = new SettingsViewModel(services);
```

- [ ] **Step 3: Replace tabs with bound lists and settings fields**

In `src/LocalAsrClient.App/MainWindow.xaml`, replace History, Stats, Model, and Settings tab contents with:

```xml
<TabItem Header="历史">
    <DataGrid ItemsSource="{Binding History.Items}" AutoGenerateColumns="False" Margin="12">
        <DataGrid.Columns>
            <DataGridTextColumn Header="时间" Binding="{Binding CreatedAt}" Width="160"/>
            <DataGridTextColumn Header="文本" Binding="{Binding Text}" Width="*"/>
            <DataGridTextColumn Header="字数" Binding="{Binding CharacterCount}" Width="70"/>
            <DataGridTemplateColumn Header="操作" Width="90">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <Button Content="复制"
                                Command="{Binding DataContext.History.CopyCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                CommandParameter="{Binding}"/>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>
        </DataGrid.Columns>
    </DataGrid>
</TabItem>
<TabItem Header="统计">
    <DataGrid ItemsSource="{Binding Stats.Days}" AutoGenerateColumns="False" Margin="12">
        <DataGrid.Columns>
            <DataGridTextColumn Header="日期" Binding="{Binding Date}" Width="120"/>
            <DataGridTextColumn Header="次数" Binding="{Binding InputCount}" Width="80"/>
            <DataGridTextColumn Header="成功" Binding="{Binding SuccessCount}" Width="80"/>
            <DataGridTextColumn Header="失败" Binding="{Binding FailedCount}" Width="80"/>
            <DataGridTextColumn Header="录音秒数" Binding="{Binding RecordingSeconds}" Width="100"/>
            <DataGridTextColumn Header="识别秒数" Binding="{Binding ProcessingSeconds}" Width="100"/>
            <DataGridTextColumn Header="字数" Binding="{Binding CharacterCount}" Width="80"/>
            <DataGridTextColumn Header="词数" Binding="{Binding WordCount}" Width="80"/>
        </DataGrid.Columns>
    </DataGrid>
</TabItem>
<TabItem Header="模型">
    <StackPanel Margin="16" Width="520" HorizontalAlignment="Left">
        <TextBlock Text="{Binding Model.ServiceState}" Margin="0,0,0,8"/>
        <TextBlock Text="{Binding Model.ServiceAddress}" Margin="0,0,0,12"/>
        <WrapPanel>
            <Button Content="启动服务" Command="{Binding Model.StartCommand}" Margin="0,0,8,8"/>
            <Button Content="停止服务" Command="{Binding Model.StopCommand}" Margin="0,0,8,8"/>
            <Button Content="重启服务" Command="{Binding Model.RestartCommand}" Margin="0,0,8,8"/>
            <Button Content="健康检查" Command="{Binding Model.HealthCheckCommand}" Margin="0,0,8,8"/>
        </WrapPanel>
    </StackPanel>
</TabItem>
<TabItem Header="设置">
    <StackPanel Margin="16" Width="560" HorizontalAlignment="Left">
        <TextBlock Text="模型文件路径"/>
        <TextBox Text="{Binding Settings.ModelPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <TextBlock Text="whisper-server 路径"/>
        <TextBox Text="{Binding Settings.WhisperServerPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <TextBlock Text="数据存储路径"/>
        <TextBox Text="{Binding Settings.DataDirectory, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,12"/>
        <CheckBox Content="客户端启动时自动启动当前模型" IsChecked="{Binding Settings.StartModelOnAppStartup}" Margin="0,0,0,12"/>
        <Button Content="保存设置" Command="{Binding Settings.SaveCommand}" Width="100" HorizontalAlignment="Left"/>
    </StackPanel>
</TabItem>
```

- [ ] **Step 4: Load initial history and stats**

In `MainViewModel` constructor after subscriptions:

```csharp
_ = LoadAsync();
```

Add method:

```csharp
private async Task LoadAsync()
{
    await Settings.LoadAsync();
    var history = await _services.HistoryRepository.GetRecentAsync(50, CancellationToken.None);
    History.Load(history);
    var end = DateOnly.FromDateTime(DateTime.Now);
    var start = end.AddDays(-30);
    var stats = await _services.StatsRepository.GetRangeAsync(start, end, CancellationToken.None);
    Stats.Load(stats);
}
```

- [ ] **Step 5: Build and manual verify**

Run:

```powershell
dotnet build LocalAsrClient.sln
dotnet test LocalAsrClient.sln
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

Expected:

- Settings tab allows entering paths and saving.
- Model tab shows service address and service buttons.
- History and Stats tabs render empty grids without crashing.
- Debug tab still works.

- [ ] **Step 6: Commit**

```powershell
git add src/LocalAsrClient.App
git commit -m "feat: add settings history stats and model panels"
```

---

### Task 13: End-To-End Whisper Server Verification

**Files:**
- Modify only if verification exposes a defect in already-created files.

- [ ] **Step 1: Build in Release**

Run:

```powershell
dotnet build LocalAsrClient.sln -c Release
```

Expected: build succeeds.

- [ ] **Step 2: Configure settings in the app**

Run:

```powershell
dotnet run --project src/LocalAsrClient.App/LocalAsrClient.App.csproj
```

In Settings tab, set:

```text
模型文件路径: path to ggml large-v3-turbo q5_0 model
whisper-server 路径: path to whisper-server.exe
数据存储路径: default path or a local test path
客户端启动时自动启动当前模型: off
```

Click “保存设置”.

Expected: settings persist after app restart.

- [ ] **Step 3: Verify model lifecycle**

In Model tab, click “启动服务”.

Expected:

- No console window appears.
- Service status becomes ready after model load.
- `127.0.0.1:8080` responds in the app health check.

Click “停止服务”.

Expected: service process exits.

- [ ] **Step 4: Verify right Alt flow with Notepad**

Open Notepad, place the caret in the editor, then press right Alt.

Expected:

- Overlay appears at bottom center.
- If model is not ready, overlay shows “模型加载中” then “可录音”.
- Press right Alt again to begin recording.
- Speak a short Chinese sentence.
- Press right Alt again to stop.
- Overlay shows “识别中”, then “正在输入”, then “已输入”.
- Text appears in Notepad.
- Clipboard content remains unchanged unless the overlay copy button is clicked.

- [ ] **Step 5: Verify no-input-target fallback**

Click the desktop or a non-text area, then perform the same right Alt recording flow.

Expected:

- If injection fails, overlay remains visible.
- Overlay shows recognized text.
- Overlay shows “复制”.
- Clicking “复制” places the result text on the clipboard.

- [ ] **Step 6: Verify persistence**

After at least one successful recognition, open History and Stats tabs.

Expected:

- History shows recent text if retention policy is not disabled.
- Stats shows daily input count, recording seconds, processing seconds, character count, and word count.
- No audio file is written to the data directory.

- [ ] **Step 7: Verify tray lifecycle**

Minimize and close the main window.

Expected:

- App remains in tray.
- Single-click tray icon reopens window.
- Right-click tray icon menu has “打开窗口” and “退出程序”.
- “退出程序” shuts down the app and the managed `whisper-server` process.

- [ ] **Step 8: Commit verification fixes**

If Step 1-7 required code changes:

```powershell
git add src tests
git commit -m "fix: complete MVP verification fixes"
```

If no code changes were required, do not create an empty commit.

---

## Completion Checklist

- [ ] `dotnet test LocalAsrClient.sln` passes.
- [ ] `dotnet build LocalAsrClient.sln -c Release` passes.
- [ ] Tray lifecycle works manually.
- [ ] Right Alt starts/stops dictation manually.
- [ ] Managed `whisper-server` starts hidden and stops on app exit.
- [ ] Overlay state simulation works from Debug.
- [ ] Overlay does not steal focus during dictation.
- [ ] Successful injection does not overwrite clipboard.
- [ ] Fallback overlay copy button writes to clipboard only when clicked.
- [ ] Stats are retained independently from text history.
- [ ] Text history retention settings work.
- [ ] No audio files are stored as history or durable app data.

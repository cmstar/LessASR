using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.App.DemoMode;

public static class DemoDataSeeder
{
    public const int MinimumHistoryEntryCount = 20;

    public static async Task SeedAsync(
        SqliteDatabase database,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settingsStore = new SqliteSettingsStore(database);
        await settingsStore.SaveAsync(
            AppSettings.CreateDefault() with
            {
                ModelPath = @"C:\LessASR Demo\models\ggml-large-v3-turbo.bin",
                WhisperServerPath = @"C:\LessASR Demo\whisper-server.exe",
                TranscriptRetentionPolicy = TranscriptRetentionPolicy.OneMonth,
                StartModelOnAppStartup = false,
                PreferredTranscriptionLanguageId = "zh-Hans"
            },
            cancellationToken);

        var statsRepository = new SqliteStatsRepository(database);
        var today = DateOnly.FromDateTime(now.Date);
        for (var dayOffset = -(StatsViewModel.SummaryDayCount - 1); dayOffset <= 0; dayOffset++)
        {
            var date = today.AddDays(dayOffset);
            var inputCount = 6 + Math.Abs(dayOffset * 7 % 9);
            for (var attempt = 0; attempt < inputCount; attempt++)
            {
                var succeeded = attempt != inputCount - 1 || dayOffset % 4 != 0;
                var characters = succeeded ? 48 + Math.Abs((dayOffset * 13 + attempt * 17) % 96) : 0;
                await statsRepository.RecordAsync(
                    new DailyStatsDelta(
                        date,
                        succeeded,
                        TimeSpan.FromSeconds(18 + characters * 0.42),
                        TimeSpan.FromSeconds(succeeded ? 1.2 + attempt * 0.11 : 3.5),
                        characters,
                        succeeded ? Math.Max(1, characters / 2) : 0),
                    cancellationToken);
            }
        }

        var historyRepository = new SqliteTextHistoryRepository(database);
        for (var index = 0; index < DemoDataScenario.HistoryTexts.Count; index++)
        {
            var text = DemoDataScenario.HistoryTexts[index];
            var todayStart = new DateTimeOffset(now.Date, now.Offset);
            var createdAt = index switch
            {
                < 7 => todayStart.AddHours(16).AddMinutes(-(index * 47)),
                < 12 => todayStart.AddDays(-1).AddHours(15).AddMinutes(-((index - 7) * 53)),
                _ => todayStart.AddDays(-2 - (index - 12) / 3).AddHours(14 - index % 3)
            };
            await historyRepository.AddAsync(
                new TextHistoryEntry(
                    CreateDeterministicGuid(index),
                    createdAt,
                    text,
                    TextMetrics.CountCharacters(text),
                    TextMetrics.CountWords(text),
                    TimeSpan.FromSeconds(20 + text.Length * 0.45),
                    TimeSpan.FromSeconds(1.4 + index * 0.05),
                    "demo-asr",
                    "ggml-large-v3-turbo"),
                cancellationToken);
        }
    }

    private static Guid CreateDeterministicGuid(int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = 0x4C;
        bytes[1] = 0x41;
        bytes[2] = 0x53;
        bytes[3] = 0x52;
        BitConverter.TryWriteBytes(bytes[12..], index + 1);
        return new Guid(bytes);
    }
}

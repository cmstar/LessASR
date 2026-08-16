using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class StatsViewModelTests
{
    [Fact]
    public void LoadBuildsTodayAndThirtyDaySummary()
    {
        var today = new DateOnly(2026, 7, 22);
        var viewModel = new StatsViewModel();

        viewModel.Load(
        [
            Snapshot(today.AddDays(-1), inputCount: 4, successCount: 3, characterCount: 320, recordingSeconds: 95),
            Snapshot(today, inputCount: 6, successCount: 6, characterCount: 780, recordingSeconds: 125)
        ],
        today);

        Assert.Equal(6, viewModel.TodayInputCount);
        Assert.Equal(780, viewModel.TodayCharacterCount);
        Assert.Equal("2 分 5 秒", viewModel.TodayRecordingDurationText);
        Assert.Equal(10, viewModel.ThirtyDayInputCount);
        Assert.Equal(1_100, viewModel.ThirtyDayCharacterCount);
        Assert.Equal("3 分 40 秒", viewModel.ThirtyDayRecordingDurationText);
        Assert.Equal("300.0", viewModel.ThirtyDayCharactersPerMinuteText);
    }

    [Fact]
    public void LoadUsesZeroSpeedWhenThirtyDayRecordingDurationIsMissing()
    {
        var today = new DateOnly(2026, 7, 22);
        var viewModel = new StatsViewModel();

        viewModel.Load(
        [
            Snapshot(today, inputCount: 2, characterCount: 120)
        ],
        today);

        Assert.Equal("0 秒", viewModel.ThirtyDayRecordingDurationText);
        Assert.Equal("0.0", viewModel.ThirtyDayCharactersPerMinuteText);
    }

    [Fact]
    public void LoadBuildsSevenOrderedTrendPointsIncludingEmptyDays()
    {
        var today = new DateOnly(2026, 7, 22);
        var viewModel = new StatsViewModel();

        viewModel.Load(
        [
            Snapshot(today, characterCount: 700),
            Snapshot(today.AddDays(-3), characterCount: 350)
        ],
        today);

        Assert.Equal(7, viewModel.LastSevenDays.Count);
        Assert.Equal(today.AddDays(-6), viewModel.LastSevenDays[0].Date);
        Assert.Equal(today, viewModel.LastSevenDays[6].Date);
        Assert.Equal(0, viewModel.LastSevenDays[1].CharacterCount);
        Assert.Equal(350, viewModel.LastSevenDays[3].CharacterCount);
        Assert.Equal(700, viewModel.LastSevenDays[6].CharacterCount);
        Assert.True(viewModel.LastSevenDays[6].BarHeight > viewModel.LastSevenDays[3].BarHeight);
    }

    [Fact]
    public void LoadMultipleProvidersOnSameDayKeepsDailyRowsButAggregatesSummaryAndTrend()
    {
        var today = new DateOnly(2026, 8, 16);
        var viewModel = new StatsViewModel();

        viewModel.Load(
        [
            Snapshot(today, "本地 Whisper", inputCount: 2, successCount: 2, characterCount: 120, recordingSeconds: 30),
            Snapshot(today, "Groq turbo", inputCount: 3, successCount: 2, characterCount: 180, recordingSeconds: 45)
        ],
        today);

        Assert.Equal(2, viewModel.Days.Count);
        Assert.Equal(["Groq turbo", "本地 Whisper"], viewModel.Days.Select(day => day.ProviderName));
        Assert.Equal(5, viewModel.TodayInputCount);
        Assert.Equal(300, viewModel.TodayCharacterCount);
        Assert.Equal("1 分 15 秒", viewModel.TodayRecordingDurationText);
        Assert.Equal(5, viewModel.ThirtyDayInputCount);
        Assert.Equal(300, viewModel.ThirtyDayCharacterCount);
        Assert.Equal(300, viewModel.LastSevenDays[^1].CharacterCount);
    }

    private static DailyStatsSnapshot Snapshot(
        DateOnly date,
        string providerName = "本地 Whisper",
        int inputCount = 0,
        int successCount = 0,
        int characterCount = 0,
        double recordingSeconds = 0)
    {
        return new DailyStatsSnapshot(
            date,
            providerName,
            inputCount,
            successCount,
            inputCount - successCount,
            recordingSeconds,
            0,
            characterCount,
            0);
    }
}

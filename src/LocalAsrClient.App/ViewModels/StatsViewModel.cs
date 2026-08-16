using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class StatsViewModel : INotifyPropertyChanged
{
    public const int SummaryDayCount = 30;

    public const int TrendDayCount = 7;

    public ObservableCollection<DailyStatsSnapshot> Days { get; } = new();

    public ObservableCollection<StatsTrendPointViewModel> LastSevenDays { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public int TodayInputCount { get; private set; }

    public int TodayCharacterCount { get; private set; }

    public string TodayRecordingDurationText { get; private set; } = "0 秒";

    public int ThirtyDayInputCount { get; private set; }

    public int ThirtyDayCharacterCount { get; private set; }

    public string ThirtyDayRecordingDurationText { get; private set; } = "0 秒";

    public string ThirtyDayCharactersPerMinuteText { get; private set; } = "0.0";

    public int SevenDayCharacterCount => LastSevenDays.Sum(point => point.CharacterCount);

    public void Load(IEnumerable<DailyStatsSnapshot> days)
    {
        Load(days, DateOnly.FromDateTime(DateTime.Now));
    }

    public void Load(IEnumerable<DailyStatsSnapshot> days, DateOnly today)
    {
        var start = today.AddDays(-(SummaryDayCount - 1));
        var snapshots = days
            .Where(day => day.Date >= start && day.Date <= today)
            .OrderBy(day => day.Date)
            .ThenBy(day => day.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Days.Clear();
        foreach (var day in snapshots
                     .OrderByDescending(day => day.Date)
                     .ThenBy(day => day.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            Days.Add(day);
        }

        var todaySnapshots = snapshots.Where(day => day.Date == today).ToArray();
        TodayInputCount = todaySnapshots.Sum(day => day.InputCount);
        TodayCharacterCount = todaySnapshots.Sum(day => day.CharacterCount);
        TodayRecordingDurationText = FormatDuration(todaySnapshots.Sum(day => Math.Max(0, day.RecordingSeconds)));
        ThirtyDayInputCount = snapshots.Sum(day => day.InputCount);
        ThirtyDayCharacterCount = snapshots.Sum(day => day.CharacterCount);
        var thirtyDayRecordingSeconds = snapshots.Sum(day => Math.Max(0, day.RecordingSeconds));
        ThirtyDayRecordingDurationText = FormatDuration(thirtyDayRecordingSeconds);
        ThirtyDayCharactersPerMinuteText = thirtyDayRecordingSeconds <= 0
            ? "0.0"
            : $"{ThirtyDayCharacterCount * 60d / thirtyDayRecordingSeconds:0.0}";

        BuildLastSevenDays(snapshots, today);
        OnPropertyChanged(nameof(TodayInputCount));
        OnPropertyChanged(nameof(TodayCharacterCount));
        OnPropertyChanged(nameof(TodayRecordingDurationText));
        OnPropertyChanged(nameof(ThirtyDayInputCount));
        OnPropertyChanged(nameof(ThirtyDayCharacterCount));
        OnPropertyChanged(nameof(ThirtyDayRecordingDurationText));
        OnPropertyChanged(nameof(ThirtyDayCharactersPerMinuteText));
        OnPropertyChanged(nameof(SevenDayCharacterCount));
    }

    private void BuildLastSevenDays(IReadOnlyCollection<DailyStatsSnapshot> snapshots, DateOnly today)
    {
        var start = today.AddDays(-(TrendDayCount - 1));
        var byDate = snapshots
            .GroupBy(snapshot => snapshot.Date)
            .ToDictionary(group => group.Key, group => group.Sum(snapshot => snapshot.CharacterCount));
        var counts = Enumerable.Range(0, TrendDayCount)
            .Select(offset =>
            {
                var date = start.AddDays(offset);
                return (Date: date, Count: byDate.GetValueOrDefault(date));
            })
            .ToArray();
        var maximum = Math.Max(1, counts.Max(point => point.Count));

        LastSevenDays.Clear();
        foreach (var point in counts)
        {
            var height = point.Count == 0
                ? 4
                : Math.Max(8, point.Count * 88d / maximum);
            LastSevenDays.Add(new StatsTrendPointViewModel(
                point.Date,
                point.Count,
                height,
                ToWeekdayLabel(point.Date.DayOfWeek)));
        }
    }

    private static string FormatDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} 分 {duration.Seconds} 秒";
        }

        return $"{duration.Seconds} 秒";
    }

    private static string ToWeekdayLabel(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "一",
            DayOfWeek.Tuesday => "二",
            DayOfWeek.Wednesday => "三",
            DayOfWeek.Thursday => "四",
            DayOfWeek.Friday => "五",
            DayOfWeek.Saturday => "六",
            DayOfWeek.Sunday => "日",
            _ => string.Empty
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record StatsTrendPointViewModel(
    DateOnly Date,
    int CharacterCount,
    double BarHeight,
    string Label);

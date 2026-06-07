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

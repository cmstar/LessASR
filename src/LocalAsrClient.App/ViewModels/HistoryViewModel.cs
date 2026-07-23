using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private readonly Func<Guid, CancellationToken, Task> _deleteAsync;
    private readonly Func<TextHistoryEntry, bool> _confirmDelete;

    public HistoryViewModel(
        Func<Guid, CancellationToken, Task>? deleteAsync = null,
        Func<TextHistoryEntry, bool>? confirmDelete = null)
    {
        _deleteAsync = deleteAsync ?? ((_, _) => Task.CompletedTask);
        _confirmDelete = confirmDelete ?? (_ => false);
        CopyCommand = new RelayCommand<TextHistoryEntry>(entry =>
            System.Windows.Clipboard.SetText(entry.Text));
        DeleteCommand = new AsyncRelayCommand<TextHistoryEntry>(
            DeleteAsync,
            "删除历史记录失败");
    }

    public ObservableCollection<TextHistoryEntry> Items { get; } = new();

    public ObservableCollection<HistoryGroupViewModel> Groups { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasGroups => Groups.Count > 0;

    public bool HasNoGroups => !HasGroups;

    public ICommand CopyCommand { get; }

    public ICommand DeleteCommand { get; }

    public async Task DeleteAsync(TextHistoryEntry entry)
    {
        if (!_confirmDelete(entry))
        {
            return;
        }

        await _deleteAsync(entry.Id, CancellationToken.None);
    }

    public void Load(IEnumerable<TextHistoryEntry> entries)
    {
        Load(entries, DateTimeOffset.Now);
    }

    public void Load(IEnumerable<TextHistoryEntry> entries, DateTimeOffset now)
    {
        var ordered = entries.OrderByDescending(entry => entry.CreatedAt).ToArray();
        Items.Clear();
        foreach (var entry in ordered)
        {
            Items.Add(entry);
        }

        Groups.Clear();
        var today = DateOnly.FromDateTime(now.Date);
        var yesterday = today.AddDays(-1);
        var offset = now.Offset;
        AddGroup("今天", ordered.Where(entry => ToLocalDate(entry.CreatedAt, offset) == today));
        AddGroup("昨天", ordered.Where(entry => ToLocalDate(entry.CreatedAt, offset) == yesterday));
        AddGroup("更早", ordered.Where(entry => ToLocalDate(entry.CreatedAt, offset) < yesterday));
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasNoGroups));
    }

    private void AddGroup(string title, IEnumerable<TextHistoryEntry> entries)
    {
        var items = entries.ToArray();
        if (items.Length > 0)
        {
            Groups.Add(new HistoryGroupViewModel(title, items));
        }
    }

    private static DateOnly ToLocalDate(DateTimeOffset createdAt, TimeSpan offset)
    {
        return DateOnly.FromDateTime(createdAt.ToOffset(offset).Date);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class HistoryGroupViewModel
{
    public HistoryGroupViewModel(string title, IEnumerable<TextHistoryEntry> items)
    {
        Title = title;
        Items = new ObservableCollection<TextHistoryEntry>(items);
    }

    public string Title { get; }

    public ObservableCollection<TextHistoryEntry> Items { get; }
}

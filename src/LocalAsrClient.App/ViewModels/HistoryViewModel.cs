using System.Collections.ObjectModel;
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
            System.Windows.Clipboard.SetText(entry.Text);
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

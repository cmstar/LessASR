using System.Windows.Input;

namespace LocalAsrClient.App.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly string _context;

    public AsyncRelayCommand(Func<Task> execute, string context)
    {
        _execute = execute;
        _context = context;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, _context);
        }
    }
}

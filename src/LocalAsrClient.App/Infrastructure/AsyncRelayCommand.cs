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

public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private readonly string _context;

    public AsyncRelayCommand(Func<T, Task> execute, string context)
    {
        _execute = execute;
        _context = context;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public async void Execute(object? parameter)
    {
        if (parameter is not T value)
        {
            return;
        }

        try
        {
            await _execute(value);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, _context);
        }
    }
}

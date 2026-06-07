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
            System.Windows.Clipboard.SetText(ResultText);
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


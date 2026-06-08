using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace LocalAsrClient.App.Overlay;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly Action? _onClose;
    private OverlayState _state;
    private string _message = "可录音";
    private string _resultText = "";
    private string _errorMessage = "";
    private bool _showResultText;
    private bool _showCopyButton;
    private bool _showCloseButton;
    private double _resultMaxHeight = 180;

    public OverlayViewModel(Action? onClose = null)
    {
        _onClose = onClose;
    }

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

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public bool ShowResultText
    {
        get => _showResultText;
        set { _showResultText = value; OnPropertyChanged(); }
    }

    public bool ShowCopyButton
    {
        get => _showCopyButton;
        set { _showCopyButton = value; OnPropertyChanged(); }
    }

    public bool ShowCloseButton
    {
        get => _showCloseButton;
        set { _showCloseButton = value; OnPropertyChanged(); }
    }

    public double ResultMaxHeight
    {
        get => _resultMaxHeight;
        set { _resultMaxHeight = value; OnPropertyChanged(); }
    }

    public ICommand CopyCommand => new RelayCommand(() =>
    {
        if (!string.IsNullOrWhiteSpace(ResultText))
        {
            System.Windows.Clipboard.SetText(ResultText);
        }
    });

    public ICommand CloseCommand => new RelayCommand(() => _onClose?.Invoke());

    public void ShowState(OverlayState state, string message, string resultText = "", string? errorMessage = null)
    {
        State = state;
        Message = message;
        ResultText = resultText;
        ErrorMessage = errorMessage ?? string.Empty;
        ShowResultText = state is OverlayState.ResultNeedsAction or OverlayState.Error
            && !string.IsNullOrWhiteSpace(resultText);
        ShowCopyButton = ShowResultText;
        ShowCloseButton = state is OverlayState.Error
            or OverlayState.ResultNeedsAction
            or OverlayState.Ready
            or OverlayState.Recording;
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

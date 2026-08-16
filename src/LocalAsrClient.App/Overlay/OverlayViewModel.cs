using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.Overlay;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly Action? _onClose;
    private readonly Action? _onSubmit;
    private OverlayState _state;
    private string _message = "聆听中";
    private string _resultText = "";
    private string _errorMessage = "";
    private bool _showCopyLayout;
    private bool _showRecordingLayout;
    private bool _showStatusLayout = true;
    private bool _showCloseButton;
    private double _overlayMinHeight = 32;
    private double _overlayWidth = 96;
    private double _resultMaxHeight = 120;

    public OverlayViewModel(Action? onClose = null, Action? onSubmit = null)
    {
        _onClose = onClose;
        _onSubmit = onSubmit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusIcon));
        }
    }

    public SymbolRegular StatusIcon => State switch
    {
        OverlayState.LoadingModel or OverlayState.Injecting => SymbolRegular.ArrowSync16,
        OverlayState.Transcribing => SymbolRegular.MicPulse16,
        OverlayState.Injected => SymbolRegular.CheckmarkCircle16,
        OverlayState.ResultNeedsAction => SymbolRegular.ClipboardError16,
        OverlayState.Error => SymbolRegular.ErrorCircle16,
        _ => SymbolRegular.Mic16
    };

    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }

    public string ResultText
    {
        get => _resultText;
        private set { _resultText = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); }
    }

    public bool ShowCopyLayout
    {
        get => _showCopyLayout;
        private set { _showCopyLayout = value; OnPropertyChanged(); }
    }

    public bool ShowRecordingLayout
    {
        get => _showRecordingLayout;
        private set { _showRecordingLayout = value; OnPropertyChanged(); }
    }

    public bool ShowStatusLayout
    {
        get => _showStatusLayout;
        private set { _showStatusLayout = value; OnPropertyChanged(); }
    }

    public bool ShowCloseButton
    {
        get => _showCloseButton;
        private set { _showCloseButton = value; OnPropertyChanged(); }
    }

    public double ResultMaxHeight
    {
        get => _resultMaxHeight;
        set { _resultMaxHeight = value; OnPropertyChanged(); }
    }

    public double OverlayMinHeight
    {
        get => _overlayMinHeight;
        private set { _overlayMinHeight = value; OnPropertyChanged(); }
    }

    public double OverlayWidth
    {
        get => _overlayWidth;
        private set { _overlayWidth = value; OnPropertyChanged(); }
    }

    public ICommand CopyCommand => new RelayCommand(() =>
    {
        if (!string.IsNullOrWhiteSpace(ResultText))
        {
            System.Windows.Clipboard.SetText(ResultText);
        }
    });

    public ICommand CloseCommand => new RelayCommand(() => _onClose?.Invoke());

    public ICommand SubmitCommand => new RelayCommand(() => _onSubmit?.Invoke());

    public void ShowState(OverlayState state, string message, string resultText = "", string? errorMessage = null)
    {
        State = state;
        Message = message;
        ResultText = resultText;
        ErrorMessage = errorMessage ?? string.Empty;

        var needsCopy = state == OverlayState.ResultNeedsAction
            && !string.IsNullOrWhiteSpace(resultText);
        ShowCopyLayout = needsCopy;
        ShowRecordingLayout = state == OverlayState.Recording;
        ShowStatusLayout = !needsCopy && !ShowRecordingLayout;
        ShowCloseButton = state is OverlayState.Error
            or OverlayState.ResultNeedsAction;
        OverlayMinHeight = needsCopy ? 148 : 32;
        OverlayWidth = needsCopy
            ? 320
            : state switch
            {
                OverlayState.Recording => 118,
                OverlayState.Transcribing => 96,
                OverlayState.Injected => 86,
                OverlayState.LoadingModel => 118,
                OverlayState.Error or OverlayState.ResultNeedsAction => 142,
                _ => 96
            };
        ResultMaxHeight = 120;
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

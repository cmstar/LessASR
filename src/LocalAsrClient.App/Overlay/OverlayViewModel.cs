using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalAsrClient.App.Overlay;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush NormalBackground = CreateBrush(248, 250, 252);
    private static readonly SolidColorBrush NormalBorder = CreateBrush(203, 213, 225);
    private static readonly SolidColorBrush LoadingBackground = CreateBrush(255, 247, 237);
    private static readonly SolidColorBrush LoadingBorder = CreateBrush(253, 186, 116);
    private static readonly SolidColorBrush LoadingMessageForeground = CreateBrush(154, 52, 18);
    private static readonly SolidColorBrush SuccessBackground = CreateBrush(240, 253, 244);
    private static readonly SolidColorBrush SuccessBorder = CreateBrush(134, 239, 172);
    private static readonly SolidColorBrush SuccessMessageForeground = CreateBrush(22, 101, 52);
    private static readonly SolidColorBrush ErrorBackground = CreateBrush(254, 242, 242);
    private static readonly SolidColorBrush ErrorBorder = CreateBrush(252, 165, 165);
    private static readonly SolidColorBrush ErrorMessageForeground = CreateBrush(153, 27, 27);
    private static readonly SolidColorBrush ErrorDetailForegroundBrush = CreateBrush(185, 28, 28);
    private static readonly SolidColorBrush NormalMessageForeground = CreateBrush(15, 23, 42);

    private enum PanelTheme
    {
        Normal,
        Loading,
        Success,
        Error
    }

    private readonly Action? _onClose;
    private OverlayState _state;
    private string _message = "聆听中";
    private string _resultText = "";
    private string _errorMessage = "";
    private bool _showCopyLayout;
    private bool _showCompactLayout = true;
    private bool _showCopyButton;
    private bool _showCloseButton;
    private bool _showResultText;
    private PanelTheme _panelTheme = PanelTheme.Normal;
    private double _overlayMinHeight = 56;
    private double _overlayMinWidth = 96;
    private double _resultMaxHeight = 120;

    public OverlayViewModel(Action? onClose = null)
    {
        _onClose = onClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); }
    }

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

    public bool ShowCompactLayout
    {
        get => _showCompactLayout;
        private set { _showCompactLayout = value; OnPropertyChanged(); }
    }

    public bool ShowCopyButton
    {
        get => _showCopyButton;
        private set { _showCopyButton = value; OnPropertyChanged(); }
    }

    public bool ShowCloseButton
    {
        get => _showCloseButton;
        private set { _showCloseButton = value; OnPropertyChanged(); }
    }

    public bool ShowResultText
    {
        get => _showResultText;
        private set { _showResultText = value; OnPropertyChanged(); }
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

    public double OverlayMinWidth
    {
        get => _overlayMinWidth;
        private set { _overlayMinWidth = value; OnPropertyChanged(); }
    }

    public System.Windows.Media.Brush PanelBackground => _panelTheme switch
    {
        PanelTheme.Loading => LoadingBackground,
        PanelTheme.Success => SuccessBackground,
        PanelTheme.Error => ErrorBackground,
        _ => NormalBackground
    };

    public System.Windows.Media.Brush PanelBorderBrush => _panelTheme switch
    {
        PanelTheme.Loading => LoadingBorder,
        PanelTheme.Success => SuccessBorder,
        PanelTheme.Error => ErrorBorder,
        _ => NormalBorder
    };

    public System.Windows.Media.Brush MessageForeground => _panelTheme switch
    {
        PanelTheme.Loading => LoadingMessageForeground,
        PanelTheme.Success => SuccessMessageForeground,
        PanelTheme.Error => ErrorMessageForeground,
        _ => NormalMessageForeground
    };

    public System.Windows.Media.Brush ErrorDetailForeground => _panelTheme == PanelTheme.Error
        ? ErrorDetailForegroundBrush
        : NormalMessageForeground;

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
        ApplyPanelTheme(state switch
        {
            OverlayState.LoadingModel => PanelTheme.Loading,
            OverlayState.Injected => PanelTheme.Success,
            OverlayState.Error => PanelTheme.Error,
            _ => PanelTheme.Normal
        });

        var needsCopy = state == OverlayState.ResultNeedsAction
            && !string.IsNullOrWhiteSpace(resultText);
        ShowCopyLayout = needsCopy;
        ShowCompactLayout = !needsCopy;
        ShowCopyButton = needsCopy;
        ShowResultText = needsCopy;
        ShowCloseButton = state is OverlayState.Error
            or OverlayState.ResultNeedsAction
            or OverlayState.Recording;
        OverlayMinHeight = needsCopy ? 168 : 56;
        OverlayMinWidth = needsCopy ? 220 : 120;
        ResultMaxHeight = 120;
    }

    private void ApplyPanelTheme(PanelTheme theme)
    {
        if (_panelTheme == theme)
        {
            return;
        }

        _panelTheme = theme;
        OnPropertyChanged(nameof(PanelBackground));
        OnPropertyChanged(nameof(PanelBorderBrush));
        OnPropertyChanged(nameof(MessageForeground));
        OnPropertyChanged(nameof(ErrorDetailForeground));
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(global::System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
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

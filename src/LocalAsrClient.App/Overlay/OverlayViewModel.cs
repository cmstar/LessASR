using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LocalAsrClient.Core.Dictation;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.Overlay;

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly Action? _onClose;
    private readonly Action? _onSubmit;
    private readonly Action<Guid, string>? _onSegmentTextChanged;
    private OverlayState _state;
    private string _message = "聆听中";
    private string _resultText = "";
    private string _errorMessage = "";
    private bool _showCopyLayout;
    private bool _showRecordingLayout;
    private bool _showStatusLayout = true;
    private bool _showCloseButton;
    private bool _showSegmentLayout;
    private bool _showReviewLayout;
    private double _overlayMinHeight = 32;
    private double _overlayWidth = 96;
    private double _resultMaxHeight = 120;

    public OverlayViewModel(
        Action? onClose = null,
        Action? onSubmit = null,
        Action<Guid, string>? onSegmentTextChanged = null)
    {
        _onClose = onClose;
        _onSubmit = onSubmit;
        _onSegmentTextChanged = onSegmentTextChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InPlaceSegmentViewModel> Segments { get; } = [];

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

    public bool ShowSegmentLayout
    {
        get => _showSegmentLayout;
        private set { _showSegmentLayout = value; OnPropertyChanged(); }
    }

    public bool ShowReviewLayout
    {
        get => _showReviewLayout;
        private set { _showReviewLayout = value; OnPropertyChanged(); }
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
        ShowSegmentLayout = false;
        ShowReviewLayout = false;
        Segments.Clear();
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

    public void ApplyInPlaceStatus(InPlaceDictationStatus status)
    {
        State = status.State switch
        {
            InPlaceDictationState.EnsuringModelReady => OverlayState.LoadingModel,
            InPlaceDictationState.Recording => OverlayState.Recording,
            InPlaceDictationState.Reviewing => OverlayState.Reviewing,
            InPlaceDictationState.Finishing or InPlaceDictationState.Injecting => OverlayState.Transcribing,
            InPlaceDictationState.ResultNeedsAction => OverlayState.ResultNeedsAction,
            InPlaceDictationState.Error => OverlayState.Error,
            _ => OverlayState.Injected
        };
        Message = status.Message;
        ResultText = status.ResultText ?? string.Empty;
        ErrorMessage = status.ErrorMessage ?? string.Empty;

        var needsCopy = status.State == InPlaceDictationState.ResultNeedsAction
            && !string.IsNullOrWhiteSpace(status.ResultText);
        var isRecording = status.State == InPlaceDictationState.Recording;
        var isReviewing = status.State == InPlaceDictationState.Reviewing;
        UpdateSegments(status.Segments, isReviewing);

        ShowCopyLayout = needsCopy;
        ShowRecordingLayout = isRecording;
        ShowReviewLayout = isReviewing;
        ShowSegmentLayout = status.HasSegmented && !needsCopy;
        ShowStatusLayout = !needsCopy && !isRecording && !isReviewing;
        ShowCloseButton = status.State is InPlaceDictationState.Error
            or InPlaceDictationState.ResultNeedsAction;
        OverlayWidth = needsCopy
            ? 320
            : ShowSegmentLayout || isReviewing
                ? 360
                : State switch
                {
                    OverlayState.Recording => 118,
                    OverlayState.Transcribing => 108,
                    OverlayState.Injected => 86,
                    OverlayState.LoadingModel => 118,
                    OverlayState.Error or OverlayState.ResultNeedsAction => 142,
                    _ => 96
                };
        OverlayMinHeight = needsCopy ? 148 : ShowSegmentLayout ? 96 : 32;
        ResultMaxHeight = 120;
    }

    private void UpdateSegments(
        IReadOnlyList<ContinuousDictationSegment> segments,
        bool isReviewing)
    {
        var visible = segments
            .Where(segment => segment.State != ContinuousSegmentState.WaitingInput)
            .ToArray();
        var existing = Segments.ToDictionary(segment => segment.Id);
        for (var index = 0; index < visible.Length; index++)
        {
            var segment = visible[index];
            if (existing.TryGetValue(segment.Id, out var viewModel))
            {
                viewModel.UpdateFrom(segment, isReviewing);
                var currentIndex = Segments.IndexOf(viewModel);
                if (currentIndex != index)
                {
                    Segments.Move(currentIndex, index);
                }
            }
            else
            {
                Segments.Insert(index, new InPlaceSegmentViewModel(
                    segment,
                    isReviewing,
                    _onSegmentTextChanged));
            }
        }

        while (Segments.Count > visible.Length)
        {
            Segments.RemoveAt(Segments.Count - 1);
        }
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

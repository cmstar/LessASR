using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.Overlay;

public sealed class InPlaceSegmentViewModel : INotifyPropertyChanged
{
    private readonly Action<Guid, string>? _onTextChanged;
    private ContinuousSegmentState _state;
    private string _text = string.Empty;
    private string? _errorMessage;
    private bool _isReviewing;
    private bool _suppressTextChanged;

    public InPlaceSegmentViewModel(
        ContinuousDictationSegment segment,
        bool isReviewing,
        Action<Guid, string>? onTextChanged)
    {
        Id = segment.Id;
        _onTextChanged = onTextChanged;
        UpdateFrom(segment, isReviewing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public ContinuousSegmentState State => _state;

    public string Text
    {
        get => _state == ContinuousSegmentState.Completed ? _text : string.Empty;
        set
        {
            if (!IsEditable || _text == value)
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
            if (!_suppressTextChanged)
            {
                _onTextChanged?.Invoke(Id, value);
            }
        }
    }

    public bool IsEditable => _isReviewing && _state == ContinuousSegmentState.Completed;

    public bool ShowPlaceholder => _state != ContinuousSegmentState.Completed;

    public bool IsFailed => _state == ContinuousSegmentState.Failed;

    public string StateLabel => _state switch
    {
        ContinuousSegmentState.Transcribing => "识别中",
        ContinuousSegmentState.Completed => "已完成",
        ContinuousSegmentState.Failed => "识别失败",
        _ => string.Empty
    };

    public string Placeholder => _state switch
    {
        ContinuousSegmentState.Transcribing => "正在识别...",
        ContinuousSegmentState.Failed => string.IsNullOrWhiteSpace(_errorMessage)
            ? "识别失败"
            : $"识别失败：{_errorMessage}",
        _ => string.Empty
    };

    public void UpdateFrom(ContinuousDictationSegment segment, bool isReviewing)
    {
        _suppressTextChanged = true;
        try
        {
            _state = segment.State;
            _text = segment.Text;
            _errorMessage = segment.ErrorMessage;
            _isReviewing = isReviewing;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(StateLabel));
            OnPropertyChanged(nameof(Placeholder));
        }
        finally
        {
            _suppressTextChanged = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

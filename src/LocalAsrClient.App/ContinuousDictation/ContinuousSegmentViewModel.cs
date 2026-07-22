using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ContinuousDictation;

public sealed class ContinuousSegmentViewModel : INotifyPropertyChanged
{
    private readonly Action<Guid, string>? _onTextChanged;
    private ContinuousSegmentState _state;
    private string _text = string.Empty;
    private string? _errorMessage;
    private bool _suppressTextChanged;

    public ContinuousSegmentViewModel(ContinuousDictationSegment segment, Action<Guid, string>? onTextChanged)
    {
        Id = segment.Id;
        _onTextChanged = onTextChanged;
        UpdateFrom(segment, suppressTextChanged: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public ContinuousSegmentState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(ShowPlaceholder));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(Placeholder));
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(StateLabel));
        }
    }

    public string Text
    {
        get => IsEditable ? _text : string.Empty;
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

    public bool IsEditable => State == ContinuousSegmentState.Completed;

    public bool ShowPlaceholder => !IsEditable;

    public bool IsFailed => State == ContinuousSegmentState.Failed;

    public string StateLabel => State switch
    {
        ContinuousSegmentState.WaitingInput => "等待输入",
        ContinuousSegmentState.Transcribing => "识别中",
        ContinuousSegmentState.Completed => "已完成",
        ContinuousSegmentState.Failed => "识别失败",
        _ => string.Empty
    };

    public string Placeholder => State switch
    {
        ContinuousSegmentState.WaitingInput => ContinuousDictationStrings.PlaceholderWaiting,
        ContinuousSegmentState.Transcribing => ContinuousDictationStrings.PlaceholderTranscribing,
        ContinuousSegmentState.Failed => string.IsNullOrWhiteSpace(_errorMessage)
            ? ContinuousDictationStrings.PlaceholderFailedPrefix
            : $"{ContinuousDictationStrings.PlaceholderFailedPrefix}：{_errorMessage}",
        _ => string.Empty
    };

    public void UpdateFrom(ContinuousDictationSegment segment, bool suppressTextChanged = false)
    {
        _suppressTextChanged = suppressTextChanged;
        try
        {
            State = segment.State;
            _errorMessage = segment.ErrorMessage;
            _text = segment.Text;
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(Placeholder));
        }
        finally
        {
            _suppressTextChanged = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

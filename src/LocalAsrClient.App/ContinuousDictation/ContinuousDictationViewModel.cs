using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ContinuousDictation;

public sealed class ContinuousDictationViewModel : INotifyPropertyChanged
{
    private readonly ContinuousDictationSession _session;
    private string _bannerMessage = string.Empty;
    private bool _isRecordingActive;

    public ContinuousDictationViewModel(
        ContinuousDictationSession session,
        Action onClose,
        Action onEndRecording)
    {
        _session = session;
        CloseCommand = new RelayCommand(onClose);
        EndRecordingCommand = new RelayCommand(onEndRecording);
        CopyCommand = new RelayCommand(OnCopy);
    }

    public event Action? ScrollToBottomRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContinuousSegmentViewModel> Segments { get; } = new();

    public string HeaderText => $"连续听写模式 ({CompletedCount}/{TotalCount})";

    public int CompletedCount { get; private set; }

    public int TotalCount { get; private set; }

    public bool IsRecordingActive
    {
        get => _isRecordingActive;
        private set
        {
            if (_isRecordingActive == value)
            {
                return;
            }

            _isRecordingActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordingStatusText));
        }
    }

    public string RecordingStatusText => IsRecordingActive ? "正在录音" : "已暂停";

    public string BannerMessage
    {
        get => _bannerMessage;
        private set
        {
            if (_bannerMessage == value)
            {
                return;
            }

            _bannerMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBanner));
        }
    }

    public bool HasBanner => !string.IsNullOrWhiteSpace(BannerMessage);

    public ICommand CloseCommand { get; }

    public ICommand EndRecordingCommand { get; }

    public ICommand CopyCommand { get; }

    public void ApplySnapshot(ContinuousDictationSnapshot snapshot)
    {
        CompletedCount = snapshot.CompletedCount;
        TotalCount = snapshot.TotalCount;
        IsRecordingActive = snapshot.IsRecordingActive;
        OnPropertyChanged(nameof(HeaderText));

        if (snapshot.BannerMessage is not null)
        {
            BannerMessage = snapshot.BannerMessage;
        }

        var previousCount = Segments.Count;
        var existingMap = Segments.ToDictionary(segment => segment.Id);
        Segments.Clear();
        foreach (var segment in snapshot.Segments)
        {
            if (existingMap.TryGetValue(segment.Id, out var viewModel))
            {
                viewModel.UpdateFrom(segment);
                Segments.Add(viewModel);
            }
            else
            {
                Segments.Add(new ContinuousSegmentViewModel(segment, OnSegmentTextChanged));
            }
        }

        if (snapshot.Segments.Count > previousCount)
        {
            ScrollToBottomRequested?.Invoke();
        }
    }

    public void SetBanner(string? message)
    {
        BannerMessage = message ?? string.Empty;
    }

    public void Clear()
    {
        Segments.Clear();
        CompletedCount = 0;
        TotalCount = 0;
        IsRecordingActive = false;
        BannerMessage = string.Empty;
        OnPropertyChanged(nameof(HeaderText));
    }

    private void OnSegmentTextChanged(Guid segmentId, string text)
    {
        _session.UpdateSegmentText(segmentId, text);
    }

    private void OnCopy()
    {
        var text = _session.BuildHistoryText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

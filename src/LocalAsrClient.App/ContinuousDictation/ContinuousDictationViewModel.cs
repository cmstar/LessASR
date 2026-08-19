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
    private readonly Action<Guid, string> _updateSegmentText;
    private readonly Func<string> _buildHistoryText;
    private string _bannerMessage = string.Empty;
    private bool _isRecordingActive;

    public ContinuousDictationViewModel(
        ContinuousDictationSession session,
        Action onClose,
        Action onEndRecording,
        bool isDemoMode = false)
        : this(
            session.UpdateSegmentText,
            session.BuildHistoryText,
            onClose,
            onEndRecording,
            isDemoMode)
    {
    }

    internal ContinuousDictationViewModel(
        Action<Guid, string> updateSegmentText,
        Func<string> buildHistoryText,
        Action onClose,
        Action onEndRecording,
        bool isDemoMode = false)
    {
        _updateSegmentText = updateSegmentText;
        _buildHistoryText = buildHistoryText;
        CloseCommand = new RelayCommand(onClose);
        EndRecordingCommand = new RelayCommand(onEndRecording);
        CopyCommand = new RelayCommand(OnCopy);
        IsDemoMode = isDemoMode;
    }

    public event Action? ScrollToBottomRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContinuousSegmentViewModel> Segments { get; } = new();

    public bool IsDemoMode { get; }

    public string HeaderText => $"独立听写 {CompletedCount}/{TotalCount}";

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
        for (var targetIndex = 0; targetIndex < snapshot.Segments.Count; targetIndex++)
        {
            var segment = snapshot.Segments[targetIndex];
            var currentIndex = FindSegmentIndex(segment.Id, targetIndex);
            if (currentIndex < 0)
            {
                Segments.Insert(targetIndex, new ContinuousSegmentViewModel(segment, OnSegmentTextChanged));
            }
            else
            {
                if (currentIndex != targetIndex)
                {
                    Segments.Move(currentIndex, targetIndex);
                }

                Segments[targetIndex].UpdateFrom(segment);
            }
        }

        while (Segments.Count > snapshot.Segments.Count)
        {
            Segments.RemoveAt(Segments.Count - 1);
        }

        if (snapshot.Segments.Count > previousCount)
        {
            ScrollToBottomRequested?.Invoke();
        }
    }

    private int FindSegmentIndex(Guid segmentId, int startIndex)
    {
        for (var index = startIndex; index < Segments.Count; index++)
        {
            if (Segments[index].Id == segmentId)
            {
                return index;
            }
        }

        return -1;
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
        _updateSegmentText(segmentId, text);
    }

    private void OnCopy()
    {
        var text = _buildHistoryText();
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

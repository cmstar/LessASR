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

    public ContinuousDictationViewModel(
        ContinuousDictationSession session,
        Action onTerminate,
        Action onEndRecording)
    {
        _session = session;
        TerminateCommand = new RelayCommand(onTerminate);
        EndRecordingCommand = new RelayCommand(onEndRecording);
        CopyCommand = new RelayCommand(OnCopy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContinuousSegmentViewModel> Segments { get; } = new();

    public string HeaderText => $"连续听写模式 ({CompletedCount}/{TotalCount})";

    public int CompletedCount { get; private set; }

    public int TotalCount { get; private set; }

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

    public ICommand TerminateCommand { get; }

    public ICommand EndRecordingCommand { get; }

    public ICommand CopyCommand { get; }

    public void ApplySnapshot(ContinuousDictationSnapshot snapshot)
    {
        CompletedCount = snapshot.CompletedCount;
        TotalCount = snapshot.TotalCount;
        OnPropertyChanged(nameof(HeaderText));

        if (snapshot.BannerMessage is not null)
        {
            BannerMessage = snapshot.BannerMessage;
        }

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

using System.ComponentModel;
using System.Windows;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.App.ContinuousDictation;

public sealed class ContinuousDictationCoordinator : IDisposable
{
    private readonly ContinuousDictationSession _session;
    private readonly ITextHistoryRepository _historyRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IClock _clock;
    private readonly IAsrBackend _asrBackend;
    private readonly bool _isDemoMode;
    private ContinuousDictationWindow? _window;
    private ContinuousDictationViewModel? _viewModel;
    private bool _isClosing;

    public ContinuousDictationCoordinator(
        ContinuousDictationSession session,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock,
        IAsrBackend asrBackend,
        bool isDemoMode = false)
    {
        _session = session;
        _historyRepository = historyRepository;
        _settingsStore = settingsStore;
        _clock = clock;
        _asrBackend = asrBackend;
        _isDemoMode = isDemoMode;
        _session.Changed += OnSessionChanged;
    }

    public bool IsWindowOpen => _window is { IsLoaded: true };

    public ContinuousDictationWindow? CurrentWindow => IsWindowOpen ? _window : null;

    public void HandleF9()
    {
        _ = ShowAndToggleRecordingAsync();
    }

    public void HandleRightControl()
    {
        if (!IsWindowOpen)
        {
            return;
        }

        _ = RunSessionAsync(session => session.CommitSegmentBoundaryAsync(CancellationToken.None));
    }

    public async Task ShowAndToggleRecordingAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(EnsureWindowVisible);
        await RunF9Async();
    }

    public Task CommitSegmentBoundaryAsync() =>
        RunSessionAsync(session => session.CommitSegmentBoundaryAsync(CancellationToken.None));

    public void HandleEscape()
    {
        if (!IsWindowOpen || !_session.IsRecordingActive)
        {
            return;
        }

        _ = RunSessionAsync(session => session.CancelCurrentSegmentAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        System.Windows.Application.Current?.Dispatcher.Invoke(DisposeWindow);
    }

    private void EnsureWindowVisible()
    {
        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return;
        }

        _window = null;
        _viewModel = null;
        _isClosing = false;

        _viewModel = new ContinuousDictationViewModel(
            _session,
            RequestClose,
            () => _ = RunSessionAsync(session => session.CancelCurrentSegmentAsync(CancellationToken.None)),
            _isDemoMode);

        _window = new ContinuousDictationWindow(_viewModel);
        _window.Closing += OnWindowClosing;
        _window.Closed += OnWindowClosed;
        _window.Show();
        _window.Activate();
    }

    private void RequestClose()
    {
        _window?.Close();
    }

    private async Task RunF9Async()
    {
        try
        {
            if (_asrBackend.Status != AsrBackendStatus.Ready)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => _viewModel?.SetBanner(ContinuousDictationStrings.ModelLoading));

                await _asrBackend.EnsureReadyAsync(CancellationToken.None);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => _viewModel?.SetBanner(null));
            }

            await _session.ToggleRecordingAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "F9 连续听写处理失败", showDialog: false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => _viewModel?.SetBanner($"{ContinuousDictationStrings.PlaceholderFailedPrefix}：{ex.Message}"));
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        var history = _session.BuildHistory();
        _ = PersistHistoryAndTerminateAsync(history);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            _window.Closed -= OnWindowClosed;
        }

        _window = null;
        _viewModel = null;
        _isClosing = false;
    }

    private void DisposeWindow()
    {
        if (_window is null)
        {
            return;
        }

        _isClosing = true;
        _window.Closing -= OnWindowClosing;
        _window.Closed -= OnWindowClosed;
        _window.Close();
        _window = null;
        _viewModel = null;
        _isClosing = false;
        _ = _session.TerminateAsync(CancellationToken.None);
    }

    private async Task PersistHistoryAndTerminateAsync(ContinuousDictationHistory history)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(history.Text))
            {
                await WriteHistoryAsync(history, CancellationToken.None);
            }

            await _session.TerminateAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "连续听写关窗处理失败", showDialog: false);
        }
    }

    private void OnSessionChanged(ContinuousDictationSnapshot snapshot)
    {
        if (_window is null || _isClosing)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => _viewModel?.ApplySnapshot(snapshot));
    }

    private async Task RunSessionAsync(Func<ContinuousDictationSession, Task> action)
    {
        try
        {
            await action(_session);
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "连续听写会话操作失败", showDialog: false);
        }
    }

    private async Task WriteHistoryAsync(
        ContinuousDictationHistory history,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.TranscriptRetentionPolicy == TranscriptRetentionPolicy.Disabled)
        {
            return;
        }

        var characterCount = TextMetrics.CountCharacters(history.Text);
        var wordCount = TextMetrics.CountWords(history.Text);
        await _historyRepository.AddAsync(
            new TextHistoryEntry(
                Guid.NewGuid(),
                _clock.Now,
                history.Text,
                characterCount,
                wordCount,
                TimeSpan.Zero,
                TimeSpan.Zero,
                history.BackendId,
                history.ModelId),
            cancellationToken);
        await _historyRepository.PruneAsync(_clock.Now, settings.TranscriptRetentionPolicy, cancellationToken);
    }
}

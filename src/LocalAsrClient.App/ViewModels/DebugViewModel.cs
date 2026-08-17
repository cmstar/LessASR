using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.ViewModels;

public sealed class DebugViewModel
{
    private readonly AppServices _services;

    public DebugViewModel(AppServices services)
    {
        _services = services;
        SampleText = "这是一段模拟的语音识别结果，用来测试浮窗宽度、换行和复制按钮。";
    }

    public string SampleText { get; set; }

    public ICommand ShowLoadingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.LoadingModel, "模型加载中..."));
    public ICommand ShowRecordingCommand => new RelayCommand(_services.OverlayWindow.ShowRecordingPreview);
    public ICommand ShowTranscribingCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Transcribing, "识别中"));
    public ICommand ShowInjectedCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Injected, "已注入"));
    public ICommand ShowCopyTextCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.ResultNeedsAction, "未找到可输入位置", SampleText));
    public ICommand ShowErrorCommand => new RelayCommand(() => _services.OverlayWindow.ShowOverlay(OverlayState.Error, "输入失败"));
    public ICommand HideCommand => new RelayCommand(_services.OverlayWindow.HideOverlay);
    public ICommand TestInjectionCommand => new RelayCommand(async () =>
    {
        if (_services.InPlaceOrchestrator.State == LocalAsrClient.Core.Dictation.InPlaceDictationState.Idle)
        {
            _services.InjectionTargetCapture.Capture();
        }

        await _services.InPlaceOrchestrator.ToggleAsync(CancellationToken.None);
    });

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

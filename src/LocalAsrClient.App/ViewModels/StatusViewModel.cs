using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ViewModels;

public sealed class StatusViewModel : INotifyPropertyChanged
{
    private string _currentState = "空闲";
    private string _currentModel = "未选择模型";
    private string _serviceState = "未启动";
    private string _hotkey = "右 Alt";
    private string _lastResult = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentState { get => _currentState; set { _currentState = value; OnPropertyChanged(); } }
    public string CurrentModel { get => _currentModel; set { _currentModel = value; OnPropertyChanged(); } }
    public string ServiceState { get => _serviceState; set { _serviceState = value; OnPropertyChanged(); } }
    public string Hotkey { get => _hotkey; set { _hotkey = value; OnPropertyChanged(); } }
    public string LastResult { get => _lastResult; set { _lastResult = value; OnPropertyChanged(); } }

    public void Apply(DictationStatus status)
    {
        CurrentState = status.Message;
        LastResult = status.ResultText ?? LastResult;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

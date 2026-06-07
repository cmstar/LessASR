namespace LocalAsrClient.Core.Abstractions;

public interface IHotkeyListener : IDisposable
{
    event Action? Triggered;
    bool IsRunning { get; }
    void Start();
    void Stop();
}

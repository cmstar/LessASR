using System.Collections.ObjectModel;

namespace LocalAsrClient.TestTarget.Diagnostics;

public sealed class TargetEventRecorder
{
    private long _sequenceId;

    public ObservableCollection<string> Lines { get; } = [];

    public void Record(string eventName, string details)
    {
        var id = Interlocked.Increment(ref _sequenceId);
        Lines.Add($"{id:000} {DateTime.Now:HH:mm:ss.fff} {eventName} {details}");
    }

    public void Clear()
    {
        Lines.Clear();
        _sequenceId = 0;
    }
}

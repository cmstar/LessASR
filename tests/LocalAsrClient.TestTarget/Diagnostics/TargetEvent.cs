namespace LocalAsrClient.TestTarget.Diagnostics;

public sealed record TargetEvent(long SequenceId, DateTimeOffset Timestamp, string EventName, string Details);

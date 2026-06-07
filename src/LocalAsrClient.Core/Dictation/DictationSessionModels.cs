namespace LocalAsrClient.Core.Dictation;

public sealed record DictationStatus(
    DictationState State,
    string Message,
    string? ResultText = null,
    string? ErrorMessage = null);

public sealed record RecordingResult(
    byte[] WavData,
    TimeSpan Duration,
    int SampleRate,
    int Channels);
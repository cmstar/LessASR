namespace LocalAsrClient.Core.Asr;

public sealed record WhisperServerOptions(
    string ServerExecutablePath,
    string ModelPath,
    string Host,
    int Port)
{
    public Uri BaseUri => new($"http://{Host}:{Port}");
}

public enum WhisperServerStatus
{
    Stopped,
    Starting,
    Ready,
    Transcribing,
    Failed
}

public enum AsrBackendStatus
{
    Stopped,
    Starting,
    Ready,
    Transcribing,
    Failed
}

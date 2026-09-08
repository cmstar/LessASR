namespace LocalAsrClient.Core.Asr;

public abstract record AudioInput(string Format, int SampleRate, int Channels);

public sealed record InMemoryAudioInput(
    byte[] Data,
    string Format,
    int SampleRate,
    int Channels) : AudioInput(Format, SampleRate, Channels);

public sealed record FileAudioInput(
    string Path,
    string Format,
    int SampleRate,
    int Channels) : AudioInput(Format, SampleRate, Channels);

// 词汇提示与语言行文示例分开传递，后端的词汇表开关只控制 InitialPrompt。
public sealed record AsrRequest(
    AudioInput Audio,
    string? Language,
    IReadOnlyDictionary<string, string> Options,
    string? InitialPrompt = null,
    string? LanguageStylePrompt = null);

public sealed record AsrResult(
    string Text,
    TimeSpan? AudioDuration,
    TimeSpan? ProcessingDuration,
    double? Confidence);

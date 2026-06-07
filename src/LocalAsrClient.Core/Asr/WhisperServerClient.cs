using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerClient
{
    Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, CancellationToken cancellationToken);
}

public sealed class WhisperServerClient : IWhisperServerClient
{
    private readonly HttpClient _httpClient;

    public WhisperServerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AsrResult> TranscribeAsync(InMemoryAudioInput audio, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.Data);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "dictation.wav");
        content.Add(new StringContent("json"), "response_format");

        using var response = await _httpClient.PostAsync("/v1/audio/transcriptions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        stopwatch.Stop();
        return new AsrResult(text, null, stopwatch.Elapsed, null);
    }
}

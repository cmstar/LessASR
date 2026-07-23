using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerClient
{
    Task<AsrResult> TranscribeAsync(
        InMemoryAudioInput audio,
        string? language,
        string? initialPrompt,
        CancellationToken cancellationToken);
}

public sealed class WhisperServerClient : IWhisperServerClient
{
    private const string InferencePath = "/inference";
    public static readonly TimeSpan TranscribeTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;

    public WhisperServerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AsrResult> TranscribeAsync(
        InMemoryAudioInput audio,
        string? language,
        string? initialPrompt,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.Data);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "dictation.wav");
        content.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(language))
        {
            content.Add(new StringContent(language), "language");
        }
        if (!string.IsNullOrWhiteSpace(initialPrompt))
        {
            content.Add(new StringContent(initialPrompt), "prompt");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TranscribeTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, InferencePath) { Content = content };
        request.Headers.ConnectionClose = true;
        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"whisper-server 转写失败：{(int)response.StatusCode} {response.ReasonPhrase}。{Truncate(body, 200)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var text = document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;

        stopwatch.Stop();
        return new AsrResult(text, null, stopwatch.Elapsed, null);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}

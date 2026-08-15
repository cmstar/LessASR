using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LocalAsrClient.Core.Asr;

public interface IOpenAiCompatibleTranscriptionClient
{
    Task<AsrResult> TranscribeAsync(
        Uri endpoint,
        string model,
        string? apiKey,
        InMemoryAudioInput audio,
        string? language,
        string? prompt,
        CancellationToken cancellationToken);
}

public sealed class OpenAiCompatibleTranscriptionClient : IOpenAiCompatibleTranscriptionClient
{
    private const int MaxErrorBodyLength = 500;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleTranscriptionClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AsrResult> TranscribeAsync(
        Uri endpoint,
        string model,
        string? apiKey,
        InMemoryAudioInput audio,
        string? language,
        string? prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _ = RemoteEndpointPolicy.ParseAndValidate(endpoint.AbsoluteUri);
        var normalizedModel = model.Trim();
        if (normalizedModel.Length == 0)
        {
            throw new InvalidOperationException("远程 API 模型名称不能为空。");
        }

        var normalizedApiKey = apiKey?.Trim();
        var stopwatch = Stopwatch.StartNew();
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.Data);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "dictation.wav");
        content.Add(new StringContent(normalizedModel), "model");
        content.Add(new StringContent("json"), "response_format");
        if (!string.IsNullOrWhiteSpace(language))
        {
            content.Add(new StringContent(language), "language");
        }
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            content.Add(new StringContent(prompt), "prompt");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(normalizedApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"远程 API 转写失败：{(int)response.StatusCode} {response.ReasonPhrase}。{RedactAndTruncate(body, normalizedApiKey)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("远程 API 响应缺少字符串类型的 text 字段。");
        }

        stopwatch.Stop();
        return new AsrResult(
            textElement.GetString() ?? string.Empty,
            AudioDuration: null,
            ProcessingDuration: stopwatch.Elapsed,
            Confidence: null);
    }

    private static string RedactAndTruncate(string value, string? apiKey)
    {
        var redacted = !string.IsNullOrWhiteSpace(apiKey)
            ? value.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal)
            : value;
        return redacted.Length <= MaxErrorBodyLength
            ? redacted
            : redacted[..MaxErrorBodyLength];
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class OpenAiCompatibleTranscriptionClientTests
{
    [Fact]
    public async Task TranscribeAsync_SendsExactEndpointAndStandardMultipartFields()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"text\":\"你好\"}"));
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleTranscriptionClient(httpClient);
        var endpoint = new Uri("https://api.example.com/custom/transcriptions?tenant=one");
        var audio = new InMemoryAudioInput([1, 2, 3, 4], "wav", 16000, 1);

        var result = await client.TranscribeAsync(
            endpoint,
            "whisper-large-v3",
            "  sk-secret  ",
            audio,
            "zh",
            "LessASR, 专业词汇",
            CancellationToken.None);

        Assert.Equal("你好", result.Text);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(endpoint, handler.RequestUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("sk-secret", handler.Authorization?.Parameter);
        Assert.Equal("whisper-large-v3", handler.Fields["model"]);
        Assert.Equal("json", handler.Fields["response_format"]);
        Assert.Equal("zh", handler.Fields["language"]);
        Assert.Equal("LessASR, 专业词汇", handler.Fields["prompt"]);
        Assert.Equal([1, 2, 3, 4], handler.FileBytes);
        Assert.Equal("dictation.wav", handler.FileName);
        Assert.Equal("audio/wav", handler.FileMediaType);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TranscribeAsync_OmitsAuthorizationAndOptionalFieldsWhenBlank()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{\"text\":\"\"}"));
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleTranscriptionClient(httpClient);

        var result = await client.TranscribeAsync(
            new Uri("http://192.168.1.8:8080/v1/audio/transcriptions"),
            "local-whisper",
            "   ",
            new InMemoryAudioInput([7, 8], "wav", 16000, 1),
            language: null,
            prompt: null,
            CancellationToken.None);

        Assert.Equal(string.Empty, result.Text);
        Assert.Null(handler.Authorization);
        Assert.DoesNotContain("language", handler.Fields.Keys);
        Assert.DoesNotContain("prompt", handler.Fields.Keys);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task TranscribeAsync_RedactsApiKeyFromFailureAndDoesNotRetry()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("upstream rejected sk-sensitive-value", Encoding.UTF8, "text/plain")
        });
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleTranscriptionClient(httpClient);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.TranscribeAsync(
            new Uri("https://api.example.com/v1/audio/transcriptions"),
            "whisper-1",
            "sk-sensitive-value",
            new InMemoryAudioInput([1], "wav", 16000, 1),
            language: null,
            prompt: null,
            CancellationToken.None));

        Assert.DoesNotContain("sk-sensitive-value", error.Message, StringComparison.Ordinal);
        Assert.Contains("500", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.CallCount);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public Dictionary<string, string> Fields { get; } = [];

        public byte[] FileBytes { get; private set; } = [];

        public string? FileName { get; private set; }

        public string? FileMediaType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (var part in multipart)
            {
                var disposition = part.Headers.ContentDisposition;
                var name = disposition?.Name?.Trim('"');
                if (name == "file")
                {
                    FileBytes = await part.ReadAsByteArrayAsync(cancellationToken);
                    FileName = disposition?.FileName?.Trim('"');
                    FileMediaType = part.Headers.ContentType?.MediaType;
                }
                else if (!string.IsNullOrEmpty(name))
                {
                    Fields[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }

            return _responseFactory(request);
        }
    }
}

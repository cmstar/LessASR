using System.Net;
using System.Text;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerBackendTests
{
    [Theory]
    [InlineData("你好，世界")]
    [InlineData("先检查 Whisper.cpp，\n再测试 OpenAI API。\n没有标点的一段\n")]
    public async Task Client_ParsesOpenAiCompatibleTextResponse(string text)
    {
        var handler = new StubHttpHandler(System.Text.Json.JsonSerializer.Serialize(new { text }));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        var result = await client.TranscribeAsync(new InMemoryAudioInput(
            Data: Encoding.UTF8.GetBytes("fake wav"),
            Format: "wav",
            SampleRate: 16000,
            Channels: 1), language: null, initialPrompt: null, CancellationToken.None);

        Assert.Equal(text, result.Text);
        Assert.Equal("/inference", handler.LastRequestPath);
    }

    [Fact]
    public async Task Client_DisablesTokenTimestamps_ToAvoidServerForcedWrapping()
    {
        var handler = new StubHttpHandler("""{"text":"测试文本"}""");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        await client.TranscribeAsync(
            new InMemoryAudioInput(Encoding.UTF8.GetBytes("fake wav"), "wav", 16000, 1),
            "zh", initialPrompt: null, CancellationToken.None);

        Assert.Equal("json", handler.LastRequestFields["response_format"]);
        Assert.True(handler.LastRequestFields.TryGetValue("token_timestamps", out var tokenTimestamps));
        Assert.Equal("false", tokenTimestamps);
        Assert.DoesNotContain("no_timestamps", handler.LastRequestFields.Keys);
    }

    [Fact]
    public async Task Client_SendsLanguageField_WhenProvidedInRequest()
    {
        var handler = new StubHttpHandler("""{"text":"你好"}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        await client.TranscribeAsync(new InMemoryAudioInput(
            Encoding.UTF8.GetBytes("fake"), "wav", 16000, 1), "zh", initialPrompt: null, CancellationToken.None);

        Assert.Contains("language", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\r\nzh\r\n", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_OmitsPromptField()
    {
        var handler = new StubHttpHandler("""{"text":"你好"}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        await client.TranscribeAsync(new InMemoryAudioInput(
            Encoding.UTF8.GetBytes("fake"), "wav", 16000, 1), "zh", initialPrompt: null, CancellationToken.None);

        Assert.DoesNotContain("prompt", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_SendsPromptField_WhenProvidedInRequest()
    {
        var handler = new StubHttpHandler("""{"text":"你好"}""");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:8080")
        };
        var client = new WhisperServerClient(httpClient);

        await client.TranscribeAsync(
            new InMemoryAudioInput(Encoding.UTF8.GetBytes("fake"), "wav", 16000, 1),
            "zh",
            "初音ミク, Kubernetes, 大语言模型, LessASR",
            CancellationToken.None);

        Assert.Contains("name=prompt", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("初音ミク, Kubernetes, 大语言模型, LessASR", handler.LastRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backend_EnsuresServerBeforeTranscription()
    {
        var manager = new StubWhisperServerManager();
        var client = new StubWhisperServerClient("测试文本");
        var backend = new ManagedWhisperServerBackend(manager, client);

        var result = await backend.TranscribeAsync(new AsrRequest(
            new InMemoryAudioInput(Array.Empty<byte>(), "wav", 16000, 1),
            Language: "zh",
            Options: new Dictionary<string, string>(),
            InitialPrompt: "大语言模型, LessASR"), CancellationToken.None);

        Assert.True(manager.Started);
        Assert.Equal("测试文本", result.Text);
        Assert.Equal("大语言模型, LessASR", client.LastInitialPrompt);
    }

    [Theory]
    [InlineData(null, "以下是普通话的句子。")]
    [InlineData("专业词汇, LessASR", "专业词汇, LessASR\n以下是普通话的句子。")]
    public async Task Backend_IncludesLanguageStyleWithOrWithoutVocabulary(string? vocabulary, string expected)
    {
        var client = new StubWhisperServerClient("测试文本");
        var backend = new ManagedWhisperServerBackend(new StubWhisperServerManager(), client);

        await backend.TranscribeAsync(new AsrRequest(
            new InMemoryAudioInput([], "wav", 16000, 1), "zh", new Dictionary<string, string>(),
            InitialPrompt: vocabulary, LanguageStylePrompt: "以下是普通话的句子。"), CancellationToken.None);

        Assert.Equal(expected, client.LastInitialPrompt);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHttpHandler(string body)
        {
            _body = body;
        }

        public string? LastRequestPath { get; private set; }
        public string LastRequestBody { get; private set; } = "";
        public Dictionary<string, string> LastRequestFields { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.AbsolutePath;
            foreach (var part in Assert.IsType<MultipartFormDataContent>(request.Content))
            {
                var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                LastRequestFields[name] = await part.ReadAsStringAsync(cancellationToken);
            }
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            LastRequestBody = content;
            Assert.Contains("form-data", request.Content.Headers.ContentType!.MediaType);
            Assert.NotEmpty(content);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubWhisperServerManager : IWhisperServerManager
    {
        public event Action<WhisperServerStatus>? StatusChanged;
        public bool Started { get; private set; }
        public WhisperServerStatus Status { get; private set; } = WhisperServerStatus.Stopped;
        public Uri BaseUri => new("http://127.0.0.1:8080");

        public void UpdateOptions(WhisperServerOptions options)
        {
        }

        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            Started = true;
            Status = WhisperServerStatus.Ready;
            StatusChanged?.Invoke(Status);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Status = WhisperServerStatus.Stopped;
            StatusChanged?.Invoke(Status);
            return Task.CompletedTask;
        }

        public Task HealthCheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubWhisperServerClient : IWhisperServerClient
    {
        private readonly string _text;

        public StubWhisperServerClient(string text)
        {
            _text = text;
        }

        public string? LastInitialPrompt { get; private set; }

        public Task<AsrResult> TranscribeAsync(
            InMemoryAudioInput audio,
            string? language,
            string? initialPrompt,
            CancellationToken cancellationToken)
        {
            LastInitialPrompt = initialPrompt;
            return Task.FromResult(new AsrResult(_text, null, TimeSpan.FromMilliseconds(50), null));
        }
    }
}

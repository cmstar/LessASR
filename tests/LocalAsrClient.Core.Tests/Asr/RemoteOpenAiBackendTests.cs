using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class RemoteOpenAiBackendTests
{
    [Fact]
    public async Task TranscribeAsync_WhenVocabularyIsDisabled_DropsPromptAndUsesDecryptedKey()
    {
        var client = new StubClient();
        var protector = new StubSecretProtector("plain-key");
        var backend = new RemoteOpenAiBackend(
            Profile(useVocabulary: false, protectedApiKey: "protected-key"),
            protector,
            client);

        await backend.TranscribeAsync(Request("用户的专业词汇"), CancellationToken.None);

        Assert.Null(client.Prompt);
        Assert.Equal("plain-key", client.ApiKey);
        Assert.Equal("protected-key", protector.LastProtectedValue);
    }

    [Fact]
    public async Task TranscribeAsync_WhenVocabularyIsEnabled_ForwardsPrompt()
    {
        var client = new StubClient();
        var backend = new RemoteOpenAiBackend(
            Profile(useVocabulary: true, protectedApiKey: null),
            new StubSecretProtector("unused"),
            client);

        await backend.TranscribeAsync(Request("LessASR, 专业词汇"), CancellationToken.None);

        Assert.Equal("LessASR, 专业词汇", client.Prompt);
        Assert.Null(client.ApiKey);
    }

    [Fact]
    public async Task EnsureReadyAsync_RejectsInvalidEndpointWithoutSendingRequest()
    {
        var client = new StubClient();
        var profile = Profile(useVocabulary: false, protectedApiKey: null) with
        {
            Endpoint = "http://example.com/v1/audio/transcriptions"
        };
        var backend = new RemoteOpenAiBackend(profile, new StubSecretProtector("unused"), client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.EnsureReadyAsync(CancellationToken.None));

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenSavedKeyCannotBeDecrypted_AsksUserToReenterIt()
    {
        var client = new StubClient();
        var backend = new RemoteOpenAiBackend(
            Profile(useVocabulary: false, protectedApiKey: "unavailable"),
            new ThrowingSecretProtector(),
            client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.EnsureReadyAsync(CancellationToken.None));

        Assert.Contains("重新输入", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.CallCount);
    }

    private static RemoteApiProfile Profile(bool useVocabulary, string? protectedApiKey) => new(
        Guid.NewGuid(),
        "远程服务",
        "https://api.example.com/v1/audio/transcriptions",
        "whisper-1",
        protectedApiKey,
        useVocabulary,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static AsrRequest Request(string prompt) => new(
        new InMemoryAudioInput([1, 2], "wav", 16000, 1),
        Language: "zh",
        Options: new Dictionary<string, string>(),
        InitialPrompt: prompt);

    private sealed class StubSecretProtector : ISecretProtector
    {
        private readonly string _plaintext;

        public StubSecretProtector(string plaintext)
        {
            _plaintext = plaintext;
        }

        public string? LastProtectedValue { get; private set; }

        public string Protect(string plaintext) => throw new NotSupportedException();

        public string Unprotect(string protectedValue)
        {
            LastProtectedValue = protectedValue;
            return _plaintext;
        }
    }

    private sealed class StubClient : IOpenAiCompatibleTranscriptionClient
    {
        public int CallCount { get; private set; }

        public string? ApiKey { get; private set; }

        public string? Prompt { get; private set; }

        public Task<AsrResult> TranscribeAsync(
            Uri endpoint,
            string model,
            string? apiKey,
            InMemoryAudioInput audio,
            string? language,
            string? prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ApiKey = apiKey;
            Prompt = prompt;
            return Task.FromResult(new AsrResult("识别结果", null, TimeSpan.Zero, null));
        }
    }

    private sealed class ThrowingSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => throw new NotSupportedException();

        public string Unprotect(string protectedValue) =>
            throw new InvalidOperationException("decrypt failed");
    }
}

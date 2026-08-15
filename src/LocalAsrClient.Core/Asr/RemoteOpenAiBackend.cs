using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Asr;

public sealed class RemoteOpenAiBackend : IAsrBackend
{
    private readonly RemoteApiProfile _profile;
    private readonly ISecretProtector _secretProtector;
    private readonly IOpenAiCompatibleTranscriptionClient _client;

    public RemoteOpenAiBackend(
        RemoteApiProfile profile,
        ISecretProtector secretProtector,
        IOpenAiCompatibleTranscriptionClient client)
    {
        _profile = profile;
        _secretProtector = secretProtector;
        _client = client;
    }

    public string Name => _profile.Name;

    public AsrBackendStatus Status => AsrBackendStatus.Ready;

    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        _ = GetValidatedConfiguration();
        return Task.CompletedTask;
    }

    public async Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        if (request.Audio is not InMemoryAudioInput audio)
        {
            throw new NotSupportedException("远程 API 仅支持内存音频输入。");
        }

        var configuration = GetValidatedConfiguration();
        return await _client.TranscribeAsync(
            configuration.Endpoint,
            configuration.Model,
            configuration.ApiKey,
            audio,
            request.Language,
            _profile.UseVocabulary ? request.InitialPrompt : null,
            cancellationToken);
    }

    private ValidatedConfiguration GetValidatedConfiguration()
    {
        var endpoint = RemoteEndpointPolicy.ParseAndValidate(_profile.Endpoint);
        var model = _profile.Model.Trim();
        if (model.Length == 0)
        {
            throw new InvalidOperationException("远程 API 模型名称不能为空。");
        }

        var apiKey = string.IsNullOrWhiteSpace(_profile.ProtectedApiKey)
            ? null
            : _secretProtector.Unprotect(_profile.ProtectedApiKey);
        return new ValidatedConfiguration(endpoint, model, apiKey);
    }

    private sealed record ValidatedConfiguration(Uri Endpoint, string Model, string? ApiKey);
}

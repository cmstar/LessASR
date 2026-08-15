using System.Buffers.Binary;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Asr;

public enum ApiKeyUpdateMode
{
    Retain,
    Replace,
    Clear
}

public sealed record RemoteApiProfileInput(
    string Name,
    string Endpoint,
    string Model,
    bool UseVocabulary,
    string? ApiKey);

public interface IAsrServiceCoordinator
{
    Task<IReadOnlyList<RemoteApiProfile>> GetRemoteProfilesAsync(CancellationToken cancellationToken);
    Task<RemoteApiProfile> CreateRemoteAsync(RemoteApiProfileInput input, CancellationToken cancellationToken);
    Task UpdateRemoteAsync(Guid id, RemoteApiProfileInput input, ApiKeyUpdateMode apiKeyUpdateMode, CancellationToken cancellationToken);
    Task DeleteRemoteAsync(Guid id, CancellationToken cancellationToken);
    Task ActivateRemoteAsync(Guid id, CancellationToken cancellationToken);
    Task ActivateLocalAsync(CancellationToken cancellationToken);
    Task<AsrResult> TestRemoteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class AsrServiceCoordinator : IAsrServiceCoordinator
{
    private readonly IRemoteApiProfileRepository _repository;
    private readonly ISettingsStore _settingsStore;
    private readonly IVocabularyRepository _vocabularyRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IWhisperServerManager _localManager;
    private readonly SwitchableAsrBackend _router;
    private readonly IAsrBackend _localBackend;
    private readonly Func<RemoteApiProfile, IAsrBackend> _remoteBackendFactory;
    private readonly Func<bool> _isDictationBusy;
    private readonly AsrActivityGate _activityGate;
    private readonly Action _refreshLocalClient;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    public AsrServiceCoordinator(
        IRemoteApiProfileRepository repository,
        ISettingsStore settingsStore,
        IVocabularyRepository vocabularyRepository,
        ISecretProtector secretProtector,
        IWhisperServerManager localManager,
        SwitchableAsrBackend router,
        IAsrBackend localBackend,
        Func<RemoteApiProfile, IAsrBackend> remoteBackendFactory,
        Func<bool> isDictationBusy,
        AsrActivityGate activityGate,
        Action refreshLocalClient)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _vocabularyRepository = vocabularyRepository;
        _secretProtector = secretProtector;
        _localManager = localManager;
        _router = router;
        _localBackend = localBackend;
        _remoteBackendFactory = remoteBackendFactory;
        _isDictationBusy = isDictationBusy;
        _activityGate = activityGate;
        _refreshLocalClient = refreshLocalClient;
    }

    public async Task<IReadOnlyList<RemoteApiProfile>> GetRemoteProfilesAsync(
        CancellationToken cancellationToken)
    {
        var profiles = await _repository.GetAllAsync(cancellationToken);
        return profiles.Select(ToClientProfile).ToArray();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.ActiveRemoteApiProfileId is not Guid activeId)
        {
            _router.Replace(_localBackend);
            return;
        }

        var profile = await _repository.GetByIdAsync(activeId, cancellationToken);
        if (profile is null)
        {
            await _settingsStore.UpdateAsync(
                current => current with { ActiveRemoteApiProfileId = null },
                cancellationToken);
            _router.Replace(_localBackend);
            return;
        }

        _router.Replace(_remoteBackendFactory(profile));
    }

    public async Task<RemoteApiProfile> CreateRemoteAsync(
        RemoteApiProfileInput input,
        CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        ValidateInput(input);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await using var activityLease = await AcquireMutationLeaseAsync(cancellationToken);
            var protectedApiKey = ProtectOptional(input.ApiKey);
            var profile = await _repository.CreateAsync(
                input.Name,
                input.Endpoint,
                input.Model,
                protectedApiKey,
                input.UseVocabulary,
                cancellationToken);
            return ToClientProfile(profile);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task UpdateRemoteAsync(
        Guid id,
        RemoteApiProfileInput input,
        ApiKeyUpdateMode apiKeyUpdateMode,
        CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        ValidateInput(input);
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await using var activityLease = await AcquireMutationLeaseAsync(cancellationToken);
            var existing = await GetRequiredProfileAsync(id, cancellationToken);
            var protectedApiKey = apiKeyUpdateMode switch
            {
                ApiKeyUpdateMode.Retain => existing.ProtectedApiKey,
                ApiKeyUpdateMode.Clear => null,
                ApiKeyUpdateMode.Replace => ProtectRequired(input.ApiKey),
                _ => throw new ArgumentOutOfRangeException(nameof(apiKeyUpdateMode))
            };

            await _repository.UpdateAsync(
                id,
                input.Name,
                input.Endpoint,
                input.Model,
                protectedApiKey,
                input.UseVocabulary,
                cancellationToken);

            var settings = await _settingsStore.LoadAsync(cancellationToken);
            if (settings.ActiveRemoteApiProfileId == id)
            {
                _router.Replace(_remoteBackendFactory(
                    await GetRequiredProfileAsync(id, cancellationToken)));
            }
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task DeleteRemoteAsync(Guid id, CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await using var activityLease = await AcquireMutationLeaseAsync(cancellationToken);
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            if (settings.ActiveRemoteApiProfileId == id)
            {
                throw new InvalidOperationException("正在使用的远程 API 配置不能删除，请先切换服务。");
            }

            await _repository.DeleteAsync(id, cancellationToken);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task ActivateRemoteAsync(Guid id, CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await using var activityLease = await AcquireMutationLeaseAsync(cancellationToken);
            var profile = await GetRequiredProfileAsync(id, cancellationToken);
            var remoteBackend = _remoteBackendFactory(profile);
            await remoteBackend.EnsureReadyAsync(cancellationToken);

            var settings = await _settingsStore.LoadAsync(cancellationToken);
            if (settings.ActiveRemoteApiProfileId is null)
            {
                await _localManager.StopAsync(cancellationToken);
                _refreshLocalClient();
            }

            await _settingsStore.UpdateAsync(
                current => current with { ActiveRemoteApiProfileId = id },
                cancellationToken);
            _router.Replace(remoteBackend);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task ActivateLocalAsync(CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        await _mutationLock.WaitAsync(cancellationToken);
        try
        {
            await using var activityLease = await AcquireMutationLeaseAsync(cancellationToken);
            await _settingsStore.UpdateAsync(
                current => current with { ActiveRemoteApiProfileId = null },
                cancellationToken);
            _router.Replace(_localBackend);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<AsrResult> TestRemoteAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredProfileAsync(id, cancellationToken);
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var language = TranscriptionLanguageCatalog.ResolveLanguage(
            settings.PreferredTranscriptionLanguageId);
        var activeVocabulary = profile.UseVocabulary
            ? await _vocabularyRepository.GetActiveAsync(cancellationToken)
            : null;
        var initialPrompt = WhisperVocabulary.CreateInitialPrompt(activeVocabulary?.EntriesText);
        var backend = _remoteBackendFactory(profile);
        await backend.EnsureReadyAsync(cancellationToken);
        return await backend.TranscribeAsync(
            new AsrRequest(
                new InMemoryAudioInput(CreateSilentWav(), "wav", 16000, 1),
                Language: language,
                Options: new Dictionary<string, string>(),
                InitialPrompt: initialPrompt),
            cancellationToken);
    }

    private async Task<RemoteApiProfile> GetRequiredProfileAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(id, cancellationToken)
        ?? throw new KeyNotFoundException("找不到远程 API 配置。");

    private void ThrowIfBusy()
    {
        if (_isDictationBusy())
        {
            throw new InvalidOperationException("听写进行中，暂不能更改服务配置。");
        }
    }

    private async ValueTask<AsrActivityLease> AcquireMutationLeaseAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfBusy();
        var lease = await _activityGate.TryEnterAsync(cancellationToken);
        if (lease is null)
        {
            throw new InvalidOperationException(_isDictationBusy()
                ? "听写进行中，暂不能更改服务配置。"
                : "另一项服务操作正在进行，请稍后再试。");
        }

        return lease;
    }

    private static void ValidateInput(RemoteApiProfileInput input)
    {
        _ = RemoteEndpointPolicy.ParseAndValidate(input.Endpoint);
        if (string.IsNullOrWhiteSpace(input.Model))
        {
            throw new InvalidOperationException("远程 API 模型名称不能为空。");
        }
    }

    private string? ProtectOptional(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? null : _secretProtector.Protect(apiKey.Trim());

    private string ProtectRequired(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("用于替换的 API Key 不能为空；如需移除请使用清除操作。");
        }

        return _secretProtector.Protect(apiKey.Trim());
    }

    private RemoteApiProfile ToClientProfile(RemoteApiProfile profile)
    {
        var availability = ApiKeyAvailability.NotConfigured;
        if (!string.IsNullOrWhiteSpace(profile.ProtectedApiKey))
        {
            try
            {
                _ = _secretProtector.Unprotect(profile.ProtectedApiKey);
                availability = ApiKeyAvailability.Available;
            }
            catch
            {
                availability = ApiKeyAvailability.Unavailable;
            }
        }

        return profile with
        {
            ProtectedApiKey = null,
            ApiKeyAvailability = availability
        };
    }

    private static byte[] CreateSilentWav()
    {
        const int sampleRate = 16000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate / 4;
        var dataLength = sampleCount * channels * (bitsPerSample / 8);
        var wav = new byte[44 + dataLength];

        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * channels * bitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)(channels * bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataLength);
        return wav;
    }
}

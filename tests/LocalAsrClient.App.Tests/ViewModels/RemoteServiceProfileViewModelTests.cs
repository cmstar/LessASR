using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class RemoteServiceProfileViewModelTests
{
    [Fact]
    public async Task SaveAsync_WithBlankKey_RetainsExistingProtectedKey()
    {
        ApiKeyUpdateMode? capturedMode = null;
        var profile = CreateProfile(protectedApiKey: "ciphertext");
        var viewModel = CreateViewModel(profile, onSave: (_, _, mode) =>
        {
            capturedMode = mode;
            return Task.FromResult(profile);
        });

        await viewModel.SaveAsync("");

        Assert.Equal(ApiKeyUpdateMode.Retain, capturedMode);
        Assert.Equal("已配置 · 基于系统 DPAPI 保存", viewModel.ApiKeyPlaceholderText);
    }

    [Fact]
    public async Task SaveAsync_WithEnteredKey_RequestsReplacementWithoutRetainingPlaintext()
    {
        string? capturedKey = null;
        ApiKeyUpdateMode? capturedMode = null;
        var profile = CreateProfile();
        var saved = profile with { ProtectedApiKey = "ciphertext" };
        var viewModel = CreateViewModel(profile, onSave: (_, key, mode) =>
        {
            capturedKey = key;
            capturedMode = mode;
            return Task.FromResult(saved);
        });

        await viewModel.SaveAsync(" secret-key ");

        Assert.Equal("secret-key", capturedKey);
        Assert.Equal(ApiKeyUpdateMode.Replace, capturedMode);
        Assert.DoesNotContain("secret-key", viewModel.ApiKeyPlaceholderText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearApiKeyAsync_UsesExplicitClearMode()
    {
        var profile = CreateProfile(protectedApiKey: "ciphertext");
        var clearedId = Guid.Empty;
        var viewModel = CreateViewModel(
            profile,
            onClearApiKey: id =>
            {
                clearedId = id;
                return Task.CompletedTask;
            });

        await viewModel.ClearApiKeyAsync();

        Assert.Equal(profile.Id, clearedId);
        Assert.Equal("可为空", viewModel.ApiKeyPlaceholderText);
    }

    [Fact]
    public void EditingSavedFields_DisablesTestAndActivationUntilSaved()
    {
        var viewModel = CreateViewModel(CreateProfile());

        viewModel.Endpoint = "https://draft.example/v1/audio/transcriptions";

        Assert.False(viewModel.CanTest);
        Assert.False(viewModel.CanActivate);
    }

    [Fact]
    public void EditingProxy_IsTrackedAndDiscardRestoresTheSavedAddress()
    {
        var profile = CreateProfile() with
        {
            ProxyUrl = "http://127.0.0.1:7890/"
        };
        var viewModel = CreateViewModel(profile);

        viewModel.ProxyUrl = "socks5://127.0.0.1:1080";

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.CanTest);
        Assert.False(viewModel.CanActivate);

        viewModel.DiscardChanges();

        Assert.Equal(profile.ProxyUrl, viewModel.ProxyUrl);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task ClearApiKeyAsync_DoesNotPersistOrDiscardUnsavedFields()
    {
        var clearCallCount = 0;
        var profile = CreateProfile(protectedApiKey: "ciphertext");
        var viewModel = CreateViewModel(
            profile,
            onClearApiKey: _ =>
            {
                clearCallCount++;
                return Task.CompletedTask;
            });
        viewModel.Endpoint = "https://draft.example/v1/audio/transcriptions";

        await viewModel.ClearApiKeyAsync();

        Assert.Equal(1, clearCallCount);
        Assert.Equal("https://draft.example/v1/audio/transcriptions", viewModel.Endpoint);
        Assert.False(viewModel.HasApiKey);
    }

    [Fact]
    public void DiscardChanges_RestoresTheLastSavedSnapshot()
    {
        var profile = CreateProfile();
        var viewModel = CreateViewModel(profile);
        viewModel.Name = "Draft name";
        viewModel.Endpoint = "https://draft.example/v1/audio/transcriptions";
        viewModel.Model = "draft-model";
        viewModel.UseVocabulary = !profile.UseVocabulary;
        viewModel.SetApiKeyDraftPresent(true);

        viewModel.DiscardChanges();

        Assert.Equal(profile.Name, viewModel.Name);
        Assert.Equal(profile.Endpoint, viewModel.Endpoint);
        Assert.Equal(profile.Model, viewModel.Model);
        Assert.Equal(profile.UseVocabulary, viewModel.UseVocabulary);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.True(viewModel.CanTest);
    }

    [Fact]
    public async Task TestAsync_ShowsProgressAndEditingInvalidatesTheSuccessfulResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(
            CreateProfile(),
            onTest: async _ =>
            {
                started.SetResult();
                await release.Task;
                return new AsrResult(string.Empty, null, null, null);
            });

        var test = viewModel.TestAsync();
        await started.Task;

        Assert.Equal("正在测试…", viewModel.LastMessage);

        release.SetResult();
        await test;
        Assert.Equal("测试通过。", viewModel.LastMessage);

        viewModel.Model = "draft-model";

        Assert.Equal("", viewModel.LastMessage);
        Assert.False(viewModel.CanTest);
    }

    [Fact]
    public async Task EnteringAnApiKeyDraft_DisablesSavedProfileActionsAndInvalidatesTheTestResult()
    {
        var viewModel = CreateViewModel(CreateProfile());
        await viewModel.TestAsync();
        Assert.Equal("测试通过。", viewModel.LastMessage);

        viewModel.SetApiKeyDraftPresent(true);

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.CanTest);
        Assert.False(viewModel.CanActivate);
        Assert.Equal("", viewModel.LastMessage);
    }

    [Fact]
    public async Task TestAsync_WhenRequestFails_ReplacesProgressWithTheError()
    {
        var viewModel = CreateViewModel(
            CreateProfile(),
            onTest: _ => throw new InvalidOperationException("认证失败"));

        await viewModel.TestAsync();

        Assert.Equal("", viewModel.LastMessage);
        Assert.Equal("认证失败", viewModel.LastError);
    }

    [Fact]
    public void HttpEndpoint_ShowsUnencryptedTransportWarning()
    {
        var profile = CreateProfile() with
        {
            Endpoint = "http://192.168.1.12:9000/v1/audio/transcriptions"
        };

        var viewModel = CreateViewModel(profile);

        Assert.True(viewModel.IsHttpEndpoint);
        Assert.Contains("未加密", viewModel.EndpointWarningText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableApiKey_AsksForReentryAndDisablesSavedProfileActions()
    {
        var profile = CreateProfile(protectedApiKey: "ciphertext") with
        {
            ApiKeyAvailability = ApiKeyAvailability.Unavailable
        };

        var viewModel = CreateViewModel(profile);

        Assert.Contains("重新输入", viewModel.ApiKeyPlaceholderText, StringComparison.Ordinal);
        Assert.False(viewModel.CanTest);
        Assert.False(viewModel.CanActivate);
        Assert.True(viewModel.CanClearApiKey);
    }

    [Fact]
    public void NewProfile_DefaultsVocabularyToDisabled()
    {
        var viewModel = CreateViewModel(profile: null);

        Assert.True(viewModel.IsNew);
        Assert.False(viewModel.UseVocabulary);
        Assert.Equal("OpenAI 兼容 API", viewModel.ProviderTypeText);
    }

    private static RemoteServiceProfileViewModel CreateViewModel(
        RemoteApiProfile? profile,
        Func<RemoteServiceProfileViewModel, string?, ApiKeyUpdateMode, Task<RemoteApiProfile>>? onSave = null,
        Func<Guid, Task<AsrResult>>? onTest = null,
        Func<Guid, Task>? onClearApiKey = null) =>
        new(
            profile,
            isActive: false,
            onSave ?? ((_, _, _) => Task.FromResult(profile ?? CreateProfile())),
            _ => Task.CompletedTask,
            onTest ?? (_ => Task.FromResult(new AsrResult(string.Empty, null, null, null))),
            _ => Task.CompletedTask,
            onClearApiKey ?? (_ => Task.CompletedTask),
            action => action(),
            _ => Task.CompletedTask);

    private static RemoteApiProfile CreateProfile(string? protectedApiKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new RemoteApiProfile(
            Guid.NewGuid(),
            "Office API",
            "https://api.example/v1/audio/transcriptions",
            "whisper-1",
            protectedApiKey,
            false,
            now,
            now);
    }
}

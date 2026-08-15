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
        Assert.Equal("已配置 · 基于系统 DPAPI 保存", viewModel.ApiKeyStatusText);
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
        Assert.DoesNotContain("secret-key", viewModel.ApiKeyStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClearApiKeyAsync_UsesExplicitClearMode()
    {
        ApiKeyUpdateMode? capturedMode = null;
        var profile = CreateProfile(protectedApiKey: "ciphertext");
        var viewModel = CreateViewModel(profile, onSave: (_, _, mode) =>
        {
            capturedMode = mode;
            return Task.FromResult(profile with { ProtectedApiKey = null });
        });

        await viewModel.ClearApiKeyAsync();

        Assert.Equal(ApiKeyUpdateMode.Clear, capturedMode);
        Assert.Equal("未配置 · API Key 可为空", viewModel.ApiKeyStatusText);
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
    public void NewProfile_DefaultsVocabularyToDisabled()
    {
        var viewModel = CreateViewModel(profile: null);

        Assert.True(viewModel.IsNew);
        Assert.False(viewModel.UseVocabulary);
        Assert.Equal("OpenAI 兼容 API", viewModel.ProviderTypeText);
    }

    private static RemoteServiceProfileViewModel CreateViewModel(
        RemoteApiProfile? profile,
        Func<RemoteServiceProfileViewModel, string?, ApiKeyUpdateMode, Task<RemoteApiProfile>>? onSave = null) =>
        new(
            profile,
            isActive: false,
            onSave ?? ((_, _, _) => Task.FromResult(profile ?? CreateProfile())),
            _ => Task.CompletedTask,
            _ => Task.FromResult(new AsrResult(string.Empty, null, null, null)),
            _ => Task.CompletedTask,
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

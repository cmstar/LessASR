using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class RemoteServiceProfileViewModel : INotifyPropertyChanged
{
    private readonly Func<RemoteServiceProfileViewModel, string?, ApiKeyUpdateMode, Task<RemoteApiProfile>> _save;
    private readonly Func<Guid, Task> _activate;
    private readonly Func<Guid, Task<AsrResult>> _test;
    private readonly Func<Guid, Task> _delete;
    private readonly Func<Guid, Task> _clearApiKey;
    private readonly Func<Func<Task>, Task> _runPageOperation;
    private readonly Func<RemoteServiceProfileViewModel, Task> _discard;
    private Guid? _id;
    private string _name;
    private string _endpoint;
    private string _model;
    private bool _useVocabulary;
    private ApiKeyAvailability _apiKeyAvailability;
    private bool _hasApiKeyDraft;
    private bool _isActive;
    private bool _isOperationInProgress;
    private bool _isInteractionLocked;
    private string _lastMessage = "";
    private string _lastError = "";
    private string _savedName;
    private string _savedEndpoint;
    private string _savedModel;
    private bool _savedUseVocabulary;

    public RemoteServiceProfileViewModel(
        RemoteApiProfile? profile,
        bool isActive,
        Func<RemoteServiceProfileViewModel, string?, ApiKeyUpdateMode, Task<RemoteApiProfile>> save,
        Func<Guid, Task> activate,
        Func<Guid, Task<AsrResult>> test,
        Func<Guid, Task> delete,
        Func<Guid, Task> clearApiKey,
        Func<Func<Task>, Task> runPageOperation,
        Func<RemoteServiceProfileViewModel, Task>? discard = null)
    {
        _save = save;
        _activate = activate;
        _test = test;
        _delete = delete;
        _clearApiKey = clearApiKey;
        _runPageOperation = runPageOperation;
        _discard = discard ?? (_ => Task.CompletedTask);
        _id = profile?.Id;
        _name = profile?.Name ?? "";
        _endpoint = profile?.Endpoint ?? "";
        _model = profile?.Model ?? "whisper-1";
        _useVocabulary = profile?.UseVocabulary ?? false;
        _apiKeyAvailability = profile?.ApiKeyAvailability ?? ApiKeyAvailability.NotConfigured;
        _isActive = isActive;
        _savedName = _name;
        _savedEndpoint = _endpoint;
        _savedModel = _model;
        _savedUseVocabulary = _useVocabulary;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid? Id => _id;
    public bool IsNew => _id is null;
    public string ProviderTypeText => "OpenAI 兼容 API";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "新远程 API" : Name;

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Endpoint
    {
        get => _endpoint;
        set
        {
            if (SetField(ref _endpoint, value))
            {
                OnPropertyChanged(nameof(IsHttpEndpoint));
                OnPropertyChanged(nameof(EndpointWarningText));
            }
        }
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public bool UseVocabulary
    {
        get => _useVocabulary;
        set => SetField(ref _useVocabulary, value);
    }

    public bool HasApiKey => _apiKeyAvailability != ApiKeyAvailability.NotConfigured;
    public string ApiKeyStatusText => _apiKeyAvailability switch
    {
        ApiKeyAvailability.Available => "已配置 · 基于系统 DPAPI 保存",
        ApiKeyAvailability.Unavailable => "需要重新输入 API Key · 当前 Windows 用户无法解密",
        _ => ""
    };

    public bool IsActive => _isActive;
    public bool IsOperationInProgress => _isOperationInProgress;
    public bool HasUnsavedChanges => !string.Equals(Name, _savedName, StringComparison.Ordinal)
        || !string.Equals(Endpoint, _savedEndpoint, StringComparison.Ordinal)
        || !string.Equals(Model, _savedModel, StringComparison.Ordinal)
        || UseVocabulary != _savedUseVocabulary
        || _hasApiKeyDraft;
    public bool CanMutate => !_isOperationInProgress && !_isInteractionLocked;
    public bool CanActivate => CanMutate && !IsActive && !IsNew && !HasUnsavedChanges
        && _apiKeyAvailability != ApiKeyAvailability.Unavailable;
    public bool CanTest => CanMutate && !IsNew && !HasUnsavedChanges
        && _apiKeyAvailability != ApiKeyAvailability.Unavailable;
    public bool CanDelete => CanMutate && !IsActive;
    public bool CanClearApiKey => CanMutate && HasApiKey && !IsNew;
    public bool ShowDiscardChanges => !IsNew && HasUnsavedChanges;
    public bool CanDiscardChanges => CanMutate && ShowDiscardChanges;
    public bool IsHttpEndpoint => Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    public string EndpointWarningText => IsHttpEndpoint
        ? "此地址使用 HTTP，音频发送至 API 时为未加密传输。仅建议用于可信的本机或局域网服务。"
        : "";
    public string LastMessage => _lastMessage;
    public string LastError => _lastError;

    public async Task<bool> SaveAsync(string? enteredApiKey)
    {
        var normalizedKey = enteredApiKey?.Trim();
        var mode = string.IsNullOrEmpty(normalizedKey)
            ? ApiKeyUpdateMode.Retain
            : ApiKeyUpdateMode.Replace;
        return await RunAsync(async () =>
        {
            var saved = await _save(this, normalizedKey, mode);
            ApplySavedProfile(saved);
            SetMessage("配置已保存。");
        });
    }

    public async Task ClearApiKeyAsync() => _ = await RunRequiredIdAsync(async id =>
    {
        await _clearApiKey(id);
        _apiKeyAvailability = ApiKeyAvailability.NotConfigured;
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(ApiKeyStatusText));
        RaiseAvailabilityChanged();
        SetMessage("API Key 已清除。");
    });

    public async Task ActivateAsync() => _ = await RunRequiredIdAsync(async id =>
    {
        await _activate(id);
        SetMessage("已启用该模型。");
    });

    public async Task TestAsync()
    {
        if (!CanTest)
        {
            SetError(HasUnsavedChanges ? "请先保存当前修改，再测试配置。" : "请先保存配置。");
            return;
        }

        _ = await RunRequiredIdAsync(async id =>
        {
            SetMessage("正在测试…");
            _ = await _test(id);
            SetMessage("测试通过。");
        });
    }

    public async Task DeleteAsync()
    {
        if (IsNew)
        {
            _ = await RunAsync(() => _discard(this));
            return;
        }

        _ = await RunRequiredIdAsync(id => _delete(id));
    }

    public void SetActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        OnPropertyChanged(nameof(IsActive));
        RaiseAvailabilityChanged();
    }

    public void SetInteractionLocked(bool isLocked)
    {
        if (_isInteractionLocked == isLocked)
        {
            return;
        }

        _isInteractionLocked = isLocked;
        RaiseAvailabilityChanged();
    }

    public void SetApiKeyDraftPresent(bool isPresent)
    {
        if (_hasApiKeyDraft == isPresent)
        {
            return;
        }

        _hasApiKeyDraft = isPresent;
        if (isPresent)
        {
            SetMessage("");
            SetError("");
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowDiscardChanges));
        RaiseAvailabilityChanged();
    }

    public void DiscardChanges()
    {
        if (!CanDiscardChanges)
        {
            return;
        }

        _name = _savedName;
        _endpoint = _savedEndpoint;
        _model = _savedModel;
        _useVocabulary = _savedUseVocabulary;
        _hasApiKeyDraft = false;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(UseVocabulary));
        OnPropertyChanged(nameof(IsHttpEndpoint));
        OnPropertyChanged(nameof(EndpointWarningText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowDiscardChanges));
        SetError("");
        SetMessage("已放弃未保存修改。");
        RaiseAvailabilityChanged();
    }

    private async Task<bool> RunRequiredIdAsync(Func<Guid, Task> action)
    {
        if (_id is not Guid id)
        {
            SetError("请先保存配置。");
            return false;
        }

        return await RunAsync(() => action(id));
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        if (!CanMutate)
        {
            return false;
        }

        SetOperationInProgress(true);
        SetError("");
        SetMessage("");
        try
        {
            await _runPageOperation(action);
            return true;
        }
        catch (Exception ex)
        {
            SetMessage("");
            SetError(ex.Message);
            return false;
        }
        finally
        {
            SetOperationInProgress(false);
        }
    }

    private void ApplySavedProfile(RemoteApiProfile profile)
    {
        _id = profile.Id;
        _name = profile.Name;
        _endpoint = profile.Endpoint;
        _model = profile.Model;
        _useVocabulary = profile.UseVocabulary;
        _apiKeyAvailability = profile.ApiKeyAvailability;
        _savedName = profile.Name;
        _savedEndpoint = profile.Endpoint;
        _savedModel = profile.Model;
        _savedUseVocabulary = profile.UseVocabulary;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(UseVocabulary));
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(ApiKeyStatusText));
        OnPropertyChanged(nameof(IsHttpEndpoint));
        OnPropertyChanged(nameof(EndpointWarningText));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowDiscardChanges));
        RaiseAvailabilityChanged();
    }

    private void SetOperationInProgress(bool value)
    {
        _isOperationInProgress = value;
        OnPropertyChanged(nameof(IsOperationInProgress));
        RaiseAvailabilityChanged();
    }

    private void SetMessage(string value)
    {
        _lastMessage = value;
        OnPropertyChanged(nameof(LastMessage));
    }

    private void SetError(string value)
    {
        _lastError = value;
        OnPropertyChanged(nameof(LastError));
    }

    private void RaiseAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanClearApiKey));
        OnPropertyChanged(nameof(ShowDiscardChanges));
        OnPropertyChanged(nameof(CanDiscardChanges));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        SetMessage("");
        SetError("");
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ShowDiscardChanges));
        RaiseAvailabilityChanged();
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

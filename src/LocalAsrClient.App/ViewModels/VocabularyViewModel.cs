using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed record VocabularyProfileListItem(
    Guid Id,
    string Name,
    int EntryCount,
    bool IsActive,
    bool IsSelected)
{
    public string EntryCountText => $"{EntryCount} 项";
}

public sealed class VocabularyViewModel : INotifyPropertyChanged
{
    private sealed record VocabularyDraft(string Name, string EntriesText);

    private readonly IVocabularyRepository _repository;
    private readonly Func<IReadOnlyList<string>, string?> _requestNewName;
    private readonly Func<VocabularyProfile, bool> _confirmDelete;
    private readonly Dictionary<Guid, VocabularyDraft> _drafts = [];
    private IReadOnlyList<VocabularyProfile> _loadedProfiles = [];
    private VocabularyProfile? _selectedProfile;
    private VocabularyProfile? _activeProfile;
    private string _selectedName = string.Empty;
    private string _vocabularyText = string.Empty;
    private string? _validationError;
    private int _entryCount;
    private bool _isBusy;

    public VocabularyViewModel(
        IVocabularyRepository repository,
        Func<IReadOnlyList<string>, string?>? requestNewName = null,
        Func<VocabularyProfile, bool>? confirmDelete = null)
    {
        _repository = repository;
        _requestNewName = requestNewName ?? (_ => null);
        _confirmDelete = confirmDelete ?? (_ => false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VocabularyProfileListItem> Profiles { get; } = [];

    public Guid? SelectedProfileId => _selectedProfile?.Id;

    public string SelectedName
    {
        get => _selectedName;
        set
        {
            if (SetField(ref _selectedName, value ?? string.Empty))
            {
                RefreshValidationAndEditorState();
                StoreSelectedDraft();
                RebuildProfileItems();
            }
        }
    }

    public string VocabularyText
    {
        get => _vocabularyText;
        set
        {
            if (SetField(ref _vocabularyText, value ?? string.Empty))
            {
                RefreshValidationAndEditorState();
                StoreSelectedDraft();
                RebuildProfileItems();
            }
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set => SetField(ref _validationError, value);
    }

    public int EntryCount
    {
        get => _entryCount;
        private set
        {
            if (SetField(ref _entryCount, value))
            {
                OnPropertyChanged(nameof(EntryCountText));
            }
        }
    }

    public string EntryCountText => $"{EntryCount} / {WhisperVocabulary.MaxEntries}";

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasSelection => _selectedProfile is not null;

    public bool HasActiveVocabulary => _activeProfile is not null;

    public string ActiveVocabularyName => _activeProfile?.Name ?? "未使用词汇表";

    public bool SelectedIsActive => _selectedProfile is not null
        && _selectedProfile.Id == _activeProfile?.Id;

    public bool HasUnsavedChanges => _selectedProfile is not null
        && (!string.Equals(SelectedName, _selectedProfile.Name, StringComparison.Ordinal)
            || !string.Equals(VocabularyText, _selectedProfile.EntriesText, StringComparison.Ordinal));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyActionState();
            }
        }
    }

    public bool CanSave => HasSelection
        && HasUnsavedChanges
        && string.IsNullOrEmpty(ValidationError)
        && !IsBusy;

    public bool CanDiscard => HasSelection && HasUnsavedChanges && !IsBusy;

    public bool CanActivate => HasSelection
        && !SelectedIsActive
        && !HasUnsavedChanges
        && string.IsNullOrEmpty(ValidationError)
        && !IsBusy;

    public bool CanDeactivate => HasActiveVocabulary && !IsBusy;

    public bool CanDelete => HasSelection && !IsBusy;

    public string LastUpdatedText => _selectedProfile is null
        ? string.Empty
        : $"上次更新：{_selectedProfile.UpdatedAt.ToLocalTime():HH:mm:ss}";

    public ICommand NewCommand => new AsyncRelayCommand(NewFromDialogAsync, "新建词汇表失败");

    public ICommand SelectCommand =>
        new AsyncRelayCommand<VocabularyProfileListItem>(SelectAsync, "切换词汇表编辑项失败");

    public ICommand SaveCommand => new AsyncRelayCommand(
        async () => await SaveAsync(),
        "保存词汇表失败");

    public ICommand DiscardCommand => new RelayCommand(DiscardChanges);

    public ICommand ActivateCommand => new AsyncRelayCommand(
        ActivateSelectedAsync,
        "启用词汇表失败");

    public ICommand DeactivateCommand => new AsyncRelayCommand(
        DeactivateAsync,
        "停用词汇表失败");

    public ICommand DeleteCommand => new AsyncRelayCommand(
        DeleteSelectedAsync,
        "删除词汇表失败");

    public async Task LoadAsync()
    {
        await ReloadAsync(preferredSelectedId: null);
    }

    public async Task<VocabularyProfile> CreateAsync(string name)
    {
        var result = VocabularyProfileName.Validate(name);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        var created = await _repository.CreateAsync(result.NormalizedName, CancellationToken.None);
        await ReloadAsync(created.Id);
        return created;
    }

    public Task SelectAsync(VocabularyProfileListItem item)
    {
        if (_selectedProfile?.Id == item.Id)
        {
            return Task.CompletedTask;
        }

        var profile = _loadedProfiles.SingleOrDefault(candidate => candidate.Id == item.Id);
        if (profile is not null)
        {
            ApplySelectedProfile(profile);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> SaveAsync()
    {
        if (!CanSave || _selectedProfile is null)
        {
            return !HasUnsavedChanges;
        }

        IsBusy = true;
        try
        {
            await _repository.UpdateAsync(
                _selectedProfile.Id,
                SelectedName,
                VocabularyText,
                CancellationToken.None);
            _drafts.Remove(_selectedProfile.Id);
            await ReloadAsync(_selectedProfile.Id);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void DiscardChanges()
    {
        if (_selectedProfile is not null)
        {
            _drafts.Remove(_selectedProfile.Id);
            ApplySelectedProfile(_selectedProfile);
        }
    }

    public async Task ActivateSelectedAsync()
    {
        if (!CanActivate || _selectedProfile is null)
        {
            return;
        }

        var selectedId = _selectedProfile.Id;
        IsBusy = true;
        try
        {
            await _repository.SetActiveAsync(selectedId, CancellationToken.None);
            await ReloadAsync(selectedId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeactivateAsync()
    {
        if (!CanDeactivate)
        {
            return;
        }

        var selectedId = _selectedProfile?.Id;
        IsBusy = true;
        try
        {
            await _repository.SetActiveAsync(null, CancellationToken.None);
            await ReloadAsync(selectedId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (!CanDelete || _selectedProfile is null || !_confirmDelete(_selectedProfile))
        {
            return;
        }

        var selectedIndex = _loadedProfiles
            .Select((profile, index) => (profile, index))
            .Single(pair => pair.profile.Id == _selectedProfile.Id)
            .index;
        var deletedId = _selectedProfile.Id;

        IsBusy = true;
        try
        {
            _drafts.Remove(deletedId);
            await _repository.DeleteAsync(deletedId, CancellationToken.None);
            var remaining = await _repository.GetAllAsync(CancellationToken.None);
            var nextId = remaining.Count == 0
                ? (Guid?)null
                : remaining[Math.Min(selectedIndex, remaining.Count - 1)].Id;
            ReloadFrom(remaining, nextId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NewFromDialogAsync()
    {
        var visibleNames = _loadedProfiles
            .Select(profile => _drafts.GetValueOrDefault(profile.Id)?.Name ?? profile.Name)
            .ToArray();
        var name = _requestNewName(visibleNames);
        if (name is not null)
        {
            await CreateAsync(name);
        }
    }

    private async Task ReloadAsync(Guid? preferredSelectedId)
    {
        var profiles = await _repository.GetAllAsync(CancellationToken.None);
        ReloadFrom(profiles, preferredSelectedId);
    }

    private void ReloadFrom(
        IReadOnlyList<VocabularyProfile> profiles,
        Guid? preferredSelectedId)
    {
        _loadedProfiles = profiles;
        _activeProfile = profiles.SingleOrDefault(profile => profile.IsActive);
        var profileIds = profiles.Select(profile => profile.Id).ToHashSet();
        foreach (var removedId in _drafts.Keys.Where(id => !profileIds.Contains(id)).ToArray())
        {
            _drafts.Remove(removedId);
        }

        var selected = preferredSelectedId is not null
            ? profiles.SingleOrDefault(profile => profile.Id == preferredSelectedId)
            : null;
        selected ??= _selectedProfile is not null
            ? profiles.SingleOrDefault(profile => profile.Id == _selectedProfile.Id)
            : null;
        selected ??= _activeProfile ?? profiles.FirstOrDefault();

        ApplySelectedProfile(selected);
        OnPropertyChanged(nameof(ActiveVocabularyName));
        OnPropertyChanged(nameof(HasActiveVocabulary));
        OnPropertyChanged(nameof(CanDeactivate));
    }

    private void ApplySelectedProfile(VocabularyProfile? profile)
    {
        _selectedProfile = profile;
        var draft = profile is not null && _drafts.TryGetValue(profile.Id, out var storedDraft)
            ? storedDraft
            : null;
        _selectedName = draft?.Name ?? profile?.Name ?? string.Empty;
        _vocabularyText = draft?.EntriesText ?? profile?.EntriesText ?? string.Empty;
        OnPropertyChanged(nameof(SelectedProfileId));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(VocabularyText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIsActive));
        OnPropertyChanged(nameof(LastUpdatedText));
        RebuildProfileItems();
        RefreshValidationAndEditorState();
    }

    private void RebuildProfileItems()
    {
        Profiles.Clear();
        foreach (var profile in _loadedProfiles)
        {
            var draft = _drafts.GetValueOrDefault(profile.Id);
            Profiles.Add(new VocabularyProfileListItem(
                profile.Id,
                draft?.Name ?? profile.Name,
                WhisperVocabulary.Parse(draft?.EntriesText ?? profile.EntriesText).Entries.Count,
                profile.IsActive,
                profile.Id == _selectedProfile?.Id));
        }

        OnPropertyChanged(nameof(HasProfiles));
    }

    private void RefreshValidationAndEditorState()
    {
        var nameResult = VocabularyProfileName.Validate(SelectedName);
        var duplicateName = nameResult.IsValid
            && _loadedProfiles.Any(profile =>
                profile.Id != _selectedProfile?.Id
                && string.Equals(
                    _drafts.GetValueOrDefault(profile.Id)?.Name ?? profile.Name,
                    nameResult.NormalizedName,
                    StringComparison.OrdinalIgnoreCase));
        var entriesResult = WhisperVocabulary.Parse(VocabularyText);
        EntryCount = entriesResult.Entries.Count;
        ValidationError = nameResult.ErrorMessage
            ?? (duplicateName ? "词汇表名称不能重复。" : null)
            ?? entriesResult.ErrorMessage;
        NotifyEditorState();
    }

    private void StoreSelectedDraft()
    {
        if (_selectedProfile is null)
        {
            return;
        }

        if (HasUnsavedChanges)
        {
            _drafts[_selectedProfile.Id] = new VocabularyDraft(SelectedName, VocabularyText);
        }
        else
        {
            _drafts.Remove(_selectedProfile.Id);
        }
    }

    private void NotifyEditorState()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanDelete));
    }

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanDelete));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

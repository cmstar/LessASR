using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.ViewModels;

public sealed class VocabularyViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _settingsStore;
    private string _vocabularyText = string.Empty;
    private string? _validationError;
    private int _entryCount;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private bool _isSaving;

    public VocabularyViewModel(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string VocabularyText
    {
        get => _vocabularyText;
        set
        {
            if (string.Equals(_vocabularyText, value, StringComparison.Ordinal))
            {
                return;
            }

            _vocabularyText = value ?? string.Empty;
            OnPropertyChanged();
            RefreshValidation();
            MarkDirty();
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set => SetStateField(ref _validationError, value);
    }

    public int EntryCount
    {
        get => _entryCount;
        private set
        {
            if (SetStateField(ref _entryCount, value))
            {
                OnPropertyChanged(nameof(EntryCountText));
            }
        }
    }

    public string EntryCountText => $"{EntryCount} / {WhisperVocabulary.MaxEntries}";

    public string LastSavedAtText { get; private set; } = string.Empty;

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetStateField(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetStateField(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool CanSave => HasUnsavedChanges
        && string.IsNullOrEmpty(ValidationError)
        && !IsSaving;

    public string SaveButtonText => IsSaving
        ? "正在保存…"
        : HasUnsavedChanges ? "保存词汇表" : "已保存";

    public ICommand SaveCommand => new AsyncRelayCommand(SaveAsync, "保存词汇表失败");

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            ApplyText(settings.VocabularyText);
            LastSavedAtText = string.Empty;
            OnPropertyChanged(nameof(LastSavedAtText));
        }
        finally
        {
            _isLoading = false;
            HasUnsavedChanges = false;
        }
    }

    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        var parsed = WhisperVocabulary.Parse(VocabularyText);
        if (!parsed.IsValid)
        {
            RefreshValidation();
            return;
        }

        IsSaving = true;
        try
        {
            var latestSettings = await _settingsStore.LoadAsync(CancellationToken.None);
            await _settingsStore.SaveAsync(
                latestSettings with { VocabularyText = parsed.NormalizedText },
                CancellationToken.None);

            _isLoading = true;
            try
            {
                ApplyText(parsed.NormalizedText);
            }
            finally
            {
                _isLoading = false;
            }

            LastSavedAtText = $"上次保存：{DateTime.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(LastSavedAtText));
            HasUnsavedChanges = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ApplyText(string text)
    {
        if (!string.Equals(_vocabularyText, text, StringComparison.Ordinal))
        {
            _vocabularyText = text;
            OnPropertyChanged(nameof(VocabularyText));
        }

        RefreshValidation();
    }

    private void RefreshValidation()
    {
        var parsed = WhisperVocabulary.Parse(_vocabularyText);
        EntryCount = parsed.Entries.Count;
        ValidationError = parsed.ErrorMessage;
        OnPropertyChanged(nameof(CanSave));
    }

    private void MarkDirty()
    {
        if (_isLoading)
        {
            return;
        }

        HasUnsavedChanges = true;
        if (!string.IsNullOrEmpty(LastSavedAtText))
        {
            LastSavedAtText = string.Empty;
            OnPropertyChanged(nameof(LastSavedAtText));
        }
    }

    private bool SetStateField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

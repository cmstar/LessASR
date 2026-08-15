using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Infrastructure;

namespace LocalAsrClient.App.ViewModels;

public enum MainSection
{
    Home,
    History,
    Stats,
    Services,
    Vocabulary,
    Settings,
    Diagnostics
}

public sealed class MainNavigationViewModel : INotifyPropertyChanged
{
    private MainSection _selectedSection = MainSection.Home;

    public MainNavigationViewModel()
    {
        NavigateCommand = new RelayCommand<MainSection>(section => SelectedSection = section);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand NavigateCommand { get; }

    public MainSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (_selectedSection == value)
            {
                return;
            }

            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHomeSelected));
            OnPropertyChanged(nameof(IsHistorySelected));
            OnPropertyChanged(nameof(IsStatsSelected));
            OnPropertyChanged(nameof(IsServicesSelected));
            OnPropertyChanged(nameof(IsVocabularySelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsDiagnosticsSelected));
        }
    }

    public bool IsHomeSelected => SelectedSection == MainSection.Home;

    public bool IsHistorySelected => SelectedSection == MainSection.History;

    public bool IsStatsSelected => SelectedSection == MainSection.Stats;

    public bool IsServicesSelected => SelectedSection == MainSection.Services;

    public bool IsVocabularySelected => SelectedSection == MainSection.Vocabulary;

    public bool IsSettingsSelected => SelectedSection == MainSection.Settings;

    public bool IsDiagnosticsSelected => SelectedSection == MainSection.Diagnostics;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

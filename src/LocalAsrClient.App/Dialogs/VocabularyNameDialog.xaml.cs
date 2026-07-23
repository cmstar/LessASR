using System.Windows;
using System.Windows.Controls;
using LocalAsrClient.Core.Persistence;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.Dialogs;

public partial class VocabularyNameDialog : FluentWindow
{
    private readonly HashSet<string> _existingNames;

    private VocabularyNameDialog(IReadOnlyList<string> existingNames)
    {
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string? Result { get; private set; }

    public static string? Prompt(Window? owner, IReadOnlyList<string> existingNames)
    {
        var dialog = new VocabularyNameDialog(existingNames);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshValidation();
    }

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        var validation = ValidateName();
        if (!validation.IsValid)
        {
            RefreshValidation();
            return;
        }

        Result = validation.NormalizedName;
        DialogResult = true;
    }

    private VocabularyProfileNameResult ValidateName()
    {
        var validation = VocabularyProfileName.Validate(NameTextBox.Text);
        return validation.IsValid && _existingNames.Contains(validation.NormalizedName)
            ? new VocabularyProfileNameResult(validation.NormalizedName, "词汇表名称不能重复。")
            : validation;
    }

    private void RefreshValidation()
    {
        var validation = ValidateName();
        CreateButton.IsEnabled = validation.IsValid;
        ErrorText.Text = validation.ErrorMessage ?? string.Empty;
        ErrorText.Visibility = validation.IsValid || string.IsNullOrEmpty(NameTextBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}

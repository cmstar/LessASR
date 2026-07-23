using System.Windows;
using LocalAsrClient.Core.Persistence;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.Dialogs;

public partial class DeleteHistoryConfirmationWindow : FluentWindow
{
    private const int PreviewCharacterLimit = 160;

    private DeleteHistoryConfirmationWindow(TextHistoryEntry entry)
    {
        InitializeComponent();
        HistoryPreview.Text = BuildPreview(entry.Text);
    }

    public static bool Confirm(Window? owner, TextHistoryEntry entry)
    {
        var dialog = new DeleteHistoryConfirmationWindow(entry);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    private static string BuildPreview(string text)
    {
        var normalized = text.Trim();
        return normalized.Length <= PreviewCharacterLimit
            ? normalized
            : $"{normalized[..PreviewCharacterLimit]}…";
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

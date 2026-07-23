using System.Windows;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.Dialogs;

public partial class ConfirmationDialog : FluentWindow
{
    private ConfirmationDialog(ConfirmationDialogOptions options)
    {
        InitializeComponent();
        DataContext = options;
        ApplyTone(options.Tone);
    }

    public static bool Confirm(Window? owner, ConfirmationDialogOptions options)
    {
        var dialog = new ConfirmationDialog(options);
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

    private void ApplyTone(ConfirmationDialogTone tone)
    {
        var destructive = tone == ConfirmationDialogTone.Destructive;
        IconContainer.Background = (System.Windows.Media.Brush)FindResource(
            destructive ? "LessAsr.Brush.ErrorSoft" : "LessAsr.Brush.AccentSoft");
        ConfirmationIcon.Foreground = (System.Windows.Media.Brush)FindResource(
            destructive ? "LessAsr.Brush.Error" : "LessAsr.Brush.Accent");
        ConfirmationIcon.Symbol = destructive
            ? SymbolRegular.Delete24
            : SymbolRegular.Question24;
        ConfirmButton.Style = (Style)FindResource(
            destructive ? "LessAsr.DestructivePrimaryButton" : "LessAsr.PrimaryButton");
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

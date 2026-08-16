using System.Windows;

namespace LocalAsrClient.App.Controls;

public partial class HelpLabel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(HelpLabel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
        nameof(HelpText),
        typeof(string),
        typeof(HelpLabel),
        new PropertyMetadata(string.Empty));

    public HelpLabel()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string HelpText
    {
        get => (string)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }
}

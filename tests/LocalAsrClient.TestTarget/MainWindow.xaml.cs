using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using LocalAsrClient.TestTarget.Controls;
using LocalAsrClient.TestTarget.Diagnostics;

namespace LocalAsrClient.TestTarget;

public partial class MainWindow : Window
{
    private readonly TargetEventRecorder _recorder = new();
    private readonly LoggingWinFormsTextBox _nativeTextBox;

    public MainWindow()
    {
        InitializeComponent();
        _nativeTextBox = new LoggingWinFormsTextBox(_recorder);
        NativeTextBoxHost.Child = _nativeTextBox;
        _recorder.Lines.CollectionChanged += (_, _) =>
        {
            ScreenLogTextBox.Text = string.Join(Environment.NewLine, _recorder.Lines);
            ScreenLogTextBox.ScrollToEnd();
        };
        Activated += (_, _) => _recorder.Record("Target.Window.Activated", string.Empty);
        Deactivated += (_, _) => _recorder.Record("Target.Window.Deactivated", string.Empty);
        Loaded += (_, _) => FocusNativeInput();
    }

    public string NativeText => _nativeTextBox.Text;

    protected override void OnClosing(CancelEventArgs e)
    {
        _recorder.Record("Target.Window.Closing", string.Empty);
        base.OnClosing(e);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _nativeTextBox.Clear();
        WpfTextBox.Clear();
        _recorder.Clear();
        FocusNativeInput();
    }

    private void FocusNativeButton_Click(object sender, RoutedEventArgs e)
    {
        FocusNativeInput();
    }

    private void FocusNativeInput()
    {
        _nativeTextBox.Focus();
        _recorder.Record("Target.NativeTextBox.FocusRequested", string.Empty);
    }

    private void Control_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.GotKeyboardFocus", string.Empty);
    }

    private void Control_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.LostKeyboardFocus", string.Empty);
    }

    private void WpfTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _recorder.Record("Target.WpfTextBox.TextChanged", $"length={WpfTextBox.Text.Length}");
    }
}

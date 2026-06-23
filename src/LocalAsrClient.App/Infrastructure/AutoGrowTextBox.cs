using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalAsrClient.App.Infrastructure;

public static class AutoGrowTextBox
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(AutoGrowTextBox),
        new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WpfTextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            textBox.TextChanged += OnTextBoxChanged;
            textBox.SizeChanged += OnTextBoxSizeChanged;
            textBox.Loaded += OnTextBoxChanged;
        }
        else
        {
            textBox.TextChanged -= OnTextBoxChanged;
            textBox.SizeChanged -= OnTextBoxSizeChanged;
            textBox.Loaded -= OnTextBoxChanged;
        }
    }

    private static void OnTextBoxChanged(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            AdjustHeight(textBox);
        }
    }

    private static void OnTextBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
        {
            AdjustHeight(textBox);
        }
    }

    private static void AdjustHeight(WpfTextBox textBox)
    {
        if (!textBox.IsLoaded || textBox.ActualWidth <= 0)
        {
            return;
        }

        var minHeight = textBox.MinHeight > 0 ? textBox.MinHeight : 28;
        var border = textBox.BorderThickness.Top + textBox.BorderThickness.Bottom;
        var padding = textBox.Padding.Top + textBox.Padding.Bottom;
        var contentHeight = minHeight - border - padding;

        if (!string.IsNullOrEmpty(textBox.Text))
        {
            var typeface = new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch);
            var pixelsPerDip = VisualTreeHelper.GetDpi(textBox).PixelsPerDip;
            var contentWidth = Math.Max(
                1,
                textBox.ActualWidth - textBox.Padding.Left - textBox.Padding.Right - border);

            var formatted = new FormattedText(
                textBox.Text,
                CultureInfo.CurrentUICulture,
                textBox.FlowDirection,
                typeface,
                textBox.FontSize,
                System.Windows.Media.Brushes.Black,
                pixelsPerDip)
            {
                MaxTextWidth = contentWidth
            };

            contentHeight = Math.Ceiling(formatted.Height);
        }

        var height = contentHeight + padding + border;
        textBox.Height = Math.Max(minHeight, height);
    }
}

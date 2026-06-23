using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalAsrClient.App.Infrastructure;

public static class ScrollViewerWheelForwarder
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable",
        typeof(bool),
        typeof(ScrollViewerWheelForwarder),
        new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var scrollViewer = FindAncestorScrollViewer(source);
        if (scrollViewer is null)
        {
            return;
        }

        e.Handled = true;
        var offset = scrollViewer.VerticalOffset - e.Delta;
        offset = Math.Max(0, Math.Min(offset, scrollViewer.ScrollableHeight));
        scrollViewer.ScrollToVerticalOffset(offset);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

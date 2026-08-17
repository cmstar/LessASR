using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LocalAsrClient.App.Overlay;

internal sealed class RecordingWaveform : FrameworkElement
{
    private const double PreferredBarSpacing = 4;

    public static readonly DependencyProperty WaveformBrushProperty = DependencyProperty.Register(
        nameof(WaveformBrush),
        typeof(Brush),
        typeof(RecordingWaveform),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly WaveformHistory _history = new();

    public RecordingWaveform()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public Brush WaveformBrush
    {
        get => (Brush)GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
    }

    public void PushLevel(float level)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    () => PushLevel(level));
            }

            return;
        }

        _history.Push(level);
        InvalidateVisual();
    }

    public void Reset()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, Reset);
            }

            return;
        }

        _history.Reset();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0 || WaveformBrush is null)
        {
            return;
        }

        var centerY = ActualHeight / 2;
        var left = 1d;
        var right = Math.Max(left, ActualWidth - 1);

        drawingContext.PushOpacity(0.42);
        drawingContext.DrawLine(
            CreatePen(WaveformBrush, 1),
            new Point(left, centerY),
            new Point(right, centerY));
        drawingContext.Pop();

        var samples = _history.Samples;
        var visibleBarCount = Math.Clamp(
            (int)Math.Floor((right - left) / PreferredBarSpacing) + 1,
            1,
            samples.Count);
        var firstVisibleIndex = samples.Count - visibleBarCount;
        var spacing = visibleBarCount > 1 ? (right - left) / (visibleBarCount - 1) : 0;
        var maximumHalfHeight = Math.Max(1, Math.Min(8, (ActualHeight - 2) / 2));
        var barPen = CreatePen(WaveformBrush, 1.5);

        for (var visibleIndex = 0; visibleIndex < visibleBarCount; visibleIndex++)
        {
            var level = samples[firstVisibleIndex + visibleIndex];
            if (level <= 0)
            {
                continue;
            }

            var halfHeight = 1 + (Math.Pow(level, 0.72) * (maximumHalfHeight - 1));
            var x = left + (visibleIndex * spacing);
            drawingContext.DrawLine(
                barPen,
                new Point(x, centerY - halfHeight),
                new Point(x, centerY + halfHeight));
        }
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }
}

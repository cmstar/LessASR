using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace LocalAsrClient.App.Overlay;

internal sealed class RecordingWaveform : FrameworkElement
{
    internal const int InputLevelsPerBar = 3;
    private const double PreferredBarSpacing = 4;
    private static readonly TimeSpan ScrollDuration = TimeSpan.FromMilliseconds(150);
    private static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        "ScrollOffset",
        typeof(double),
        typeof(RecordingWaveform),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WaveformBrushProperty = DependencyProperty.Register(
        nameof(WaveformBrush),
        typeof(Brush),
        typeof(RecordingWaveform),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly WaveformHistory _history = new();
    private int _pendingLevelCount;
    private float _pendingPeak;

    public RecordingWaveform()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = true;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                ResetScroll();
            }
        };
    }

    public Brush WaveformBrush
    {
        get => (Brush)GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
    }

    public void PushLevel(float level, bool animate = true)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    () => PushLevel(level, animate));
            }

            return;
        }

        // Combine three 50 ms audio updates into one bar without losing brief peaks.
        _pendingPeak = Math.Max(_pendingPeak, Math.Clamp(level, 0, 1));
        if (++_pendingLevelCount < InputLevelsPerBar)
        {
            return;
        }

        _history.Push(_pendingPeak);
        _pendingLevelCount = 0;
        _pendingPeak = 0;
        if (animate && IsVisible)
        {
            // Keep the existing bars at their current positions when history shifts,
            // then move continuously between audio updates at WPF's rendering cadence.
            var offset = (double)GetValue(ScrollOffsetProperty);
            BeginAnimation(ScrollOffsetProperty, null);
            SetValue(ScrollOffsetProperty, offset + 1);
            BeginAnimation(ScrollOffsetProperty, new DoubleAnimation(offset + 1, 0, ScrollDuration));
        }
        else
        {
            ResetScroll();
        }

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
        _pendingLevelCount = 0;
        _pendingPeak = 0;
        ResetScroll();
        InvalidateVisual();
    }

    private void ResetScroll()
    {
        BeginAnimation(ScrollOffsetProperty, null);
        SetValue(ScrollOffsetProperty, 0d);
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
        var scrollOffset = (double)GetValue(ScrollOffsetProperty);
        var maximumHalfHeight = Math.Max(1, Math.Min(8, (ActualHeight - 2) / 2));
        var barPen = CreatePen(WaveformBrush, 1.5);

        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        // Include bars just outside the left edge so they exit smoothly as well.
        var firstDrawnIndex = Math.Max(-firstVisibleIndex, -(int)Math.Ceiling(scrollOffset));
        for (var visibleIndex = firstDrawnIndex; visibleIndex < visibleBarCount; visibleIndex++)
        {
            var level = samples[firstVisibleIndex + visibleIndex];
            if (level <= 0)
            {
                continue;
            }

            var halfHeight = 1 + (Math.Pow(level, 0.72) * (maximumHalfHeight - 1));
            var x = left + ((visibleIndex + scrollOffset) * spacing);
            drawingContext.DrawLine(
                barPen,
                new Point(x, centerY - halfHeight),
                new Point(x, centerY + halfHeight));
        }

        drawingContext.Pop();
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

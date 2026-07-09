using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AiMeter.Controls;

public partial class CircularProgress : UserControl
{
    public static readonly DependencyProperty PercentageProperty = DependencyProperty.Register(
        nameof(Percentage), typeof(double), typeof(CircularProgress),
        new PropertyMetadata(0d, OnPercentageChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(CircularProgress),
        new PropertyMetadata(60d, OnSizeChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(CircularProgress),
        new PropertyMetadata(5d, OnSizeChanged));

    public static readonly DependencyProperty ForegroundBrushProperty = DependencyProperty.Register(
        nameof(ForegroundBrush), typeof(Brush), typeof(CircularProgress),
        new PropertyMetadata(new SolidColorBrush(Colors.DodgerBlue)));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CircularProgress),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(CircularProgress),
        new PropertyMetadata(string.Empty));

    public double Percentage
    {
        get => (double)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush ForegroundBrush
    {
        get => (Brush)GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public CircularProgress()
    {
        InitializeComponent();
        Loaded += (s, e) => UpdateArc();
    }

    private static void OnPercentageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircularProgress control) control.UpdateArc();
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircularProgress control) control.UpdateArc();
    }

    private void UpdateArc()
    {
        if (PART_PathFigure == null || PART_ArcSegment == null || PART_TrackEllipse == null) return;

        double radius = (Size - StrokeThickness) / 2;
        if (radius <= 0) return;

        PART_TrackEllipse.Width = Size - StrokeThickness;
        PART_TrackEllipse.Height = Size - StrokeThickness;

        double angle = (Percentage / 100.0) * 360.0;
        // WPF ArcSegment fails if angle is exactly 360
        if (angle >= 360) angle = 359.999;
        if (angle <= 0) angle = 0.001;

        double angleRad = (angle - 90) * Math.PI / 180.0;

        double x = (Size / 2) + radius * Math.Cos(angleRad);
        double y = (Size / 2) + radius * Math.Sin(angleRad);

        PART_PathFigure.StartPoint = new Point(Size / 2, StrokeThickness / 2);
        PART_ArcSegment.Point = new Point(x, y);
        PART_ArcSegment.Size = new Size(radius, radius);
        PART_ArcSegment.IsLargeArc = angle > 180.0;
    }
}

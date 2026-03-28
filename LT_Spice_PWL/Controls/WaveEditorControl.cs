using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PwlEditor.Models;

namespace PwlEditor.Controls;

public sealed class PointEditRequestedEventArgs : EventArgs
{
    public int Index { get; }
    public WavePoint Point { get; }

    public PointEditRequestedEventArgs(int index, WavePoint point)
    {
        Index = index;
        Point = point;
    }
}

public class WaveEditorControl : FrameworkElement
{
    private const double PointRadius = 5;
    private int _selectedIndex = -1;
    private bool _isDragging;
    private bool _isInternalUpdate;

    public ObservableCollection<WavePoint> Points { get; } = new();

    public double PeriodDuration { get; set; } = 1.0;
    public double YMin { get; set; } = -1.0;
    public double YMax { get; set; } = 1.0;
    public int HorizontalDivisions { get; set; } = 10;
    public int VerticalDivisions { get; set; } = 10;
    public double SnapX { get; set; } = 0.05;
    public double SnapY { get; set; } = 0.05;
    public bool SnapEnabled { get; set; } = true;

    public event EventHandler? PointsChanged;
    public event EventHandler<PointEditRequestedEventArgs>? PointEditRequested;

    public WaveEditorControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;

        Loaded += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) => InvalidateVisual();

        Points.CollectionChanged += (_, _) =>
        {
            if (_isInternalUpdate)
                return;

            _isInternalUpdate = true;
            SortPoints();
            _isInternalUpdate = false;
            InvalidateVisual();
            PointsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void SetPoints(IEnumerable<WavePoint> points)
    {
        _isInternalUpdate = true;

        Points.Clear();

        foreach (var point in points.OrderBy(p => p.X))
        {
            var safePoint = new WavePoint(
                Math.Clamp(point.X, 0.0, Math.Max(PeriodDuration, 1e-12)),
                Math.Clamp(point.Y, Math.Min(YMin, YMax), Math.Max(YMin, YMax))
            );

            Points.Add(safePoint);
        }

        SortPoints();

        _isInternalUpdate = false;
        _selectedIndex = -1;

        InvalidateVisual();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePoint(int index, WavePoint updatedPoint)
    {
        if (index < 0 || index >= Points.Count)
            return;

        var clamped = ClampPoint(index, updatedPoint);
        Points[index].X = clamped.X;
        Points[index].Y = clamped.Y;

        SortPoints();
        _selectedIndex = FindNearestPointIndex(clamped);

        InvalidateVisual();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSelectedPoint()
    {
        if (_selectedIndex < 0 || _selectedIndex >= Points.Count)
            return;

        Points.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Min(_selectedIndex, Points.Count - 1);

        InvalidateVisual();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var canvasBackground = GetBrush("CanvasBackgroundBrush", Brushes.White);
        var borderBrush = GetBrush("BorderBrushTheme", Brushes.LightGray);
        var gridBrush = GetBrush("GridLineBrush", Brushes.Gainsboro);
        var axisBrush = GetBrush("AxisLineBrush", Brushes.Gray);
        var waveBrush = GetBrush("WaveLineBrush", Brushes.DodgerBlue);
        var pointBrush = GetBrush("PointBrush", Brushes.DarkBlue);
        var selectedPointBrush = GetBrush("SelectedPointBrush", Brushes.OrangeRed);
        var infoTextBrush = GetBrush("InfoTextBrush", Brushes.DimGray);

        dc.DrawRectangle(canvasBackground, new Pen(borderBrush, 1), new Rect(0, 0, ActualWidth, ActualHeight));

        DrawGrid(dc, gridBrush);
        DrawAxes(dc, axisBrush);
        DrawPolyline(dc, waveBrush);
        DrawPoints(dc, pointBrush, selectedPointBrush);
        DrawInfo(dc, infoTextBrush);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        Focus();
        CaptureMouse();

        var position = e.GetPosition(this);
        var hitIndex = HitTestPoint(position);

        if (hitIndex >= 0)
        {
            _selectedIndex = hitIndex;
            _isDragging = true;
        }
        else
        {
            var newPoint = CreatePointFromMouse(position);
            InsertPointSorted(newPoint);
            _selectedIndex = FindNearestPointIndex(newPoint);
            _isDragging = true;
            PointsChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDragging || _selectedIndex < 0 || _selectedIndex >= Points.Count)
            return;

        var point = CreatePointFromMouse(e.GetPosition(this));
        point = ClampPoint(_selectedIndex, point);

        Points[_selectedIndex].X = point.X;
        Points[_selectedIndex].Y = point.Y;

        _isInternalUpdate = true;
        SortPoints();
        _isInternalUpdate = false;

        _selectedIndex = FindNearestPointIndex(point);

        InvalidateVisual();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _isDragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        Focus();

        var index = HitTestPoint(e.GetPosition(this));
        if (index >= 0)
        {
            _selectedIndex = index;
            PointEditRequested?.Invoke(this, new PointEditRequestedEventArgs(index, Points[index].Clone()));
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Delete)
        {
            RemoveSelectedPoint();
            e.Handled = true;
        }
    }

    private void DrawGrid(DrawingContext dc, Brush gridBrush)
    {
        var gridPen = new Pen(gridBrush, 1);

        for (var i = 1; i < HorizontalDivisions; i++)
        {
            var x = ActualWidth * i / HorizontalDivisions;
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, ActualHeight));
        }

        for (var i = 1; i < VerticalDivisions; i++)
        {
            var y = ActualHeight * i / VerticalDivisions;
            dc.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawAxes(DrawingContext dc, Brush axisBrush)
    {
        if (YMin >= 0 || YMax <= 0)
            return;

        var zeroY = ToCanvasY(0);
        dc.DrawLine(new Pen(axisBrush, 1.2), new Point(0, zeroY), new Point(ActualWidth, zeroY));
    }

    private void DrawPolyline(DrawingContext dc, Brush waveBrush)
    {
        if (Points.Count == 0)
            return;

        var pen = new Pen(waveBrush, 2.0);

        for (var i = 0; i < Points.Count - 1; i++)
        {
            dc.DrawLine(pen, ToCanvasPoint(Points[i]), ToCanvasPoint(Points[i + 1]));
        }
    }

    private void DrawPoints(DrawingContext dc, Brush pointBrush, Brush selectedPointBrush)
    {
        for (var i = 0; i < Points.Count; i++)
        {
            var point = ToCanvasPoint(Points[i]);
            var brush = i == _selectedIndex ? selectedPointBrush : pointBrush;
            dc.DrawEllipse(brush, null, point, PointRadius, PointRadius);
        }
    }

    private void DrawInfo(DrawingContext dc, Brush infoTextBrush)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var text = new FormattedText(
            $"Punkte: {Points.Count}   |   DEL löscht markierten Punkt   |   Rechtsklick = exakt bearbeiten",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            infoTextBrush,
            dpi);

        dc.DrawText(text, new Point(8, 8));
    }

    private Brush GetBrush(string key, Brush fallback)
    {
        return Application.Current.Resources[key] as Brush ?? fallback;
    }

    private Point ToCanvasPoint(WavePoint point)
    {
        double safeX = Math.Clamp(point.X, 0.0, Math.Max(PeriodDuration, 1e-12));
        double safeY = Math.Clamp(point.Y, Math.Min(YMin, YMax), Math.Max(YMin, YMax));
        return new Point(ToCanvasX(safeX), ToCanvasY(safeY));
    }

    private double ToCanvasX(double x)
    {
        if (PeriodDuration <= 0)
            return 0;

        return x / PeriodDuration * Math.Max(1, ActualWidth);
    }

    private double ToCanvasY(double y)
    {
        var range = Math.Max(1e-9, YMax - YMin);
        return (1.0 - ((y - YMin) / range)) * Math.Max(1, ActualHeight);
    }

    private WavePoint CreatePointFromMouse(Point mouse)
    {
        var x = (mouse.X / Math.Max(1, ActualWidth)) * PeriodDuration;
        var y = YMax - (mouse.Y / Math.Max(1, ActualHeight)) * (YMax - YMin);

        if (SnapEnabled)
        {
            x = Snap(x, SnapX);
            y = Snap(y, SnapY);
        }

        x = Math.Clamp(x, 0, PeriodDuration);
        y = Math.Clamp(y, Math.Min(YMin, YMax), Math.Max(YMin, YMax));

        return new WavePoint(x, y);
    }

    private WavePoint ClampPoint(int index, WavePoint point)
    {
        var minX = 0.0;
        var maxX = PeriodDuration;
        const double eps = 1e-9;

        if (index > 0)
            minX = Points[index - 1].X;

        if (index < Points.Count - 1)
            maxX = Points[index + 1].X;

        point.X = Math.Clamp(point.X, minX, maxX);
        point.Y = Math.Clamp(point.Y, Math.Min(YMin, YMax), Math.Max(YMin, YMax));

        if (index > 0 && point.X < Points[index - 1].X - eps)
            point.X = Points[index - 1].X;

        if (index < Points.Count - 1 && point.X > Points[index + 1].X + eps)
            point.X = Points[index + 1].X;

        return point;
    }

    private void InsertPointSorted(WavePoint point)
    {
        var index = 0;
        while (index < Points.Count && Points[index].X < point.X)
            index++;

        Points.Insert(index, point);
    }

    private int FindNearestPointIndex(WavePoint point)
    {
        var bestIndex = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < Points.Count; i++)
        {
            var dx = Points[i].X - point.X;
            var dy = Points[i].Y - point.Y;
            var d = dx * dx + dy * dy;

            if (d < bestDistance)
            {
                bestDistance = d;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private int HitTestPoint(Point mouse)
    {
        for (var i = 0; i < Points.Count; i++)
        {
            var p = ToCanvasPoint(Points[i]);
            if ((p - mouse).Length <= PointRadius + 4)
                return i;
        }

        return -1;
    }

    private void SortPoints()
    {
        var ordered = Points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Points[i], ordered[i]))
            {
                Points.Move(Points.IndexOf(ordered[i]), i);
            }
        }
    }

    private static double Snap(double value, double step)
    {
        if (step <= 0)
            return value;

        return Math.Round(value / step) * step;
    }
}
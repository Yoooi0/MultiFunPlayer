using MultiFunPlayer.Common;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for DraggablePointCanvas.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
public partial class DraggablePointCanvas : UserControl
{
    private Vector _captureOffset;
    private KeyframeCollection _keyframes;

    public string PopupText { get; set; }
    public PointCollection LinePoints { get; set; }

    public UIElementCollection PointElements => PointCanvas.Children;

    [DoNotNotify]
    public ObservableConcurrentCollection<Point> Points
    {
        get => (ObservableConcurrentCollection<Point>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(ObservableConcurrentCollection<Point>),
            typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    new PropertyChangedCallback(OnPointsPropertyChanged)));

    [DoNotNotify]
    public double ScrubberPosition
    {
        get => (double)GetValue(ScrubberPositionProperty);
        set => SetValue(ScrubberPositionProperty, value);
    }

    public static readonly DependencyProperty ScrubberPositionProperty =
        DependencyProperty.Register(nameof(ScrubberPosition), typeof(double),
            typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnScrubberPositionPropertyChanged)));

    [DoNotNotify]
    public InterpolationType InterpolationType
    {
        get => (InterpolationType)GetValue(InterpolationTypeProperty);
        set => SetValue(InterpolationTypeProperty, value);
    }

    public static readonly DependencyProperty InterpolationTypeProperty =
        DependencyProperty.Register(nameof(InterpolationType), typeof(InterpolationType),
            typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(InterpolationType.Linear,
                new PropertyChangedCallback(OnInterpolationTypePropertyChanged)));

    [DoNotNotify]
    public int InterpolationPointCount
    {
        get => (int)GetValue(InterpolationPointCountProperty);
        set => SetValue(InterpolationPointCountProperty, value);
    }

    public static readonly DependencyProperty InterpolationPointCountProperty =
        DependencyProperty.Register(nameof(InterpolationPointCount), typeof(int),
            typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(100,
                new PropertyChangedCallback(OnInterpolationPointCountPropertyChanged)));

    [DoNotNotify]
    public Rect Viewport
    {
        get => (Rect)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(Rect),
            typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(new Rect(0, 0, 100, 100),
                    new PropertyChangedCallback(OnViewportPropertyChanged)));

    [DoNotNotify]
    public string PopupFormat
    {
        get => (string)GetValue(PopupFormatProperty);
        set => SetValue(PopupFormatProperty, value);
    }

    public static readonly DependencyProperty PopupFormatProperty =
        DependencyProperty.Register(nameof(PopupFormat), typeof(string),
            typeof(DraggablePointCanvas), new PropertyMetadata("X: {0} Y: {1}"));

    [SuppressPropertyChangedWarnings]
    private static void OnPointsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= @this.OnPointsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += @this.OnPointsCollectionChanged;

        @this.SynchronizeElementsFromPoints();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnScrubberPositionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        @this.RefreshScrubber();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnInterpolationTypePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        @this.RefreshLine();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnInterpolationPointCountPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        @this.RefreshLine();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private static void OnViewportPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        @this.UpdateViewport((Rect)e.OldValue, (Rect)e.NewValue);
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        SynchronizeElementsFromPoints();
        RefreshScrubber();
    }

    public DraggablePointCanvas()
    {
        InitializeComponent();

        IsVisibleChanged += (_, _) => RefreshScrubber();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is DraggablePoint point)
        {
            if (e.ClickCount == 1)
            {
                _captureOffset = e.GetPosition(point) - point.Position;
                Mouse.Capture(point, CaptureMode.Element);
                SynchronizePopup(point.Position);
            }
            else if (e.ClickCount == 2 && PointElements.Count > 1)
            {
                RemoveElement(point);
                SynchronizePointsFromElements();
            }
        }
        else if (e.Source is Canvas && e.ClickCount == 2)
        {
            AddElement(e.GetPosition(PointCanvas));
            SynchronizePointsFromElements();
        }
    }

    private void RemoveElement(UIElement element)
    {
        PointElements.Remove(element);

        element.MouseEnter -= OnElementMouseEnter;
        element.MouseLeave -= OnElementMouseLeave;
    }

    private void AddElement(Point position)
    {
        var element = new DraggablePoint()
        {
            Position = position
        };

        element.MouseEnter += OnElementMouseEnter;
        element.MouseLeave += OnElementMouseLeave;

        PointElements.Add(element);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Mouse.Captured is not DraggablePoint)
            return;

        _captureOffset = new Vector();
        Mouse.Capture(null);
        DisablePopup();
        SynchronizePointsFromElements();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.Captured is not DraggablePoint point)
            return;

        var position = e.GetPosition(this) - _captureOffset;
        position.X = Math.Clamp(position.X, 0, ActualWidth);
        position.Y = Math.Clamp(position.Y, 0, ActualHeight);

        point.Position = position;
        SynchronizePopup(position);
        SynchronizePointsFromElements();
    }

    [SuppressPropertyChangedWarnings]
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SynchronizeElementsFromPoints();
        RefreshScrubber();
    }

    private void OnElementMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.Source is not DraggablePoint)
            return;

        DisablePopup();
    }

    private void OnElementMouseEnter(object sender, MouseEventArgs e)
    {
        if (e.Source is not DraggablePoint point)
            return;

        SynchronizePopup(point.Position);
    }

    private void UpdateViewport(Rect oldValue, Rect newValue)
    {
        if (Points != null)
        {
            for (var i = 0; i < Points.Count;)
            {
                if (!newValue.Contains(Points[i]))
                    Points.RemoveAt(i);
                else
                    i++;
            }
        }

        SynchronizeElementsFromPoints();
    }

    private void SynchronizeElementsFromPoints()
    {
        if (ActualWidth == 0 || ActualHeight == 0)
            return;

        while (PointElements.Count > 0 && (Points == null || PointElements.Count > Points.Count))
            RemoveElement(PointElements[^1]);

        if (Points == null)
            return;

        while (PointElements.Count < Points.Count)
            AddElement(new Point());

        var childrenPoints = PointElements.OfType<DraggablePoint>().ToList();
        var orderedPoints = Points.OrderBy(p => p.X).ToList();
        for (var i = 0; i < orderedPoints.Count; i++)
            childrenPoints[i].Position = ToCanvas(orderedPoints[i]);

        RefreshLine();
    }

    private void SynchronizePointsFromElements()
    {
        if (ActualWidth == 0 || ActualHeight == 0)
            return;

        if (Points != null)
        {
            while (Points.Count > 0 && Points.Count > PointElements.Count)
                Points.RemoveAt(Points.Count - 1);

            while (Points.Count < PointElements.Count)
                Points.Add(new Point());
        }

        var childrenPoints = PointElements.OfType<DraggablePoint>().OrderBy(p => p.Position.X).ToList();
        for (var i = 0; i < childrenPoints.Count; i++)
        {
            PointElements.Remove(childrenPoints[i]);
            PointElements.Add(childrenPoints[i]);

            if (Points != null)
                Points[i] = FromCanvas(childrenPoints[i].Position);
        }

        RefreshLine();
    }

    private void RefreshLine()
    {
        if (!IsVisible)
            return;
        if (Points == null || Points.Count == 0 || ActualWidth == 0 || ActualHeight == 0)
            return;

        _keyframes = new KeyframeCollection(Points.Count + 2)
        {
            { ToCanvasX(Viewport.Left), ToCanvasY(Points[0].Y) }
        };

        foreach (var point in Points)
            _keyframes.Add(ToCanvasX(point.X), ToCanvasY(point.Y));
        _keyframes.Add(ToCanvasX(Viewport.Right), ToCanvasY(Points[^1].Y));

        if (InterpolationType == InterpolationType.Linear)
        {
            LinePoints = new PointCollection(_keyframes.Select(k => new Point(k.Position, k.Value)));
        }
        else
        {
            var step = Viewport.Width / InterpolationPointCount;
            var interpolatedPoints = new List<Point>(InterpolationPointCount);

            for (var i = 0; i < _keyframes.Count - 1; i++)
                for (var x = _keyframes[i].Position; x < _keyframes[i + 1].Position; x += step)
                    interpolatedPoints.Add(new Point(x, Math.Clamp(_keyframes.Interpolate(i, x, InterpolationType), 0, ActualHeight)));

            LinePoints = new PointCollection(interpolatedPoints);
        }

        RefreshScrubber();
    }

    private void RefreshScrubber()
    {
        if (!IsVisible)
            return;

        var canRefresh = CanRefresh();
        Scrubber.Visibility = canRefresh ? Visibility.Visible : Visibility.Collapsed;
        if (!canRefresh)
            return;

        var x = ToCanvasX(ScrubberPosition);
        var index = _keyframes.AdvanceIndex(-1, x);
        if (!_keyframes.ValidateIndex(index) || !_keyframes.ValidateIndex(index + 1))
            return;

        var y = _keyframes.Interpolate(index, x, InterpolationType);
        (Scrubber.Data as EllipseGeometry).Center = new Point(x, y);

        bool CanRefresh()
        {
            if (Points == null || Points.Count == 0)
                return false;
            if (_keyframes == null || _keyframes.Count == 0)
                return false;
            if (!double.IsFinite(ScrubberPosition))
                return false;
            return true;
        }
    }

    public Point FromCanvas(Point point) => new(FromCanvasX(point.X), FromCanvasY(point.Y));
    public double FromCanvasX(double x) => MathUtils.Map(x, 0, ActualWidth, Viewport.Left, Viewport.Right);
    public double FromCanvasY(double y) => MathUtils.Map(y, 0, ActualHeight, Viewport.Bottom, Viewport.Top);

    public Point ToCanvas(Point point) => new(ToCanvasX(point.X), ToCanvasY(point.Y));
    public double ToCanvasX(double x) => MathUtils.Map(x, Viewport.Left, Viewport.Right, 0, ActualWidth);
    public double ToCanvasY(double y) => MathUtils.Map(y, Viewport.Bottom, Viewport.Top, 0, ActualHeight);

    private void SynchronizePopup(Point position)
    {
        //TODO: updating offsets prevents ScrubberPosition from getting PropertyChanged events
        Popup.HorizontalOffset = position.X - 10;
        Popup.VerticalOffset = position.Y - 30;

        Popup.IsOpen = true;

        var point = FromCanvas(position);
        PopupText = string.Format(PopupFormat, point.X, point.Y);
    }

    private void DisablePopup()
    {
        Popup.IsOpen = false;
        PopupText = null;
    }
}
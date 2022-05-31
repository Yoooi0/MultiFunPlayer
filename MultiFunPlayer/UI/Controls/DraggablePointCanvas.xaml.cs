using MultiFunPlayer.Common;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for DraggablePointCanvas.xaml
/// </summary>
public partial class DraggablePointCanvas : Canvas, INotifyPropertyChanged
{
    public string PopupText { get; set; }

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
    public string PopupFormat
    {
        get => (string)GetValue(PopupFormatProperty);
        set => SetValue(PopupFormatProperty, value);
    }

    public static readonly DependencyProperty PopupFormatProperty =
        DependencyProperty.Register(nameof(PopupFormat), typeof(string),
            typeof(DraggablePointCanvas), new PropertyMetadata("X: {0} Y: {1}"));

    [DoNotNotify]
    public DataTemplate ItemTemplate
    {
        get => (DataTemplate)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate),
                typeof(DraggablePointCanvas), new FrameworkPropertyMetadata(null, null));

    [SuppressPropertyChangedWarnings]
    private static void OnPointsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DraggablePointCanvas @this)
            return;

        if(e.OldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= @this.OnPointsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += @this.OnPointsCollectionChanged;

        @this.SynchronizeElementsFromPoints();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(e.Property.Name));
    }

    [SuppressPropertyChangedWarnings]
    private void OnPointsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => SynchronizeElementsFromPoints();

    public DraggablePointCanvas()
    {
        InitializeComponent();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is DraggablePoint point)
        {
            if (e.ClickCount == 1)
            {
                Mouse.Capture(point, CaptureMode.Element);
                SynchronizePopup(point.Position);
            }
            else if (e.ClickCount == 2)
            {
                RemoveElement(point);
                SynchronizePointsFromElements();
            }
        }
        else if (e.OriginalSource is DraggablePointCanvas && e.ClickCount == 2)
        {
            AddElement(e.GetPosition(this));
            SynchronizePointsFromElements();
        }
    }

    private void RemoveElement(UIElement element)
    {
        Children.Remove(element);

        element.MouseEnter -= OnElementMouseEnter;
        element.MouseLeave -= OnElementMouseLeave;
    }

    private void AddElement(Point position)
    {
        var element = new DraggablePoint()
        { 
            Position = position, 
            ContentTemplate = ItemTemplate
        };

        element.MouseEnter += OnElementMouseEnter;
        element.MouseLeave += OnElementMouseLeave;

        Children.Add(element);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Mouse.Captured is not DraggablePoint point)
            return;

        Mouse.Capture(null);
        SynchronizePopup(null);
        SynchronizePointsFromElements();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.Captured is not DraggablePoint point)
            return;

        var position = e.GetPosition(this);
        position.X = MathUtils.Clamp((float)position.X, 0, (float)ActualWidth);
        position.Y = MathUtils.Clamp((float)position.Y, 0, (float)ActualHeight);

        point.Position = position;
        SynchronizePopup(position);
        SynchronizePointsFromElements();
    }

    private void OnElementMouseLeave(object sender, MouseEventArgs e)
    {
        if (e.Source is not DraggablePoint point)
            return;

        SynchronizePopup(null);
    }

    private void OnElementMouseEnter(object sender, MouseEventArgs e)
    {
        if (e.Source is not DraggablePoint point)
            return;

        SynchronizePopup(point.Position);
    }

    private void SynchronizeElementsFromPoints()
    {
        while (Children.Count > 0 && (Points == null || Children.Count > Points.Count))
            RemoveElement(Children[^1]);

        if (Points == null)
            return;

        while (Children.Count < Points.Count)
            AddElement(new Point());

        var orderedPoints = Points.OrderBy(p => p.X).ToList();
        for (var i = 0; i < orderedPoints.Count; i++)
            (Children[i] as DraggablePoint).Position = orderedPoints[i];
    }

    private void SynchronizePointsFromElements()
    {
        if (Points != null)
        {
            while (Points.Count > 0 && Points.Count > Children.Count)
                Points.RemoveAt(Points.Count - 1);

            while (Points.Count < Children.Count)
                Points.Add(new Point());
        }

        var orderedPoints = Children.OfType<DraggablePoint>().OrderBy(p => p.Position.X) .ToList();

        Children.Clear();
        for (var i = 0; i < orderedPoints.Count; i++)
        {
            Children.Insert(i, orderedPoints[i]);

            if(Points != null)
                Points[i] = orderedPoints[i].Position;
        }
    }

    private void SynchronizePopup(Point? position)
    {
        if (position == null)
        {
            Popup.IsOpen = false;
            PopupText = null;
        }
        else
        {
            var x = position.Value.X;
            var y = position.Value.Y;

            Popup.HorizontalOffset = x + 10;
            Popup.VerticalOffset = y + 30;
            Popup.IsOpen = true;

            PopupText = string.Format(PopupFormat, position.Value.X, position.Value.Y);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}

public class DraggablePoint : ContentControl
{
    public Point Position
    {
        get => (Point)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(Point),
            typeof(DraggablePoint), new FrameworkPropertyMetadata(default(Point),
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null));
}

using MultiFunPlayer.Common;
using MultiFunPlayer.Script;
using MultiFunPlayer.UI.Controls.ViewModels;
using PropertyChanged;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultiFunPlayer.UI.Controls;

/// <summary>
/// Interaction logic for MarkersPreview.xaml
/// </summary>
[AddINotifyPropertyChangedInterface]
internal sealed partial class MarkersPreview : UserControl
{
    private readonly Color[] _colors;

    public List<ChapterModel> ChapterModels { get; private set; }
    public List<BookmarkModel> BookmarkModels { get; private set; }

    public object ToolTipContent { get; set; }
    public object ToolTipTarget { get; set; }
    public bool ToolTipIsOpen { get; set; }

    public double ScrubberPosition => ShowScrubber ? Position / Duration * ActualWidth : 0;
    public bool ShowScrubber => double.IsFinite(Duration) && Duration > 0;

    public event EventHandler<SeekRequestEventArgs> SeekRequest;

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, ChapterCollection> Chapters
    {
        get => (IReadOnlyDictionary<DeviceAxis, ChapterCollection>)GetValue(ChaptersProperty);
        set => SetValue(ChaptersProperty, value);
    }

    public static readonly DependencyProperty ChaptersProperty =
        DependencyProperty.Register(nameof(Chapters), typeof(IReadOnlyDictionary<DeviceAxis, ChapterCollection>),
            typeof(MarkersPreview), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnChaptersChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnChaptersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkersPreview @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldChapters)
            oldChapters.CollectionChanged -= @this.OnChaptersCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newChapters)
            newChapters.CollectionChanged += @this.OnChaptersCollectionChanged;

        @this.RefreshChapters();
    }

    [SuppressPropertyChangedWarnings]
    private void OnChaptersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshChapters();

    [DoNotNotify]
    public IReadOnlyDictionary<DeviceAxis, BookmarkCollection> Bookmarks
    {
        get => (IReadOnlyDictionary<DeviceAxis, BookmarkCollection>)GetValue(BookmarksProperty);
        set => SetValue(BookmarksProperty, value);
    }

    public static readonly DependencyProperty BookmarksProperty =
        DependencyProperty.Register(nameof(Bookmarks), typeof(IReadOnlyDictionary<DeviceAxis, BookmarkCollection>),
            typeof(MarkersPreview), new FrameworkPropertyMetadata(null,
                new PropertyChangedCallback(OnBookmarksChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnBookmarksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkersPreview @this)
            return;

        if (e.OldValue is INotifyCollectionChanged oldBookmarks)
            oldBookmarks.CollectionChanged -= @this.OnBookmarksCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newBookmarks)
            newBookmarks.CollectionChanged += @this.OnBookmarksCollectionChanged;

        @this.RefreshBookmarks();
    }

    [SuppressPropertyChangedWarnings]
    private void OnBookmarksCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshBookmarks();

    [DoNotNotify]
    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(double),
            typeof(MarkersPreview), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnDurationChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkersPreview @this)
            return;

        @this.RefreshBookmarks();
        @this.RefreshChapters();
        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ShowScrubber)));
    }

    [DoNotNotify]
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double),
            typeof(MarkersPreview), new FrameworkPropertyMetadata(double.NaN,
                new PropertyChangedCallback(OnPositionChanged)));

    [SuppressPropertyChangedWarnings]
    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkersPreview @this)
            return;

        @this.PropertyChanged?.Invoke(@this, new PropertyChangedEventArgs(nameof(ScrubberPosition)));
    }

    public MarkersPreview()
    {
        InitializeComponent();

        _colors =
        [
            (Color)ColorConverter.ConvertFromString("#d94452"),
            (Color)ColorConverter.ConvertFromString("#e86c3f"),
            (Color)ColorConverter.ConvertFromString("#f5ba45"),
            (Color)ColorConverter.ConvertFromString("#8ac045"),
            (Color)ColorConverter.ConvertFromString("#35bb9b"),
            (Color)ColorConverter.ConvertFromString("#5690dc"),
            (Color)ColorConverter.ConvertFromString("#967ada")
        ];
    }

    private void RefreshChapters()
    {
        ResetToolTip();

        ChapterModels = null;
        if (Chapters == null || Chapters.Count == 0 || !double.IsFinite(Duration) || Duration <= 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        var (axis, chapters) = Chapters.FirstOrDefault(x => x.Value != null);
        if (chapters == null)
            return;

        var items = new List<ChapterModel>(chapters.Count);
        foreach (var chapter in chapters)
        {
            if (chapter.EndPosition < 0)
                continue;
            if (chapter.StartPosition > Duration)
                continue;

            items.Add(new()
            {
                Name = chapter.Name,
                StartPosition = chapter.StartPosition < 0 ? 0 : chapter.StartPosition,
                EndPosition = chapter.EndPosition > Duration ? Duration : chapter.EndPosition,
                Color = _colors[items.Count % _colors.Length],
                CanvasMultiplier = ActualWidth / Duration,
            });
        }

        ChapterModels = new List<ChapterModel>(items);
    }

    private void RefreshBookmarks()
    {
        ResetToolTip();

        BookmarkModels = null;
        if (Bookmarks == null || Bookmarks.Count == 0 || !double.IsFinite(Duration) || Duration <= 0 || ActualWidth < 1 || ActualHeight < 1)
            return;

        var (axis, bookmarks) = Bookmarks.FirstOrDefault(x => x.Value != null);
        if (bookmarks == null)
            return;

        var models = new List<BookmarkModel>(bookmarks.Count);
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.Position < 0)
                continue;
            if (bookmark.Position > Duration)
                continue;

            models.Add(new()
            {
                Name = bookmark.Name,
                Position = bookmark.Position,
                CanvasMultiplier = ActualWidth / Duration,
            });
        }

        BookmarkModels = new List<BookmarkModel>(models);
    }

    public void OnChapterStartClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ChapterModel model)
            return;

        SeekRequest?.Invoke(this, new SeekRequestEventArgs(TimeSpan.FromSeconds(model.StartPosition)));
    }

    public void OnChapterEndClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ChapterModel model)
            return;

        SeekRequest?.Invoke(this, new SeekRequestEventArgs(TimeSpan.FromSeconds(model.EndPosition)));
    }

    public void OnBookmarkClick(object sender, EventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BookmarkModel model)
            return;

        SeekRequest?.Invoke(this, new SeekRequestEventArgs(TimeSpan.FromSeconds(model.Position)));
    }

    public void OnChapterMouseEnter(object sender, MouseEventArgs e) => UpdateChapterToolTip(true, sender);
    public void OnChapterMouseLeave(object sender, MouseEventArgs e) => UpdateChapterToolTip(false, sender);
    public void OnChapterStartMouseLeave(object sender, MouseEventArgs e) => UpdateChapterToolTip(true, sender);
    public void OnChapterMiddleMouseLeave(object sender, MouseEventArgs e) => UpdateChapterToolTip(true, sender);
    public void OnChapterEndMouseLeave(object sender, MouseEventArgs e) => UpdateChapterToolTip(true, sender);

    public void OnChapterStartMouseEnter(object sender, MouseEventArgs e)
        => UpdateChapterToolTip(true, sender, m => new MarkerToolTipModel("Chapter", m.Name, TimeSpan.FromSeconds(m.StartPosition)));
    public void OnChapterMiddleMouseEnter(object sender, MouseEventArgs e)
        => UpdateChapterToolTip(true, sender, m => new ChapterToolTipModel(m.Name, TimeSpan.FromSeconds(m.StartPosition), TimeSpan.FromSeconds(m.EndPosition)));
    public void OnChapterEndMouseEnter(object sender, MouseEventArgs e)
        => UpdateChapterToolTip(true, sender, m => new MarkerToolTipModel("Chapter", m.Name, TimeSpan.FromSeconds(m.EndPosition)));

    public void OnBookmarkMouseEnter(object sender, MouseEventArgs e)
        => UpdateBookMarkToolTip(true, sender, b => new MarkerToolTipModel("Bookmark", b.Name, TimeSpan.FromSeconds(b.Position)));
    public void OnBookmarkMouseLeave(object sender, MouseEventArgs e) => UpdateBookMarkToolTip(false, sender);

    private void UpdateChapterToolTip(bool open, object sender, Func<ChapterModel, object> contentFactory = null)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ChapterModel model)
            return;

        UpdateToolTip(open, sender, contentFactory, model);
    }

    private void UpdateBookMarkToolTip(bool open, object sender, Func<BookmarkModel, object> contentFactory = null)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BookmarkModel model)
            return;

        UpdateToolTip(open, sender, contentFactory, model);
    }

    private void ResetToolTip()
    {
        ToolTipTarget = null;
        ToolTipIsOpen = false;
        ToolTipContent = null;
    }

    private void UpdateToolTip<T>(bool open, object sender, Func<T, object> contentFactory, T model)
    {
        ToolTipTarget = sender;
        ToolTipIsOpen = open;

        if (open && contentFactory != null)
            ToolTipContent = contentFactory(model);
    }

    [SuppressPropertyChangedWarnings]
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshBookmarks();
        RefreshChapters();
    }
}

internal sealed class ChapterModel
{
    public string Name { get; init; }
    public double StartPosition { get; init; }
    public double EndPosition { get; init; }
    public Color Color { get; init; }

    public double CanvasMultiplier { get; init; }
    public double CanvasLeft => Math.Floor(StartPosition * CanvasMultiplier);
    public double CanvasRight => Math.Floor(EndPosition * CanvasMultiplier);
    public double CanvasWidth => CanvasRight - CanvasLeft;
    public double CanvasMiddleWidth => Math.Max(0, CanvasWidth - 12);
}

internal sealed class BookmarkModel
{
    public string Name { get; init; }
    public double Position { get; init; }

    public double CanvasMultiplier { get; init; }
    public double CanvasLeft => Math.Floor(Position * CanvasMultiplier);
}

internal sealed record ChapterToolTipModel(string Name, TimeSpan StartPosition, TimeSpan EndPosition);
internal sealed record MarkerToolTipModel(string MarkerType, string Name, TimeSpan Position);
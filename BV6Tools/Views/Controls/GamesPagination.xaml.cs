using System.Collections;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BV6Tools.Views.Controls
{
    public partial class GamesPagination : UserControl
    {
        public static readonly DependencyProperty AutoSlideIntervalProperty =
            DependencyProperty.Register(nameof(AutoSlideInterval), typeof(int),
                typeof(GamesPagination), new PropertyMetadata(5000, OnAutoSlideSettingsChanged));

        public static readonly DependencyProperty CanScrollPagesProperty =
            DependencyProperty.Register(nameof(CanScrollPages), typeof(bool),
                typeof(GamesPagination), new PropertyMetadata(false));

        public static readonly DependencyProperty ControlsPositionProperty =
            DependencyProperty.Register(nameof(ControlsPosition), typeof(Dock),
                typeof(GamesPagination), new PropertyMetadata(Dock.Top));

        public static readonly DependencyProperty IsAutoSlideEnabledProperty =
            DependencyProperty.Register(nameof(IsAutoSlideEnabled), typeof(bool),
                typeof(GamesPagination), new PropertyMetadata(false, OnAutoSlideSettingsChanged));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable),
                typeof(GamesPagination), new PropertyMetadata(null, OnItemsSourceChanged));

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(nameof(ItemTemplate),
                typeof(DataTemplate), typeof(GamesPagination));

        public static readonly DependencyProperty PaginationControlsProperty =
            DependencyProperty.Register(nameof(PaginationControls), typeof(object), typeof(GamesPagination));

        public static readonly DependencyProperty ViewBoxMarginProperty =
            DependencyProperty.Register(nameof(ViewBoxMargin), typeof(Thickness),
                typeof(GamesPagination), new PropertyMetadata(new Thickness(10, 4, 10, 4)));

        public static readonly DependencyProperty ViewBoxWidthProperty =
            DependencyProperty.Register(nameof(ViewBoxWidth), typeof(double), typeof(GamesPagination));

        private EventHandler? _currentRenderingHandler;

        private DispatcherTimer? _resizeDebounce;

        private ScrollViewer? _scrollViewer;

        private DispatcherTimer? autoSlideTimer;

        private int cardStartIndex;

        private bool isAutoSliding = true;

        private int itemsPerPage = 5;

        public GamesPagination()
        {
            InitializeComponent();
        }

        public int AutoSlideInterval
        {
            get => (int)GetValue(AutoSlideIntervalProperty);
            set => SetValue(AutoSlideIntervalProperty, value);
        }

        public bool CanScrollPages
        {
            get => (bool)GetValue(CanScrollPagesProperty);
            set => SetValue(CanScrollPagesProperty, value);
        }

        public Dock ControlsPosition
        {
            get => (Dock)GetValue(ControlsPositionProperty);
            set => SetValue(ControlsPositionProperty, value);
        }

        public bool IsAutoSlideEnabled
        {
            get => (bool)GetValue(IsAutoSlideEnabledProperty);
            set => SetValue(IsAutoSlideEnabledProperty, value);
        }

        public IEnumerable ItemsSource { get => (IEnumerable)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

        public DataTemplate ItemTemplate { get => (DataTemplate)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

        public object PaginationControls { get => GetValue(PaginationControlsProperty); set => SetValue(PaginationControlsProperty, value); }

        public Thickness ViewBoxMargin { get => (Thickness)GetValue(ViewBoxMarginProperty); set => SetValue(ViewBoxMarginProperty, value); }

        public double ViewBoxWidth { get => (double)GetValue(ViewBoxWidthProperty); set => SetValue(ViewBoxWidthProperty, value); }

        public void BeginAutoSlide()
        {
            isAutoSliding = true;
            UpdateAutoSlideState();
        }

        public void FreezeAutoSlide() => StopAutoSlide();

        public void MoveNext() => GoToNext();

        public void MovePrevious() => GoToPrevious();

        private static void OnAutoSlideSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamesPagination control)
            {
                control.UpdateAutoSlideState();
            }
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GamesPagination control)
            {
                if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCollection)
                {
                    oldCollection.CollectionChanged -= control.OnItemsSourceCollectionChanged;
                }

                if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCollection)
                {
                    newCollection.CollectionChanged += control.OnItemsSourceCollectionChanged;
                }

                control.Dispatcher.BeginInvoke(new Action(() =>
                {
                    control.SetCardWidth();
                    control.UpdateAutoSlideState();
                }), DispatcherPriority.Background);
            }
        }

        private void OnItemsSourceCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetCardWidth();
                UpdateAutoSlideState();
            }), DispatcherPriority.Background);
        }

        #region Layout Setup

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            GamesItemsView.ApplyTemplate();

            _scrollViewer?.SizeChanged -= OnScrollViewerSizeChanged;

            _scrollViewer = GetScrollViewer(GamesItemsView);

            _scrollViewer?.SizeChanged += OnScrollViewerSizeChanged;

            SetCardWidth();

            UpdateAutoSlideState();
        }

        private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged) return;

            if (_resizeDebounce == null)
            {
                _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _resizeDebounce.Tick += (_, _) =>
                {
                    _resizeDebounce.Stop();
                    SetCardWidth();
                };
            }

            _resizeDebounce.Stop();
            _resizeDebounce.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_currentRenderingHandler != null)
            {
                CompositionTarget.Rendering -= _currentRenderingHandler;
                _currentRenderingHandler = null;
            }
            autoSlideTimer?.Stop();
            autoSlideTimer = null;
            _resizeDebounce?.Stop();
        }

        private void SetCardWidth()
        {
            if (_scrollViewer == null) return;
            var availableWidth = _scrollViewer.ViewportWidth;
            if (availableWidth <= 0) return;
            const double MinTileWidth = 180;
            for (var targetVisible = 6; targetVisible >= 1; targetVisible--)
            {
                var idealWidth = availableWidth / targetVisible;
                idealWidth -= ViewBoxMargin.Left + ViewBoxMargin.Right;
                if (idealWidth >= MinTileWidth || targetVisible == 1)
                {
                    itemsPerPage = targetVisible;
                    ViewBoxWidth = idealWidth;
                    break;
                }
            }
            var totalItems = GamesItemsView.Items.Count;
            CanScrollPages = totalItems > itemsPerPage;
            if (totalItems == 0) return;
            cardStartIndex = 0;
            ScrollToIndex(cardStartIndex);
        }

        private void StopAutoSlide()
        {
            isAutoSliding = false;
            autoSlideTimer?.Stop();
            autoSlideTimer = null;
        }

        private void UpdateAutoSlideState()
        {
            autoSlideTimer?.Stop();
            autoSlideTimer = null;

            if (IsAutoSlideEnabled && isAutoSliding && GamesItemsView?.Items != null && GamesItemsView.Items.Count > itemsPerPage)
            {
                autoSlideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoSlideInterval) };
                autoSlideTimer.Tick += (s, e) =>
                {
                    if (isAutoSliding) GoToNext();
                };
                autoSlideTimer.Start();
            }
        }

        #endregion Layout Setup

        #region Paging & Animation

        private static ScrollViewer? GetScrollViewer(DependencyObject o)
        {
            if (o is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var result = GetScrollViewer(VisualTreeHelper.GetChild(o, i));
                if (result != null) return result;
            }
            return null;
        }

        private void GoToNext()
        {
            var totalItems = GamesItemsView.Items.Count;
            if (totalItems == 0) return;

            var maxIndex = Math.Max(0, totalItems - itemsPerPage);

            if (cardStartIndex >= maxIndex)
            {
                cardStartIndex = 0;
            }
            else
            {
                cardStartIndex += itemsPerPage;
                if (cardStartIndex > maxIndex) cardStartIndex = maxIndex;
            }

            ScrollToIndex(cardStartIndex, animate: true);
        }

        private void GoToPrevious()
        {
            var totalItems = GamesItemsView.Items.Count;
            if (totalItems == 0) return;

            var maxIndex = Math.Max(0, totalItems - itemsPerPage);

            if (cardStartIndex <= 0)
            {
                cardStartIndex = maxIndex;
            }
            else
            {
                cardStartIndex -= itemsPerPage;
                if (cardStartIndex < 0) cardStartIndex = 0;
            }

            ScrollToIndex(cardStartIndex, animate: true);
        }

        private void ScrollToIndex(int index, bool animate = true)
        {
            if (_scrollViewer == null)
            {
                GamesItemsView.ApplyTemplate();
                _scrollViewer = GetScrollViewer(GamesItemsView);
            }

            if (_scrollViewer == null) return;

            var targetOffset = index * (ViewBoxWidth + ViewBoxMargin.Left + ViewBoxMargin.Right);

            if (_currentRenderingHandler != null)
            {
                System.Windows.Media.CompositionTarget.Rendering -= _currentRenderingHandler;
                _currentRenderingHandler = null;
            }

            if (!animate)
            {
                _scrollViewer.ScrollToHorizontalOffset(targetOffset);
                return;
            }

            double startOffset = _scrollViewer.HorizontalOffset;
            double delta = targetOffset - startOffset;

            if (Math.Abs(delta) < 0.1)
            {
                _scrollViewer.ScrollToHorizontalOffset(targetOffset);
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var duration = TimeSpan.FromMilliseconds(300);

            void OnRendering(object? sender, EventArgs e)
            {
                if (_scrollViewer == null) return;

                var elapsed = stopwatch.Elapsed;
                double t = elapsed.TotalMilliseconds / duration.TotalMilliseconds;

                if (t >= 1.0)
                {
                    _scrollViewer.ScrollToHorizontalOffset(targetOffset);
                    CompositionTarget.Rendering -= OnRendering;
                    _currentRenderingHandler = null;
                    return;
                }

                t = 1.0 - Math.Pow(1.0 - t, 3);
                _scrollViewer.ScrollToHorizontalOffset(startOffset + delta * t);
            }

            _currentRenderingHandler = OnRendering;
            CompositionTarget.Rendering += OnRendering;
        }

        #endregion Paging & Animation
    }
}
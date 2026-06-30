using Microsoft.Xaml.Behaviors;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BV6Tools.Behaviors
{
    public class MarqueeOnOverflowBehavior : Behavior<TextBlock>
    {
        public static readonly DependencyProperty EnableMarqueeImmediatelyProperty =
            DependencyProperty.Register(nameof(EnableMarqueeImmediately), typeof(bool), typeof(MarqueeOnOverflowBehavior),
                new PropertyMetadata(false));

        private Canvas? _canvas;
        private Storyboard? _storyboard;
        private DependencyPropertyDescriptor? _textDescriptor;

        public bool EnableMarqueeImmediately
        {
            get => (bool)GetValue(EnableMarqueeImmediatelyProperty);
            set => SetValue(EnableMarqueeImmediatelyProperty, value);
        }

        public void StartScrolling()
        {
            if (_canvas == null || AssociatedObject == null) return;

            if (_canvas.ActualWidth == 0)
                _canvas.UpdateLayout();

            AssociatedObject.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var textWidth = AssociatedObject.DesiredSize.Width;
            var canvasWidth = _canvas.ActualWidth;

            if (canvasWidth == 0) return;

            if (textWidth <= canvasWidth)
            {
                Canvas.SetLeft(AssociatedObject, 0);
                return;
            }

            var distance = textWidth - canvasWidth;
            var duration = Math.Max(distance / 40.0, 1.5);
            const double pauseStart = 2.0;
            const double pauseEnd = 2.0;

            Canvas.SetLeft(AssociatedObject, 0);

            var keyFrames = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            keyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pauseStart))));
            keyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(-distance,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pauseStart + duration))));
            keyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(pauseStart + duration + pauseEnd))));

            Storyboard.SetTarget(keyFrames, AssociatedObject);
            Storyboard.SetTargetProperty(keyFrames, new PropertyPath("(Canvas.Left)"));

            _storyboard = new Storyboard();
            _storyboard.Children.Add(keyFrames);
            _storyboard.Begin();
        }

        public void StopScrolling()
        {
            if (_storyboard != null)
            {
                _storyboard.Stop();
                _storyboard.Remove(AssociatedObject);
                _storyboard = null;
            }

            if (AssociatedObject != null)
            {
                AssociatedObject.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(AssociatedObject, 0);
            }
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += OnLoaded;
            AssociatedObject.Unloaded += OnUnloaded;

            _textDescriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            _textDescriptor?.AddValueChanged(AssociatedObject, OnTextChanged);
        }

        protected override void OnDetaching()
        {
            CleanUp();
            _textDescriptor?.RemoveValueChanged(AssociatedObject, OnTextChanged);

            if (AssociatedObject != null)
            {
                AssociatedObject.Loaded -= OnLoaded;
                AssociatedObject.Unloaded -= OnUnloaded;
            }

            base.OnDetaching();
        }

        private void CleanUp()
        {
            StopScrolling();
            if (_canvas == null) return;
            _canvas.SizeChanged -= OnCanvasSizeChanged;
            _canvas = null;
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged && e.PreviousSize.Width > 0) return;
            Refresh();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CleanUp();

            _canvas = VisualTreeHelper.GetParent(AssociatedObject) as Canvas;
            if (_canvas == null) return;

            _canvas.SizeChanged += OnCanvasSizeChanged;

            if (EnableMarqueeImmediately)
                StartScrolling();
        }

        private void OnTextChanged(object? sender, EventArgs e) => Refresh();

        private void OnUnloaded(object sender, RoutedEventArgs e) => StopScrolling();

        private void Refresh()
        {
            StopScrolling();
            StartScrolling();
        }
    }
}
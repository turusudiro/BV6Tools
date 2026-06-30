using BV6Tools.Behaviors;
using Microsoft.Xaml.Behaviors;
using System.Windows.Controls;
using System.Windows.Documents;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace BV6Tools.Views.Controls;

public partial class MarqueeTextBlock : UserControl
{
    public static readonly DependencyProperty CanvasMarginProperty =
        DependencyProperty.Register(nameof(CanvasMargin), typeof(Thickness), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public new static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(MarqueeTextBlock));

    public new static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(MarqueeTextBlock));

    public static readonly DependencyProperty TextProperty =
                    DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public MarqueeTextBlock()
    {
        InitializeComponent();

        Unloaded += (s, e) =>
        {
            var textBlock = FindName("ScrollingTextBlock") as TextBlock;
            if (textBlock == null) return;

            var behaviors = Interaction.GetBehaviors(textBlock);
            foreach (var behavior in behaviors)
            {
                if (behavior is MarqueeOnOverflowBehavior marquee)
                {
                    marquee.StopScrolling();
                }
            }
        };
    }

    public Thickness CanvasMargin
    {
        get => (Thickness)GetValue(CanvasMarginProperty);
        set => SetValue(CanvasMarginProperty, value);
    }

    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public new FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
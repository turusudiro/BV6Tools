using System.Windows.Controls;
using System.Windows.Input;

namespace BV6Tools.Views.Controls;

/// <summary>
///     Interaction logic for GameCard.xaml
/// </summary>
public partial class GameCard : UserControl
{
    public static readonly DependencyProperty AddButtonVisibleProperty =
        DependencyProperty.Register(nameof(AddButtonVisible), typeof(bool), typeof(GameCard), new PropertyMetadata(true));

    public static readonly DependencyProperty AddCommandParameterProperty =
    DependencyProperty.Register(nameof(AddCommandParameter), typeof(object), typeof(GameCard));

    public static readonly DependencyProperty AddCommandProperty =
    DependencyProperty.Register(nameof(AddCommand), typeof(ICommand), typeof(GameCard));

    public static readonly DependencyProperty DownloadButtonVisibleProperty =
    DependencyProperty.Register(nameof(DownloadButtonVisible), typeof(bool), typeof(GameCard), new PropertyMetadata(true));

    public static readonly DependencyProperty DownloadCommandParameterProperty =
    DependencyProperty.Register(nameof(DownloadCommandParameter), typeof(object), typeof(GameCard));

    public static readonly DependencyProperty DownloadCommandProperty =
    DependencyProperty.Register(nameof(DownloadCommand), typeof(ICommand), typeof(GameCard));

    public static readonly DependencyProperty ClickCommandProperty =
        DependencyProperty.Register(
            nameof(ClickCommand),
            typeof(ICommand),
            typeof(GameCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RequestBringIntoViewCommandParameterProperty =
                        DependencyProperty.Register(
            nameof(RequestBringIntoViewCommandParameter),
            typeof(object),
            typeof(GameCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RequestBringIntoViewCommandProperty =
        DependencyProperty.Register(
            nameof(RequestBringIntoViewCommand),
            typeof(ICommand),
            typeof(GameCard),
            new PropertyMetadata(null)
        );

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(GameCard),
            new PropertyMetadata(string.Empty)
        );

    public static readonly DependencyProperty ViewboxMarginProperty =
        DependencyProperty.Register(nameof(ViewBoxMargin), typeof(Thickness), typeof(GameCard),
            new PropertyMetadata(new Thickness(2)));

    public static readonly DependencyProperty ViewBoxWidthProperty =
        DependencyProperty.Register(nameof(ViewBoxWidth), typeof(double), typeof(GameCard),
            new PropertyMetadata(200.0));

    public GameCard()
    {
        InitializeComponent();
    }

    public bool AddButtonVisible
    {
        get => (bool)GetValue(AddButtonVisibleProperty);
        set => SetValue(AddButtonVisibleProperty, value);
    }

    public ICommand AddCommand
    {
        get => (ICommand)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public object AddCommandParameter
    {
        get => GetValue(AddCommandParameterProperty);
        set => SetValue(AddCommandParameterProperty, value);
    }

    public bool DownloadButtonVisible
    {
        get => (bool)GetValue(AddButtonVisibleProperty);
        set => SetValue(AddButtonVisibleProperty, value);
    }

    public ICommand DownloadCommand
    {
        get => (ICommand)GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public object DownloadCommandParameter
    {
        get => GetValue(AddCommandParameterProperty);
        set => SetValue(AddCommandParameterProperty, value);
    }

    public ICommand ClickCommand
    {
        get => (ICommand)GetValue(ClickCommandProperty);
        set => SetValue(ClickCommandProperty, value);
    }

    public ICommand RequestBringIntoViewCommand
    {
        get => (ICommand)GetValue(RequestBringIntoViewCommandProperty);
        set => SetValue(RequestBringIntoViewCommandProperty, value);
    }

    public object RequestBringIntoViewCommandParameter
    {
        get => GetValue(RequestBringIntoViewCommandParameterProperty);
        set => SetValue(RequestBringIntoViewCommandParameterProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Thickness ViewBoxMargin
    {
        get => (Thickness)GetValue(ViewboxMarginProperty);
        set => SetValue(ViewboxMarginProperty, value);
    }

    public double ViewBoxWidth
    {
        get => (double)GetValue(ViewBoxWidthProperty);
        set => SetValue(ViewBoxWidthProperty, value);
    }
}
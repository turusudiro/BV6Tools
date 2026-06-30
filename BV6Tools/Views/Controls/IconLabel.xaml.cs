using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.Views.Controls
{
    /// <summary>
    /// Interaction logic for IconLabel.xaml
    /// </summary>
    public partial class IconLabel : UserControl
    {
        public static readonly DependencyProperty FilledProperty =
            DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(IconLabel));

        public static readonly DependencyProperty LabelFontSizeProperty =
            DependencyProperty.Register(nameof(LabelFontSize), typeof(double), typeof(IconLabel),
                new PropertyMetadata(14.0));

        public static readonly DependencyProperty LabelFontWeightProperty =
            DependencyProperty.Register(nameof(LabelFontWeight), typeof(FontWeight), typeof(IconLabel),
                new PropertyMetadata(FontWeights.Normal));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(IconLabel));

        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(IconLabel),
                new PropertyMetadata(Orientation.Horizontal));

        public static readonly DependencyProperty SymbolFontSizeProperty =
            DependencyProperty.Register(nameof(SymbolFontSize), typeof(double), typeof(IconLabel),
                new PropertyMetadata(16.0));

        public static readonly DependencyProperty SymbolFontWeightProperty =
            DependencyProperty.Register(nameof(SymbolFontWeight), typeof(FontWeight), typeof(IconLabel),
                new PropertyMetadata(FontWeights.Normal));

        public static readonly DependencyProperty SymbolProperty =
            DependencyProperty.Register(nameof(Symbol), typeof(SymbolRegular), typeof(IconLabel));

        public IconLabel()
        {
            InitializeComponent();
        }

        public bool Filled
        {
            get => (bool)GetValue(FilledProperty);
            set => SetValue(FilledProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public double LabelFontSize
        {
            get => (double)GetValue(LabelFontSizeProperty);
            set => SetValue(LabelFontSizeProperty, value);
        }

        public FontWeight LabelFontWeight
        {
            get => (FontWeight)GetValue(LabelFontWeightProperty);
            set => SetValue(LabelFontWeightProperty, value);
        }

        public Orientation Orientation
        {
            get => (Orientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        public SymbolRegular Symbol
        {
            get => (SymbolRegular)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        public double SymbolFontSize
        {
            get => (double)GetValue(SymbolFontSizeProperty);
            set => SetValue(SymbolFontSizeProperty, value);
        }

        public FontWeight SymbolFontWeight
        {
            get => (FontWeight)GetValue(SymbolFontWeightProperty);
            set => SetValue(SymbolFontWeightProperty, value);
        }
    }
}
using System.Globalization;
using System.Windows.Data;

namespace BV6Tools.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bValue = value is bool b && b;
        var invert = false;

        if (parameter != null) _ = bool.TryParse(parameter.ToString(), out invert);

        return (invert ? !bValue : bValue) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is Visibility v && v == Visibility.Visible;
        var invert = false;

        if (parameter != null) _ = bool.TryParse(parameter.ToString(), out invert);

        return invert ? !isVisible : isVisible;
    }
}
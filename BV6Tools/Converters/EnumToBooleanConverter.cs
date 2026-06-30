using System.Globalization;
using System.Windows.Data;

namespace BV6Tools.Converters;

internal class EnumToBooleanConverter : IValueConverter
{
    // Source - https://stackoverflow.com/a
    // Posted by Scott, modified by community. See post 'Timeline' for change history
    // Retrieved 2025-12-08, License - CC BY-SA 4.0

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return false;
        return value.Equals(parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value.Equals(true) ? parameter : Binding.DoNothing;
    }
}
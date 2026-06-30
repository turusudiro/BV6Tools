using System.Globalization;
using System.Windows.Data;

namespace BV6Tools.Converters;

public class GreaterThanOrEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string paramStr && int.TryParse(paramStr, out var threshold))
        {
            if (value is int intValue) return intValue >= threshold;

            if (value is uint uintValue) return uintValue >= (uint)threshold;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
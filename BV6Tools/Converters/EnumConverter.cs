using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;

namespace BV6Tools.Converters
{
    // Source - https://stackoverflow.com/a/20291440
    // Posted by Suresh, modified by community. See post 'Timeline' for change history
    // Retrieved 2026-06-04, License - CC BY-SA 4.0

    public class EnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            return GetDescription((Enum)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value;
        }

        public static string GetDescription(Enum en)
        {
            Type type = en.GetType();
            MemberInfo[] memInfo = type.GetMember(en.ToString());
            if (memInfo != null && memInfo.Length > 0)
            {
                object[] attrs = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    return ((DescriptionAttribute)attrs[0]).Description;
                }
            }
            return en.ToString();
        }
    }

}

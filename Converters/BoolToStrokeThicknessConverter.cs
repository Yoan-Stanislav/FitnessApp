using System.Globalization;
using Microsoft.Maui.Controls;

namespace FitnessApp.Converters;

public class BoolToStrokeThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive ? 5 : 3; // Thicker border for active exercise
        }
        return 3;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

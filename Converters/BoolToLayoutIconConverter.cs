using Microsoft.Maui.Graphics;
using System.Globalization;

namespace FitnessApp.Converters;

public class BoolToLayoutIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isGridView)
        {
            // Return null for now to avoid missing icon file crashes
            return null;
        }
        return null; // Default to null
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

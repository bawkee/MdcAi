namespace Sala.Extensions.WinUI;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string stringValue && string.IsNullOrEmpty(stringValue))
            value = null;
        return (Invert ? value != null : value == null) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => 
        throw new NotImplementedException();
}
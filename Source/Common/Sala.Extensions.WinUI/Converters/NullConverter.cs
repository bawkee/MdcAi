namespace Sala.Extensions.WinUI;

using System.Windows;
using Microsoft.UI.Xaml.Data;

/// <summary>
/// A catch-all null converter you can use when you're too lazy to use bool.
/// </summary>
public class NullConverter : IValueConverter
{
    public object Null { get; set; } = DependencyProperty.UnsetValue;
    public object NotNull { get; set; } = DependencyProperty.UnsetValue;

    public virtual object Convert(object value, Type targetType, object parameter, string culture)
    {
        if (value is string stringValue && string.IsNullOrEmpty(stringValue))
            value = null; // Kind of sneaky but 99.99% of time you'll want this
        return value is not null ? NotNull : Null;
    }

    public virtual object ConvertBack(object value, Type targetType, object parameter, string culture) =>
        throw new InvalidOperationException();
}
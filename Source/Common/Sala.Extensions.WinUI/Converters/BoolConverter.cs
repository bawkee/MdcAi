namespace Sala.Extensions.WinUI;

using Microsoft.UI.Xaml.Data;

public class BoolConverter<T> : IValueConverter
{
    public BoolConverter(T trueValue, T falseValue)
    {
        True = trueValue;
        False = falseValue;
    }

    public T True { get; set; }
    public T False { get; set; }
    public bool Invert { get; set; }

    public virtual object Convert(object value, Type targetType, object parameter, string culture) =>
        value is true ? (Invert ? False : True) : (Invert ? True : False);

    public virtual object ConvertBack(object value, Type targetType, object parameter, string culture) =>
        value is T tvalue && EqualityComparer<T>.Default.Equals(tvalue, Invert ? False : True);
}
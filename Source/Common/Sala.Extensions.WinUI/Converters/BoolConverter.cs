namespace Sala.Extensions.WinUI;

using Microsoft.UI.Xaml.Data;
using System.Windows;

/// <summary>
/// You can use this bool converter for anything, colors, brushes, visibility, opacity, thickness, etc. 
/// For most of 10 years I just used this converter and occasionally the null one.
/// </summary>
public class BoolConverter : IValueConverter
{
    public object True { get; set; } = DependencyProperty.UnsetValue;
    public object Default { get; set; } = DependencyProperty.UnsetValue;
    public bool Invert { get; set; }

    public virtual object Convert(object value, Type targetType, object parameter, string culture) =>
        value is true ? Invert ? Default : True : Invert ? True : Default;

    public virtual object ConvertBack(object value, Type targetType, object parameter, string culture) =>
        value.Equals(Invert ? Default : True);
}

/// <summary>
/// A generic base class for converters you'd be using very often.
/// </summary>
public abstract class BoolConverter<T> : IValueConverter
{
    protected BoolConverter(T trueValue = default, T falseValue = default)
    {
        True = trueValue;
        False = falseValue;
    }

    public T True { get; set; }
    public T False { get; set; }
    public bool Invert { get; set; }

    public virtual object Convert(object value, Type targetType, object parameter, string culture) =>
        value is true ? Invert ? False : True : Invert ? True : False;

    public virtual object ConvertBack(object value, Type targetType, object parameter, string culture) =>
        value is T tvalue && EqualityComparer<T>.Default.Equals(tvalue, Invert ? False : True);
}

namespace MdcAi.Extensions.WinUI;

using Microsoft.UI.Xaml.Data;
using SalaTools.Core;

public class RoundDecimalConverter : IValueConverter
{
    public int Decimals { get; set; } = 2;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value.ChangeType<decimal>().Round(Decimals).ChangeType(targetType);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value.ChangeType(targetType);
}
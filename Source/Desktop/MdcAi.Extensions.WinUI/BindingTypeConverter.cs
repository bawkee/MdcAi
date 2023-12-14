namespace MdcAi.Extensions.WinUI;

using Microsoft.UI.Xaml.Data;
using SalaTools.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class BindingTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value.ChangeType(targetType);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value.ChangeType(targetType);
}

public class BindingFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string format)
            return string.Format(format, value);
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
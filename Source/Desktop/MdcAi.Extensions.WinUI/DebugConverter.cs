namespace MdcAi.Extensions.WinUI;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;

public class DebugConverter : IValueConverter
{
    public string Tag { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Debug.WriteLine($"DbgConvert(tag={Tag}, val={value}, targetType={targetType}, param={parameter})");
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        Debug.WriteLine($"DbgConvertBack(tag={Tag}, val={value}, targetType={targetType}, param={parameter})");
        return value;
    }
}
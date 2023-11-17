namespace Sala.Extensions.WinUI;

using Microsoft.UI.Xaml;

public sealed class BoolToVisibilityConverter : BoolConverter<Visibility>
{
    public BoolToVisibilityConverter() :
        base(Visibility.Visible, Visibility.Collapsed)
    { }
}
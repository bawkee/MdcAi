namespace MdcAi.Extensions.WinUI;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public static class PageExtensions
{
    public static void SelectPageInNavigationView(this Page page, string item)
    {
        var parent = VisualTreeHelper.GetParent(page.Frame);

        while (parent is { } something and not NavigationView)
            parent = VisualTreeHelper.GetParent(something);

        if (parent is NavigationView navigationView)
            navigationView.SelectedItem = navigationView
                                          .MenuItems
                                          .Cast<NavigationViewItem>()
                                          .FirstOrDefault(i => (string)i.Tag == item);
    }
}
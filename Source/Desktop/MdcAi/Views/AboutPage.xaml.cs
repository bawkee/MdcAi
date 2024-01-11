// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ReactiveMarbles.ObservableEvents;
using Windows.ApplicationModel.Store;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AboutPage
{
    public AboutPage() { InitializeComponent(); }

    private LicensesWindow _licInfoWnd;

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        _licInfoWnd ??= new();
        _licInfoWnd.Activate();
        _licInfoWnd.Events()
                  .Closed
                  .Take(1)
                  .Do(_ => _licInfoWnd = null)
                  .SubscribeSafe();
    }
}
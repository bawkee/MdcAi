// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views;

using MdcAi.ViewModels;
using Microsoft.UI;
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

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class RootPage
{
    public MainVm ViewModel { get; }

    public RootPage()
    {
        ViewModel = Services.GetRequired<MainVm>();

        InitializeComponent();

        Loaded += (s, e) =>
        {
            var wnd = ((App)Application.Current).Window;
            wnd.SetTitleBar(AppTitleBar);
        };
    }

    private void NavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedItem = (NavigationViewItem)args.SelectedItem;

        if (selectedItem == ChatNaviItem)
            NaviPivot.SelectedItem = ConversationPivotItem;

        if (args.IsSettingsSelected)
            NaviPivot.SelectedItem = SettingsPivotItem;
    }

    private void NavigationView_OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        if (sender.PaneDisplayMode == NavigationViewPaneDisplayMode.Top)
            VisualStateManager.GoToState(this, "Top", true);
        else
            VisualStateManager.GoToState(this,
                                         args.DisplayMode == NavigationViewDisplayMode.Minimal ?
                                             "Compact" :
                                             "Default",
                                         true);
    }
}
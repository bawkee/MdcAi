// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views;

using Castle.MicroKernel.Registration;
using MdcAi.ChatUI.ViewModels;
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
using System.Reactive.Concurrency;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using ReactiveMarbles.ObservableEvents;

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

        //CreateNavigationViewItems();

        Loaded += (s, e) =>
        {
            var wnd = ((App)Application.Current).Window;
            wnd.SetTitleBar(AppTitleBar);
        };
    }

    private void CreateNavigationViewItems()
    {
        var parentItem = new NavigationViewItem
        {
            Icon = new SymbolIcon(Symbol.Message),
            Content = "General",
            Name = "ParentItem"
        };

        for (var i = 0; i < 100; i++)
        {
            var item1 = new NavigationViewItem
            {
                Icon = new SymbolIcon(Symbol.Message),
                Content = $"Item {i}",
                Tag = parentItem // Keeping inline with your XAML, although binding would make more sense
            };

            parentItem.MenuItems.Add(item1);
        }

        NavigationViewControl.MenuItems.Add(parentItem);
    }

    private void NavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        //if (selectedItem == ChatNaviItem)
        //    NaviPivot.SelectedItem = ConversationPivotItem;

        if (args.IsSettingsSelected)
            NaviPivot.SelectedItem = SettingsPivotItem;
        else
        {
            if (args.SelectedItem is ConversationPreviewVm)
                NaviPivot.SelectedItem = ConversationPivotItem;
        }
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

    private void CategoryItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem { Tag: ConversationCategoryVm cat } item)
            return;

        if (ViewModel.Conversations.Items.First() == cat)
            RxApp.MainThreadScheduler.Schedule(
                // Hopefully this doesn't crash the delicate WinUI when there are hundreds of items... hopefully.
                () =>  item.IsExpanded = true);
    }
}

public class NavigationViewDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate CategoryTemplate { get; set; }
    public DataTemplate ItemTemplate { get; set; }

    public NavigationViewDataTemplateSelector()
    {
        // Get x:DataType from the DataTemplate
    }

    protected override DataTemplate SelectTemplateCore(object item) =>
        item switch
        {
            ConversationCategoryVm => CategoryTemplate,
            ConversationPreviewVm => ItemTemplate,
            _ => base.SelectTemplateCore(item)
        };
}
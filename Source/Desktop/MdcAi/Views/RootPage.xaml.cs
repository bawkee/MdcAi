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
    public RootPage()
    {
        ViewModel = AppServices.Container.Resolve<MainVm>();

        InitializeComponent();

        Loaded += (s, e) => ((App)Application.Current).Window.SetTitleBar(AppTitleBar);

        this.WhenActivated((disposables, viewModel) =>
        {
            viewModel.Conversations.RenameIntr.RegisterHandler(
                         async r =>
                         {
                             var dialogResult = await this.ShowTextInputDialog(
                                 "Rename conversation:",
                                 r.Input.Name,
                                 config => config.Validation = t => !string.IsNullOrEmpty(t));
                             r.SetOutput(dialogResult);
                         })
                     .DisposeWith(disposables);

            viewModel.Conversations.AddCategoryIntr.RegisterHandler(
                         async r =>
                         {
                             var dialogResult = await this.ShowTextInputDialog(
                                 "New Category Name:",
                                 null,
                                 config => config.Validation = t => !string.IsNullOrEmpty(t));
                             r.SetOutput(dialogResult);
                         })
                     .DisposeWith(disposables);

            NavigationViewControl
                .Events()
                .BackRequested
                .Select(_ => Unit.Default)
                .InvokeCommand(viewModel.Conversations.GoBackCmd)
                .DisposeWith(disposables);

            // Since we don't have anything fancy to show as a welcoming screen, just auto-select a new conversation
            // placeholder from the default 'General' category.
            viewModel.Conversations
                     .WhenAnyValue(vm => vm.Items.Count)
                     .Select(_ => viewModel.Conversations.Items
                                           .OfType<ConversationCategoryPreviewVm>()
                                           .FirstOrDefault(c => c.Id == "default"))
                     .WhereNotNull()
                     .Select(c => c.WhenAnyValue(vm => vm.Items.Count)
                                   .Select(_ => c.Items.FirstOrDefault(i => i.IsNewPlaceholder))
                                   .WhereNotNull())
                     .Switch()
                     .ObserveOnMainThread()
                     .Do(i => viewModel.Conversations.SelectedPreviewItem ??= i)
                     .Take(1)
                     .Subscribe()
                     .DisposeWith(disposables);

            viewModel.Conversations.GoToSettingsCmd = ReactiveCommand.Create(() =>
            {
                NavigationViewControl.SelectedItem =
                    NavigationViewControl.SettingsItem;
            });
        });
    }

    private void NavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
            NaviPivot.SelectedItem = SettingsPivotItem;
        else
            NaviPivot.SelectedItem = ConversationPivotItem;
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
        if (sender is not NavigationViewItem { Tag: ConversationCategoryPreviewVm cat } item)
            return;

        if (ViewModel.Conversations.Items.First() == cat)
            RxApp.MainThreadScheduler.Schedule(
                // Hopefully this doesn't crash the delicate WinUI when there are hundreds of items... hopefully.
                () => item.IsExpanded = true);
    }

    private void UndoDeleteBtn_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Observable.Return(Unit.Default)
                  .InvokeCommand(ViewModel.Conversations.UndoDeleteCmd);
    }
}

public class RootNaviDataTemplateSelector : DataTemplateSelector
{
    public DataTemplate CategoryTemplate { get; set; }
    public DataTemplate ItemTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) =>
        item switch
        {
            ConversationCategoryPreviewVm => CategoryTemplate,
            ConversationPreviewVm => ItemTemplate,
            _ => base.SelectTemplateCore(item)
        };
}

[DoNotRegister]
public class RootPageBase : ReactivePage<MainVm> { }
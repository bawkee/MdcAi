#region Copyright Notice

// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

#endregion

namespace MdcAi.Views;

using MdcAi.ChatUI.ViewModels;
using MdcAi.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Reactive.Concurrency;
using ReactiveMarbles.ObservableEvents;
using CommunityToolkit.WinUI;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class RootPage
{
    public RootPage()
    {
        ViewModel = AppServices.Container.Resolve<MainVm>();

        InitializeComponent();

        Loaded += (s, e) =>
        {
            ((App)Application.Current).Window.SetTitleBar(AppTitleBar);

            // Fixes the hamburger button being cut-off b u g
            NavigationViewControl.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        };

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
                     // This fixes the crashing, apparently not showing this at all won't fix it but showing it 
                     // 1 second later instead, fixes it. The stack overflow happens otherwise, when building the
                     // chat message linked list. Fuck. Me. WinUI is some piece of work.
                     .Throttle(TimeSpan.FromSeconds(1))
                     .ObserveOnMainThread()
                     .Do(i => viewModel.Conversations.SelectedPreviewItem ??= i)
                     .Take(1)
                     .Subscribe()
                     .DisposeWith(disposables);
           
            // Newly added category is supposed to be on the top so scroll to top
            viewModel.Conversations.AddCategoryCmd
                     .Throttle(TimeSpan.FromMilliseconds(500)) // Grace period for the thing to do the thing
                     .ObserveOnMainThread()
                     .Do(_ =>
                     {
                         // Could use https://learn.microsoft.com/en-us/windows/apps/design/controls/items-repeater#bringing-an-element-into-view
                         // but we're too lazy aren't we
                         var item = NavigationViewControl.ContainerFromMenuItem(NavigationViewControl.SelectedItem);
                         var scrollViewer = item?.FindAscendant<ScrollViewer>();
                         scrollViewer?.ChangeView(null, 0, null);
                     })
                     .SubscribeSafe()
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
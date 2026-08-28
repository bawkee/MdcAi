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

namespace MdcAi.ChatUI.Views;

using ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using RxUIExt.Windsor;
using OpenAiApi;
using RxUIExt.WinUI;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ConversationCategory
{
    public ConversationCategory()
    {
        InitializeComponent();

        this.WhenActivated((disposables, viewModel) =>
        {
            // Provider-grouped model dropdown. Selecting persists the category default.
            viewModel.Settings.WhenAnyValue(vm => vm.Models)
                              .WhereNotNull()
                              .Do(models =>
                              {
                                  var selectedId = viewModel.Settings.Model;
                                  var selectedModel = models.FirstOrDefault(m => m.ModelID == selectedId);
                                  ChatSettingModelDropdown.Content = ModelMenuFactory.LabelFor(selectedModel);
                                  var flyout = new MenuFlyout();
                                  foreach (var item in ModelMenuFactory.BuildProviderGroupedMenu(models, m =>
                                  {
                                      viewModel.Settings.Model = m.ModelID;
                                      ChatSettingModelDropdown.Content = ModelMenuFactory.LabelFor(m);
                                  }))
                                      flyout.Items.Add(item);
                                  ChatSettingModelDropdown.Flyout = flyout;
                              })
                              .SubscribeSafe()
                              .DisposeWith(disposables);

            viewModel.Settings.WhenAnyValue(vm => vm.Model)
                              .WhereNotNull()
                              .Do(m =>
                              {
                                  if (viewModel.Settings.Models?.FirstOrDefault(x => x.ModelID == m) is { } model)
                                      ChatSettingModelDropdown.Content = ModelMenuFactory.LabelFor(model);
                              })
                              .SubscribeSafe()
                              .DisposeWith(disposables);

            viewModel.RenameIntr.RegisterHandler(
                         async r =>
                         {
                             var dialogResult = await this.ShowTextInputDialog(
                                 "Rename Category:",
                                 r.Input,
                                 config => config.Validation = t => !string.IsNullOrEmpty(t));
                             r.SetOutput(dialogResult);
                         })
                     .DisposeWith(disposables);
        });
    }

    private void IconTemplate_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.Icons.SelectedItem is { } prevItem)
        {
            var prevIdx = ViewModel.Icons.IconsView.IndexOf(prevItem);
            if (IconsRepeater.TryGetElement(prevIdx) is { } prevElem)
                MoveToSelectionState(prevElem, false);
        }

        var itemIndex = IconsRepeater.GetElementIndex(sender as UIElement);
        ViewModel.Icons.SelectedItem = itemIndex == -1 ? null : (IconVm)ViewModel.Icons.IconsView[itemIndex];
        MoveToSelectionState(sender as UIElement, true);
    }

    private void IconsRepeater_OnElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        var newItem = ViewModel.Icons.IconsView[args.NewIndex];
        MoveToSelectionState(args.Element, newItem == ViewModel.Icons.SelectedItem);
    }

    private void IconsRepeater_OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        var newItem = ViewModel.Icons.IconsView[args.Index];
        MoveToSelectionState(args.Element, newItem == ViewModel.Icons.SelectedItem);
    }

    private static void MoveToSelectionState(UIElement previousItem, bool isSelected) =>
        VisualStateManager.GoToState(previousItem as Control, isSelected ? "Selected" : "Default", false);
}

[DoNotRegister]
public class ConversationCategoryBase : ReactivePage<ConversationCategoryVm> { }
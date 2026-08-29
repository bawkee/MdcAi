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
            // Provider-grouped model dropdown + its effort dropdown. Selecting persists the
            // category default; a model change clamps the effort to the new model's levels.
            viewModel.Settings.WhenAnyValue(vm => vm.Models, vm => vm.Model, vm => vm.Effort)
                              .Where(t => t.Item1 != null)
                              .Do(_ => BuildDropdowns(viewModel))
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

    // Builds both the model dropdown and its effort dropdown for the category's settings.
    // The effort dropdown targets whatever model the category default currently is; a model
    // change here clamps the stored effort to the new model's supported levels (or clears
    // it for effort-less models).
    private void BuildDropdowns(ConversationCategoryVm vm)
    {
        ClampSettingsEffort(vm);

        var models = vm.Settings.Models ?? Array.Empty<AiModel>();
        var selectedModel = models.FirstOrDefault(m => m.ModelID == vm.Settings.Model);
        ChatSettingModelDropdown.Content = ModelMenuFactory.LabelFor(selectedModel);
        var modelFlyout = new MenuFlyout();
        foreach (var item in ModelMenuFactory.BuildProviderGroupedMenu(
                     models, m => vm.Settings.Model = m.ModelID))
            modelFlyout.Items.Add(item);
        ChatSettingModelDropdown.Flyout = modelFlyout;

        var efforts = selectedModel?.SupportedEfforts;
        ChatSettingEffortDropdown.Content = vm.Settings.Effort == null ? "Effort" : $"Effort: {vm.Settings.Effort}";
        ChatSettingEffortDropdown.IsEnabled = efforts is { Length: > 0 };
        var effortFlyout = new MenuFlyout();
        if (efforts != null)
        {
            foreach (var level in efforts)
            {
                var item = new MenuFlyoutItem { Text = level };
                item.Click += (_, _) => vm.Settings.Effort = level;
                effortFlyout.Items.Add(item);
            }
        }
        ChatSettingEffortDropdown.Flyout = effortFlyout;
    }

    // Keep the stored effort default valid for the stored model: clamp to the level closest
    // to medium when missing/invalid, null it entirely for effort-less models.
    private static void ClampSettingsEffort(ConversationCategoryVm vm)
    {
        var supported = vm.Settings.Models?.FirstOrDefault(m => m.ModelID == vm.Settings.Model)?.SupportedEfforts;

        if (supported is not { Length: > 0 })
            vm.Settings.Effort = null;
        else if (!supported.Contains(vm.Settings.Effort, StringComparer.OrdinalIgnoreCase))
            vm.Settings.Effort = AiEffort.ClosestToMedium(supported);
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
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views;

using MdcAi.ChatUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using RxUIExt.Windsor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using OpenAiApi;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ConversationCategory
{
    public ConversationCategory()
    {
        this.InitializeComponent();

        this.WhenActivated((disposables, viewModel) =>
        {
            viewModel.WhenAnyValue(vm => vm.Settings.Models)
                     .WhereNotNull()
                     .Do(models =>
                     {
                         ChatSettingModelDropdown.ClearValue(Selector.SelectedValueProperty);
                         ChatSettingModelDropdown.ItemsSource = models;
                         ChatSettingModelDropdown.SelectedValuePath = nameof(AiModel.ModelID);
                         BindingOperations.SetBinding(
                             ChatSettingModelDropdown,
                             Selector.SelectedValueProperty,
                             new Binding
                             {
                                 Path = new("Settings.Model"),
                                 Mode = BindingMode.TwoWay
                             });
                     })
                     .SubscribeSafe()
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
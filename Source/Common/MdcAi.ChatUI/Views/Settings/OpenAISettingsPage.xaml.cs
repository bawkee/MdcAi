// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViewModels;
using System;
using RxUIExt.Windsor;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class OpenAISettingsPage
{
    public OpenAISettingsPage()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            if (string.IsNullOrEmpty(ViewModel.ApiKeys))
                apiExpander.IsExpanded = true;
        });
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e) 
    {
        var dialog = new ContentDialog
        {
            Content = "This will remove your API keys from this app! Make sure you have them saved somewhere else if this is the only place you used them.",
            XamlRoot = XamlRoot,
            Title = "API Keys 🔑",            
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ApiKeys = null;
    }
}

[DoNotRegister]
public class OpenAISettingsPageBase : ReactivePage<OpenAiSettingsVm> { }

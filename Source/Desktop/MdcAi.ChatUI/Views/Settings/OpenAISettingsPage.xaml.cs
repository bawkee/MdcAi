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
                ApiExpander.IsExpanded = true;
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

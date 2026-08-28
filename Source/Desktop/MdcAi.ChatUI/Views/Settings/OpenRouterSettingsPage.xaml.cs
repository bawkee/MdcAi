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
/// OpenRouter API-access settings section: key + optional attribution fields.
/// </summary>
public sealed partial class OpenRouterSettingsPage
{
    public OpenRouterSettingsPage()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            if (string.IsNullOrEmpty(ViewModel.ApiKey))
                ApiExpander.IsExpanded = true;
        });
    }

    private async void RemoveKey_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Content = "This will remove your OpenRouter API key from this app! Make sure you have it saved somewhere else if this is the only place you used it.",
            XamlRoot = XamlRoot,
            Title = "API Keys 🔑",
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ApiKey = null;
    }
}

[DoNotRegister]
public class OpenRouterSettingsPageBase : ReactivePage<OpenRouterSettingsVm> { }
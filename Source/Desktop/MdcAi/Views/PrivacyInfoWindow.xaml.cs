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
using System.Windows.Controls;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using CommunityToolkit.WinUI.UI.Controls;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PrivacyInfoWindow : ILogging
{
    public PrivacyInfoWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        var appWindow = this.GetAppWindow();
        appWindow.Resize(new()
        {
            Width = 600,
            Height = appWindow.Size.Height
        });
    }

    private async void PrivacyInfoWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var privacyMd = await StorageFile.GetFileFromApplicationUriAsync(
            new("ms-appx:///Assets/PrivacyPolicy.md"));

        MdTextBlock.Text = await FileIO.ReadTextAsync(privacyMd);
    }

    private void MdTextBlock_OnLinkClicked(object sender, LinkClickedEventArgs e) =>
        ShellUtil.StartUrl(e.Link);
}
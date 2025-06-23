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

using System;
using ViewModels;
using ReactiveMarbles.ObservableEvents;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow
{
    public MainVm ViewModel { get; }

    public MainWindow()
    {
        ViewModel = AppServices.Container.Resolve<MainVm>();

        InitializeComponent();

        SetIcon("Assets/Icon.ico");

        Title = "MDC AI";

        ExtendsContentIntoTitleBar = true;

        this.Events()
            .Activated
            .Take(1)
            .Do(_ => ViewModel.Activator.Activate())
            .SubscribeSafe();        

        PrivacyInfoWindow privacyInfoWnd = null;

        ViewModel.Settings.ShowPrivacyStatementCmd =
            ReactiveCommand.Create(() =>
            {
                privacyInfoWnd ??= new();
                privacyInfoWnd.Activate();
                privacyInfoWnd.Events()
                              .Closed
                              .Take(1)
                              .Do(_ => privacyInfoWnd = null)
                              .SubscribeSafe();
            });

        ViewModel.Settings.ShowAboutCmd =
            ReactiveCommand.CreateFromTask(async () => { await AboutDialog.ShowAsync(); });
    }

    private void SetIcon(string iconName)
    {
        string path = AppContext.BaseDirectory;
        //string path = Package.Current.InstalledPath;
        string iconPath = Path.Combine(path, iconName);
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
    }

}
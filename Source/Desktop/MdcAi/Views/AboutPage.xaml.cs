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
using ReactiveMarbles.ObservableEvents;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AboutPage
{
    public AboutPage() { InitializeComponent(); }

    private LicensesWindow _licInfoWnd;

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        _licInfoWnd ??= new();
        _licInfoWnd.Activate();
        _licInfoWnd.Events()
                  .Closed
                  .Take(1)
                  .Do(_ => _licInfoWnd = null)
                  .SubscribeSafe();
    }
}
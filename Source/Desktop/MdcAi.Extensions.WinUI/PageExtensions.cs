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

namespace MdcAi.Extensions.WinUI;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public static class PageExtensions
{
    public static void SelectPageInNavigationView(this Page page, string item)
    {
        var parent = VisualTreeHelper.GetParent(page.Frame);

        while (parent is { } something and not NavigationView)
            parent = VisualTreeHelper.GetParent(something);

        if (parent is NavigationView navigationView)
            navigationView.SelectedItem = navigationView
                                          .MenuItems
                                          .Cast<NavigationViewItem>()
                                          .FirstOrDefault(i => (string)i.Tag == item);
    }
}
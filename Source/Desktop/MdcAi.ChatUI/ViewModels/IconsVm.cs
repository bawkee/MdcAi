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

namespace MdcAi.ChatUI.ViewModels;

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Windows.Storage;
using CommunityToolkit.WinUI.UI;

public class IconsVm : ViewModel
{
    [Reactive] public ObservableCollection<IconVm> Icons { get; private set; }
    [Reactive] public AdvancedCollectionView IconsView { get; private set; }
    [Reactive] public IconVm SelectedItem { get; set; }
    [Reactive] public string Filter { get; set; } = "";
    public ReactiveCommand<Unit, Unit> LoadIcons { get; }

    public IconsVm()
    {
        this.WhenAnyValue(vm => vm.Icons)
            .WhereNotNull()
            .Select(icons => new AdvancedCollectionView(icons)
            {
                Filter = icon => ((IconVm)icon).Name.EqualsWildcard($"*{Filter.Replace(' ', '*')}*", true)
            })
            .Do(view =>
            {
                view.SortDescriptions.Add(new(nameof(IconVm.Name), SortDirection.Ascending));
                IconsView = view;
            })
            .Subscribe();

        this.WhenAnyValue(vm => vm.Filter)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(250))
            .ObserveOnMainThread()
            .Do(_ => IconsView.RefreshFilter())
            .Subscribe();

        LoadIcons = ReactiveCommand.CreateFromTask(async ct =>
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///MdcAi.ChatUI/Assets/Icons.json"));
            var json = await FileIO.ReadTextAsync(file);
            var data = JsonConvert.DeserializeObject<IEnumerable<IconVm>>(json);

            if (ct.IsCancellationRequested)
                return;

            Icons = new(data);
        });
    }
}

public class IconVm
{
    public string Name { get; init; }
    public string Code { get; init; }
    public string Character => char.ConvertFromUtf32(Convert.ToInt32(Code, 16));
}
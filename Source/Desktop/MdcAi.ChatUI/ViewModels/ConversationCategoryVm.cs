﻿#region Copyright Notice
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

using Mapster;
using LocalDal;
using Microsoft.EntityFrameworkCore;

public class ConversationCategoryVm : ActivatableViewModel
{
    [Reactive] public string Name { get; set; }
    public string IdCategory { get; set; }
    [Reactive] public string IdSettings { get; set; }
    [Reactive] public string Description { get; set; }
    [Reactive] public string IconGlyph { get; set; }
    public IconsVm Icons { get; }
    public ChatSettingsVm Settings { get; }

    public ReactiveCommand<Unit, DbCategory> LoadCmd { get; }
    public ReactiveCommand<Unit, Unit> RenameCmd { get; }
    public Interaction<string, string> RenameIntr { get; } = new();

    public ConversationCategoryVm(IconsVm icons, SettingsVm globalSettings, ChatSettingsVm settings)
    {
        Icons = icons;
        Settings = settings;

        LoadCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.FromAsync(async () =>
                            {
                                await using var ctx = AppServices.GetUserProfileDb();
                                return ctx.Categories.Include(c => c.Settings).First(c => c.IdCategory == IdCategory);
                            })
                            .ObserveOnMainThread()
                            .Do(cat => cat.Adapt(this)));

        Icons.WhenAnyValue(vm => vm.SelectedItem)
             .WhereNotNull()
             .Do(s => IconGlyph = s.Character)
             .Subscribe();

        Activator.Activated
                 .Take(1)
                 .InvokeCommand(Icons.LoadIcons);

        Icons.LoadIcons
             .Where(_ => !string.IsNullOrEmpty(IconGlyph))
             .Do(_ => Icons.SelectedItem = Icons.Icons.FirstOrDefault(i => i.Character == IconGlyph))
             .SubscribeSafe();

        Activator.Activated.Take(1).InvokeCommand(LoadCmd);

        LoadCmd.Select(_ => this.WhenAnyValue(vm => vm.IdSettings))
               .Switch()
               .WhereNotNull()
               .Select(id => Settings.LoadCmd.Execute(id))
               .Switch()
               .SubscribeSafe();

        Activator.Activated.Take(1)
                 .Select(_ => globalSettings.WhenAnyValue(vm => vm.OpenAi.ApiKey)
                                            .Where(v => !string.IsNullOrEmpty(v))
                                            .Select(_ => Unit.Default))
                 .Switch()
                 .InvokeCommand(Settings.LoadModelsCmd);        

        Settings.LoadCmd
                .Select(_ => Settings.WhenAnyValue(vm => vm.IsDirty))
                .Switch()
                .Throttle(TimeSpan.FromSeconds(1))
                .Select(_ => Observable.Using(
                            AppServices.GetUserProfileDb,
                            ctx => Settings.SaveCmd.Execute(new()
                            {
                                Ctx = ctx
                            })))
                .Switch()
                .SubscribeSafe();

        LoadCmd.Select(_ => this.WhenAnyValue(vm => vm.IconGlyph)
                                .Skip(1))
               .Switch()
               .Throttle(TimeSpan.FromSeconds(1))
               .SelectMany(icon => Observable.FromAsync(
                               async () =>
                               {
                                   await using var ctx = AppServices.GetUserProfileDb();
                                   await ctx.Categories
                                            .Where(c => c.IdCategory == IdCategory)
                                            .ExecuteUpdateAsync(c => c.SetProperty(p => p.IconGlyph, icon));
                               }))
               .SubscribeSafe();

        RenameCmd = ReactiveCommand.CreateFromObservable(
            () => RenameIntr
                  .Handle(Name)
                  .Where(name => name != null && name != Name)
                  .Select(name => Observable
                                  .FromAsync(
                                      async () =>
                                      {
                                          await using var ctx = AppServices.GetUserProfileDb();
                                          await ctx.Categories
                                                   .Where(c => c.IdCategory == IdCategory)
                                                   .ExecuteUpdateAsync(c => c.SetProperty(p => p.Name, name));
                                      })
                                  .ObserveOnMainThread()
                                  .Do(_ => Name = name))
                  .Switch());


        //this.WhenActivated(disposables =>
        //{
        //    Debug.WriteLine($"Activated {Name}");
        //    Disposable.Create(() => Debug.WriteLine($"Deactivated {Name}")).DisposeWith(disposables);
        //});
    }

    static ConversationCategoryVm()
    {
        TypeAdapterConfig<ConversationCategoryVm, DbCategory>
            .NewConfig()
            .IgnoreMember((member, _) => !member.Type.IsBuiltInConvertibleType());
    }
}
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

using LocalDal;
using System.Linq.Expressions;
using CommunityToolkit.WinUI.UI;
using Microsoft.EntityFrameworkCore;

public class ConversationsVm : ViewModel
{
    [Reactive] public object SelectedItem { get; set; }
    [Reactive] public object SelectedPreviewItem { get; set; }
    public ObservableCollectionExtended<IConversationPreviewItem> Items { get; } = new();
    public AdvancedCollectionView ItemsView { get; private set; }
    public ObservableCollection<IConversationPreviewItem> TrashBin { get; } = new();
    public ObservableCollection<IConversationPreviewItem> SelectionHistory { get; } = new();
    [Reactive] public bool ShowUndoDelete { get; private set; }
    [Reactive] public bool IsBackEnabled { get; private set; }

    public ReactiveCommand<Unit, IConversationPreviewItem[]> LoadItems { get; }
    public ReactiveCommand<Unit, IConversationPreviewItem> UndoDeleteCmd { get; }
    public Interaction<ConversationPreviewVm, string> RenameIntr { get; } = new();
    public ReactiveCommand<Unit, Unit> AddCategoryCmd { get; }
    public Interaction<Unit, string> AddCategoryIntr { get; } = new();
    public ReactiveCommand<Unit, Unit> GoBackCmd { get; }    
    public ReactiveCommand<Unit, Unit> GoToSettingsCmd { get; set; }

    public ConversationsVm()
    {
        LoadItems = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var ctx = AppServices.GetUserProfileDb();

            // Sort categories by number of conversations (capped at 10) and then by the avg freshness of 
            // these conversations from the last 10 days. This should make sure that most used categories
            // come first but only if they're actually being used, and without mega-categories always being
            // on top.
            const string categoriesSql =
                @"SELECT * 
                    FROM Categories 
                    ORDER BY MIN(10, (SELECT COUNT(*) 
                                        FROM Conversations C 
                                        WHERE C.IdCategory = IdCategory) 
                             ) DESC, 
                             (SELECT DATETIME(AVG(STRFTIME('%s', CreatedTs)), 'unixepoch') AS AvgCreatedTs 
                              FROM Messages M 
                              WHERE M.CreatedTs >= DATETIME('now', '-10 day') AND 
                                    M.IdConversation = (SELECT IdConversation FROM Conversations C WHERE C.IdCategory = IdCategory)
                              ) DESC;";

            var categories = await ctx.Set<DbCategory>()
                                      .FromSqlRaw(categoriesSql)
                                      .ToArrayAsync();

            var convos = ctx.Conversations;

            var ret = categories.Select(async category =>
            {
                var cat = new ConversationCategoryPreviewVm
                {
                    Name = category.Name,
                    Id = category.IdCategory,
                    IconGlyph = category.IconGlyph,
                    Conversations = this
                };

                cat.Items.Load((await convos.Where(m => !m.IsTrash && m.IdCategory == cat.Id)
                                            .OrderByDescending(c => c.CreatedTs)
                                            .ToArrayAsync())
                               .Select(i => new ConversationPreviewVm
                               {
                                   Id = i.IdConversation,
                                   Name = i.Name,
                                   Category = cat,
                                   CreatedTs = i.CreatedTs
                               })
                               .Prepend(cat.CreateNewItemPlaceholder()));

                return cat;
            });

            return (await Task.WhenAll(ret)).Cast<IConversationPreviewItem>()
                                            .ToArray();
        });

        LoadItems.ObserveOnMainThread()
                 .Do(i =>
                 {
                     Items.Load(i);
                     // Have to recreate view, there is a sort issue where it does its own odd thing if there is no sort
                     ItemsView = new(Items, true)
                     {
                         Filter = item => !((IConversationPreviewItem)item).IsTrash
                     };
                     // The navigation pane gets all fucked up when sort happens, its safe to say it doesnt support this so 
                     // no live sorting
                     //ItemsView.SortDescriptions.Add(new("ItemsCount", SortDirection.Descending));
                     ItemsView.ObserveFilterProperty(nameof(IConversationPreviewItem.IsTrash));
                 })
                 .SubscribeSafe();

        this.WhenAnyValue(vm => vm.SelectedPreviewItem)
            .As<IConversationPreviewItem>()
            .Select(p => p == null ? Observable.Return((IConversationPreviewItem)null) : p.WhenAnyValue(vm => vm.FullItem))
            .Switch()
            .ObserveOnMainThread()
            .Do(p => SelectedItem = p)
            .SubscribeSafe();

        // Activation logic for preview items
        this.WhenAnyValue(vm => vm.SelectedPreviewItem)
            .As<ActivatableViewModel>()
            .WhereNotNull()
            .PairWithPrevious()
            .ObserveOnMainThread()
            .Do(p =>
            {
                p.Item1?.Activator.Deactivate();
                p.Item2?.Activator.Activate();
            })
            .SubscribeSafe();

        var trashBinHasItems = TrashBin.WhenAnyValue(t => t.Count)
                                       .Select(c => c > 0);

        UndoDeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                if (TrashBin.LastOrDefault() is not { } item)
                    return null;

                await using var ctx = AppServices.GetUserProfileDb();

                if (item is ConversationPreviewVm convo)
                    await ctx.Conversations
                             .Where(c => c.IdConversation == convo.Id)
                             .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, false));
                else if (item is ConversationCategoryPreviewVm cat)
                    await ctx.Categories
                             .Where(c => c.IdCategory == cat.Id)
                             .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, false));

                return item;
            },
            trashBinHasItems);

        UndoDeleteCmd.ObserveOnMainThread()
                     .Do(item =>
                     {
                         item.IsTrash = false;
                         TrashBin.Remove(item);
                     })
                     .SubscribeSafe();

        Observable.Merge(trashBinHasItems,
                         trashBinHasItems.Select(_ => false)
                                         .Throttle(TimeSpan.FromSeconds(5)))
                  .ObserveOnMainThread()
                  .Do(hasTrash => ShowUndoDelete = hasTrash)
                  .SubscribeSafe();

        AddCategoryCmd = ReactiveCommand.CreateFromObservable(
            () => AddCategoryIntr
                  .Handle()
                  .WhereNotNull()
                  .Select(name => Observable
                                  .FromAsync(
                                      async () =>
                                      {
                                          await using var ctx = AppServices.GetUserProfileDb();
                                          await using var trans = ctx.Database.BeginTransaction();
                                          var id = Guid.NewGuid().ToString();
                                          var newCategory = new DbCategory
                                          {
                                              IdCategory = id,
                                              IdSettings = id,
                                              Name = name
                                          };
                                          var newSetting = ctx.CreateDefaultChatSettings(id);
                                          ctx.ChatSettings.Add(newSetting);
                                          ctx.Categories.Add(newCategory);
                                          await ctx.SaveChangesAsync();
                                          trans.Commit();
                                          return newCategory.IdCategory;
                                      })
                                  .ObserveOnMainThread()
                                  .Select(id => new ConversationCategoryPreviewVm
                                  {
                                      Id = id,
                                      Name = name,
                                      Conversations = this
                                  })
                                  .Do(cat =>
                                  {
                                      cat.Items.Add(cat.CreateNewItemPlaceholder());
                                      Items.Insert(0, cat);
                                      SelectedPreviewItem = cat;
                                  })
                                  .Select(_ => Unit.Default))
                  .Switch());

        var goingBack = false;

        GoBackCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Using(
                () =>
                {
                    goingBack = true;
                    return Disposable.Create(() => goingBack = false);
                },
                _ => Observable
                     .Return(Unit.Default)
                     .Do(_ =>
                     {
                         // Linq won't work here since we have duplicate items (as expected)
                         for (var i = SelectionHistory.Count - 1; i >= 0; i--)
                         {
                             if (SelectionHistory[i].IsTrash)
                                 continue;
                             SelectedPreviewItem = SelectionHistory[i];
                             SelectionHistory.RemoveAt(i);
                             break;
                         }
                     })
            ),
            this.WhenAnyValue(vm => vm.IsBackEnabled));

        Observable.Merge(SelectionHistory.ObserveCollectionChanges(),
                         TrashBin.ObserveCollectionChanges(),
                         this.WhenAnyValue(vm => vm.SelectedPreviewItem))
                  .Do(_ =>
                  {
                      var lastItem = SelectionHistory.LastOrDefault(i => !i.IsTrash);
                      IsBackEnabled = lastItem != null && SelectedPreviewItem != lastItem;
                  })
                  .SubscribeSafe();

        // Maintain selection history
        this.WhenAnyValue(vm => vm.SelectedPreviewItem)
            .As<IConversationPreviewItem>()
            .WhereNotNull()
            .PairWithPrevious()            
            .Where(_ => !goingBack) // Prevent reentrancy when going back
            .Select(p => p.Item1)
            .WhereNotNull()
            .Do(item =>
            {
                SelectionHistory.Add(item);
                if (SelectionHistory.Count > 20) // Limit backlog to 20 items
                    SelectionHistory.RemoveAt(0);
            })
            .SubscribeSafe();
    }
}

public interface IConversationPreviewItem : IReactiveObject
{
    string Name { get; set; }
    object FullItem { get; }
    bool IsTrash { get; set; }
}

public static class ConversationItemExtensions
{
    public static IEnumerable<ConversationPreviewVm> GetConversations(this IEnumerable<ConversationCategoryPreviewVm> categories) =>
        categories.SelectMany(c => c.Items);
}
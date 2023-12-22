namespace MdcAi.ChatUI.ViewModels;

using LocalDal;
using System.Linq.Expressions;
using System.Windows.Input;
using CommunityToolkit.WinUI.UI;
using OpenAiApi;
using Microsoft.EntityFrameworkCore;
using MdcAi.ChatUI.Views;

// MPV TODO
// TODO: Simple category editor
// TODO: Suggestions and tips in new chat
// TODO: Hide the search box for now
// TODO: Make the back button work for convos

// TODO: Localisation
// TODO: Clean up names "Name" instead of "name"

public class ConversationsVm : ViewModel
{
    [Reactive] public object SelectedItem { get; set; }
    [Reactive] public object SelectedPreviewItem { get; set; }
    [Reactive] public ObservableCollectionExtended<IConversationPreviewItem> Items { get; private set; }
    public ObservableCollection<ConversationPreviewVm> TrashBin { get; } = new();
    public ReactiveCommand<Unit, IConversationPreviewItem[]> LoadItems { get; }
    public ReactiveCommand<Unit, Unit> SaveConversationsCmd { get; }
    [Reactive] public bool ShowUndoDelete { get; private set; }
    public ReactiveCommand<Unit, ConversationPreviewVm> UndoDeleteCmd { get; }
    public Interaction<ConversationPreviewVm, string> RenameIntr { get; } = new();

    public ConversationsVm()
    {
        LoadItems = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var ctx = AppServices.GetUserProfileDb();

            var convos = ctx.Conversations;
            var categories = await ctx.Categories
                                      .Where(c => c.IdCategory == "default" ||
                                                  convos.Any(m => m.IdCategory == c.IdCategory))
                                      .ToArrayAsync();

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
                 .Do(i => Items = new(i))
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


        SaveConversationsCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Using(
                () => AppServices.GetUserProfileDb(),
                ctx => Items.OfType<ConversationCategoryPreviewVm>()
                            .GetConversations()
                            .Select(c => c.FullItem)
                            .WhereNotNull()
                            .Cast<ConversationVm>()
                            .Where(c => ((ICommand)c.SaveCmd).CanExecute(null))
                            .ToObservable()
                            .SelectMany(c => c.SaveCmd.Execute(new() { DbContext = ctx }))
                            // If anything else was left unsaved, save it now
                            .Concat(Observable.FromAsync(() => ctx.SaveChangesAsync())
                                              .Select(_ => Unit.Default))
            ));

        var trashBinHasItems = TrashBin.WhenAnyValue(t => t.Count)
                                       .Select(c => c > 0);

        UndoDeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                if (TrashBin.LastOrDefault() is not { } last)
                    return null;
                await using var ctx = AppServices.GetUserProfileDb();
                await ctx.Conversations
                         .Where(c => c.IdConversation == last.Id)
                         .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, false));
                return last;
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
    }
}

public interface IConversationPreviewItem : IReactiveObject
{
    string Name { get; set; }
    object FullItem { get; }
}

public class ConversationCategoryPreviewVm : ActivatableViewModel, IConversationPreviewItem
{
    public string Id { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public string IconGlyph { get; set; }
    public ConversationsVm Conversations { get; set; }
    public ObservableCollectionExtended<ConversationPreviewVm> Items { get; } = new();
    public AdvancedCollectionView ItemsView { get; }
    [Reactive] public object FullItem { get; private set; }

    public ConversationCategoryPreviewVm()
    {
        ItemsView = new(Items, true)
        {
            Filter = item => !((ConversationPreviewVm)item).IsTrash
        };

        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.IsNewPlaceholder), SortDirection.Descending));
        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.CreatedTs), SortDirection.Descending));

        ItemsView.ObserveFilterProperty(nameof(ConversationPreviewVm.IsTrash));

        // Load category from the list
        Activator.Activated
                 .Where(_ => FullItem == null)
                 .Select(_ =>
                 {
                     var cat = AppServices.Container.Resolve<ConversationCategoryVm>();
                     cat.IdCategory = Id;
                     cat.Name = Name;
                     return cat;
                 })
                 .ObserveOnMainThread()
                 .Do(cat => FullItem = cat)
                 .SubscribeSafe();

        // Propagate data to/from full item
        this.WhenAnyValue(vm => vm.FullItem)
            .WhereNotNull()
            .Cast<ConversationCategoryVm>()
            .Select(cat => Observable.Merge(
                        this.WhenAnyValue(vm => vm.Name)
                            .Do(v => cat.Name = v)
                            .Select(_ => Unit.Default),
                        cat.WhenAnyValue(vm => vm.Name)
                           .Do(v => Name = v)
                           .Select(_ => Unit.Default),
                        cat.WhenAnyValue(vm => vm.IconGlyph)
                           .Do(v => IconGlyph = v)
                           .Select(_ => Unit.Default)
                    ))
            .Switch()
            .SubscribeSafe();
    }

    public ConversationPreviewVm CreateNewItemPlaceholder() =>
        new()
        {
            Name = "New Conversation",
            IsNewPlaceholder = true,
            Category = this,
            CreatedTs = DateTime.Now
        };
}

public class ConversationPreviewVm : ActivatableViewModel, IConversationPreviewItem
{
    public string Id { get; init; }
    public DateTime CreatedTs { get; init; }
    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsNewPlaceholder { get; set; }
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public ConversationCategoryPreviewVm Category { get; set; }
    [Reactive] public object FullItem { get; private set; }

    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }
    public ReactiveCommand<Unit, Unit> RenameCmd { get; }

    public ConversationPreviewVm()
    {
        Activator.Activated
                 .Take(1)
                 .Where(_ => FullItem == null && IsNewPlaceholder)
                 // Create a new conversation
                 .Select(_ => FullItem = AppServices.Container.Resolve<ConversationVm>())
                 .Cast<ConversationVm>()
                 // When system completion is initiated, clear the 'new item' flag
                 .Select(convo => convo.WhenAnyValue(vm => vm.Head.Message.Next)
                                       .WhereNotNull()
                                       .Select(h => h.WhenAnyValue(x => x.IsCompleting))
                                       .Switch()
                                       .Where(c => c)
                                       .Select(_ => convo))
                 .Switch()
                 .Take(1)
                 .ObserveOnMainThread()
                 .Do(_ => IsNewPlaceholder = false)
                 .SubscribeSafe();

        var newConvoCreated = this.WhenAnyValue(vm => vm.IsNewPlaceholder)
                                  .Skip(1)
                                  .Where(i => !i);

        // Create some kind of name for the new item (that's not new anymore)
        newConvoCreated
            .ObserveOnMainThread()
            // Generic name
            .Do(_ => Name = $"Chat {Category.Items.Count}")
            // Auto suggest name
            .Where(_ => !Debugging.Enabled || Debugging.AutoSuggestNames)
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                var convo = (ConversationVm)FullItem;
                var result = await convo.Api.CreateChatCompletions(new()
                {
                    Messages = new List<ChatMessage>(
                        new[]
                        {
                            new ChatMessage(
                                ChatMessageRole.System,
                                "Given the content provided by the user, you are to create witty yet concise names, with a maximum of 20 characters, excluding any form of punctuation or line breaks. The names should be complete words or phrases, avoiding any cutoffs. A sprinkle of humor is welcome, as long as it adheres to the character limit. Maximum 20 characters!"
                            ),
                            new ChatMessage(
                                ChatMessageRole.User,
                                convo.Head.Message.Content)
                        }),
                    Model = AiModel.GPT35Turbo
                });

                var suggestion = result.Choices.Last().Message.Content.CompactWhitespace().Trim('\"');

                return suggestion;
            }))
            .ObserveOnMainThread()
            .Do(name => Name = name)
            .SubscribeSafe();

        // When new item stops being new, insert a new 'new placeholder'
        newConvoCreated
            .ObserveOnMainThread()
            .Do(c => Category.Items.Insert(0, Category.CreateNewItemPlaceholder()))
            .SubscribeSafe();

        // Load conversation from the list
        Activator.Activated
                 .Where(_ => FullItem == null && !IsNewPlaceholder)
                 .Select(_ =>
                 {
                     var convo = AppServices.Container.Resolve<ConversationVm>();
                     convo.Id = Id;
                     return convo;
                 })
                 .Select(convo => convo.LoadCmd
                                       .Execute()
                                       .SelectMany(_ => convo.WhenAnyValue(vm => vm.Tail)
                                                             .WhereNotNull()
                                                             .Select(_ => convo))
                                       .Take(1))
                 .Switch()
                 .ObserveOnMainThread()
                 .Do(convo => FullItem = convo)
                 .SubscribeSafe();

        // Propagate data to/from full item
        this.WhenAnyValue(vm => vm.FullItem)
            .WhereNotNull()
            .Cast<ConversationVm>()
            .Select(c => Observable.Merge(
                        this.WhenAnyValue(vm => vm.Name)
                            .Do(v => c.Name = v)
                            .Select(_ => Unit.Default),
                        c.WhenAnyValue(vm => vm.Name)
                         .Do(v => Name = v)
                         .Select(_ => Unit.Default),
                        this.WhenAnyValue(vm => vm.Category)
                            .Do(v => c.IdCategory = v?.Id)
                            .Select(_ => Unit.Default)
                    ))
            .Switch()
            .SubscribeSafe();

        DeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await using var ctx = AppServices.GetUserProfileDb();
                await ctx.Conversations
                         .Where(c => c.IdConversation == Id)
                         .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, true));
            },
            this.WhenAnyValue(vm => vm.IsNewPlaceholder).Invert());

        DeleteCmd.ObserveOnMainThread()
                 .Do(_ =>
                 {
                     if (Category.Conversations.SelectedPreviewItem == this)
                         Category.Conversations.SelectedPreviewItem = null;
                     IsTrash = true;
                     Category.Conversations.TrashBin.Add(this);
                 })
                 .SubscribeSafe();

        RenameCmd = ReactiveCommand.CreateFromObservable(
            () => Category.Conversations.RenameIntr
                          .Handle(this)
                          .Where(name => name != null && name != Name)
                          .Select(name => Observable
                                          .FromAsync(
                                              async () =>
                                              {
                                                  await using var ctx = AppServices.GetUserProfileDb();
                                                  await ctx.Conversations
                                                           .Where(c => c.IdConversation == Id)
                                                           .ExecuteUpdateAsync(c => c.SetProperty(p => p.Name, name));
                                              })
                                          .ObserveOnMainThread()
                                          .Do(_ => Name = name))
                          .Switch());
    }
}

public static class IConversationItemExtensions
{
    public static IEnumerable<ConversationPreviewVm> GetConversations(this IEnumerable<ConversationCategoryPreviewVm> categories) =>
        categories.SelectMany(c => c.Items);
}
namespace MdcAi.ChatUI.ViewModels;

using LocalDal;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;
using System.ComponentModel;
using CommunityToolkit.WinUI.UI;
using Mdc.OpenAiApi;

// TODO: Check if changing gpt model works correctly when editing a message

public class ConversationsVm : ViewModel
{
    [Reactive] public ConversationVm SelectedConversation { get; set; }
    [Reactive] public object SelectedConversationPreview { get; set; }
    [Reactive] public ObservableCollectionExtended<IConversationItem> Items { get; private set; }
    public ObservableCollection<ConversationPreviewVm> TrashBin { get; } = new();
    public ReactiveCommand<Unit, IConversationItem[]> LoadItems { get; }
    public ReactiveCommand<Unit, Unit> SaveConversationsCmd { get; }
    [Reactive] public bool ShowUndoDelete { get; private set; }
    public ReactiveCommand<Unit, ConversationPreviewVm> UndoDeleteCmd { get; }
    public Interaction<ConversationPreviewVm, string> RenameIntr { get; } = new();

    public ConversationsVm()
    {
        LoadItems = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var ctx = Services.Container.Resolve<UserProfileDbContext>();

            var convos = ctx.Conversations;
            var categories = await ctx.Categories
                                      .Where(c => c.IdCategory == "default" ||
                                                  convos.Any(m => m.IdCategory == c.IdCategory))
                                      .ToArrayAsync();

            var ret = categories.Select(async category =>
            {
                var cat = new ConversationCategoryVm
                {
                    Name = category.Name,
                    IdCategory = category.IdCategory,
                    Conversations = this
                };

                cat.Items.Load((await convos.Where(m => !m.IsTrash && m.IdCategory == cat.IdCategory)
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

            return (await Task.WhenAll(ret)).Cast<IConversationItem>()
                                            .ToArray();
        });

        LoadItems.ObserveOnMainThread()
                 .Do(i => Items = new(i))
                 .SubscribeSafe();

        var selectedConvoPreview = this.WhenAnyValue(vm => vm.SelectedConversationPreview)
                                       .As<ConversationPreviewVm>();

        // Activation logic for the conversation preview (selected one is deactivated,
        // previous one deactivated)
        selectedConvoPreview
            .PairWithPrevious()
            .ObserveOnMainThread()
            .Do(x =>
            {
                x.Item1?.Activator.Deactivate();
                x.Item2?.Activator.Activate();
            })
            .SubscribeSafe();

        // Forward the current conversation to the SelectedConversation property
        selectedConvoPreview
            .Select(p => p == null ? Observable.Return((ConversationVm)null) : p.WhenAnyValue(vm => vm.Conversation))
            .Switch()
            .ObserveOnMainThread()
            .Do(p => SelectedConversation = p)
            .SubscribeSafe();

        SaveConversationsCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Using(
                () => Services.Container.Resolve<UserProfileDbContext>(),
                ctx => Items.OfType<ConversationCategoryVm>()
                            .GetConversations()
                            .Select(c => c.Conversation)
                            .WhereNotNull()
                            .Where(c => ((ICommand)c.SaveCmd).CanExecute(null))
                            .ToObservable()
                            .SelectMany(c => c.SaveCmd.Execute(new() { DbContext = ctx }))
                            // If anything else was left unsaved, save it now
                            .Concat(Observable.FromAsync(async () => await ctx.SaveChangesAsync())
                                              .Select(_ => Unit.Default))
            ));

        var trashBinHasItems = TrashBin.WhenAnyValue(t => t.Count)
                                       .Select(c => c > 0);

        UndoDeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                if (TrashBin.LastOrDefault() is not { } last)
                    return null;
                await using var ctx = Services.Container.Resolve<UserProfileDbContext>();
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

public interface IConversationItem
{
    public string Name { get; set; }
}

public class ConversationCategoryVm : ViewModel, IConversationItem
{
    public string Name { get; set; }
    public string IdCategory { get; set; }
    public ConversationsVm Conversations { get; set; }
    public ObservableCollectionExtended<ConversationPreviewVm> Items { get; } = new();
    public AdvancedCollectionView ItemsView { get; }

    public ConversationCategoryVm()
    {
        ItemsView = new(Items, true)
        {
            Filter = item => !((ConversationPreviewVm)item).IsTrash
        };

        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.IsNewPlaceholder), SortDirection.Descending));
        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.CreatedTs), SortDirection.Descending));

        ItemsView.ObserveFilterProperty(nameof(ConversationPreviewVm.IsTrash));
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

public class ConversationPreviewVm : ActivatableViewModel, IConversationItem
{
    public string Id { get; init; }
    public DateTime CreatedTs { get; init; }
    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsNewPlaceholder { get; set; }
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public ConversationCategoryVm Category { get; set; }
    [Reactive] public ConversationVm Conversation { get; private set; }

    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }
    public ReactiveCommand<Unit, string> RenameCmd { get; }

    public ConversationPreviewVm()
    {
        Activator.Activated
                 .Take(1)
                 .Where(_ => Conversation == null && IsNewPlaceholder)
                 // Create a new conversation
                 .Select(_ => Conversation = Services.GetRequired<ConversationVm>())
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
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                var result = await Conversation.Api.CreateChatCompletions(new()
                {
                    Messages = new List<ChatMessage>(
                        new[]
                        {
                            new ChatMessage(
                                ChatMessageRole.System,
                                "You suggest names for the given content. Anything sent by the user is considered content. For any given content you should produce a summary of the content in no more than 20 characters which is used as a name for that content. Your suggestion may contain a touch of humor where applicable."
                                ),
                            new ChatMessage(
                                ChatMessageRole.User,
                                Conversation.Head.Message.Content)
                        }),
                    Model = AiModel.GPT35Turbo
                });

                var suggestion = result.Choices.Last().Message.Content.Trim('\"');

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
                 .Where(_ => Conversation == null && !IsNewPlaceholder)
                 .Select(_ =>
                 {
                     var convo = Services.Container.Resolve<ConversationVm>();
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
                 .Do(convo => Conversation = convo)
                 .SubscribeSafe();

        UpdateField(vm => vm.Category, (c, v) => c.IdCategory = v?.IdCategory).SubscribeSafe();
        UpdateField(vm => vm.Name, (c, v) => c.Name = v).SubscribeSafe();

        DeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await using var ctx = Services.Container.Resolve<UserProfileDbContext>();
                await ctx.Conversations
                         .Where(c => c.IdConversation == Id)
                         .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, true));
            },
            this.WhenAnyValue(vm => vm.IsNewPlaceholder).Invert());

        DeleteCmd.ObserveOnMainThread()
                 .Do(_ =>
                 {
                     if (Category.Conversations.SelectedConversationPreview == this)
                         Category.Conversations.SelectedConversationPreview = null;
                     IsTrash = true;
                     Category.Conversations.TrashBin.Add(this);
                 })
                 .SubscribeSafe();

        RenameCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                var name = await Category.Conversations.RenameIntr.Handle(this);

                if (!string.IsNullOrEmpty(name))
                {
                    await using var ctx = Services.Container.Resolve<UserProfileDbContext>();
                    await ctx.Conversations
                             .Where(c => c.IdConversation == Id)
                             .ExecuteUpdateAsync(c => c.SetProperty(p => p.Name, name));
                }

                return name;
            });

        RenameCmd.ObserveOnMainThread()
                 .WhereNotNull()
                 .Do(n => Name = n)
                 .SubscribeSafe();

        return;

        // Lets you propagate this preview data to the conversation automatically
        IObservable<Unit> UpdateField<T>(Expression<Func<ConversationPreviewVm, T>> prop, Action<ConversationVm, T> action) =>
            Observable.CombineLatest(this.WhenAnyValue(prop),
                                     this.WhenAnyValue(vm => vm.Conversation),
                                     (thing, convo) => (thing, convo))
                      .Where(x => x is { convo: not null })
                      .ObserveOnMainThread()
                      .Do(x => action(x.convo, x.thing))
                      .Select(_ => Unit.Default);

        //this.WhenActivated(disposables =>
        //{
        //    Debug.WriteLine($"Activated {Name}");
        //    Disposable.Create(() => Debug.WriteLine($"Deactivated {Name}")).DisposeWith(disposables);
        //});
    }
}

public static class IConversationItemExtensions
{
    public static IEnumerable<ConversationPreviewVm> GetConversations(this IEnumerable<ConversationCategoryVm> categories) =>
        categories.SelectMany(c => c.Items);
}
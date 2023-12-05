namespace MdcAi.ChatUI.ViewModels;

using LocalDal;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Windows.Navigation;

public class ConversationsVm : ViewModel
{
    [Reactive] public ConversationVm SelectedConversation { get; set; }
    [Reactive] public object SelectedConversationPreview { get; set; }
    [Reactive] public ObservableCollectionExtended<IConversationItem> Items { get; private set; }

    public ReactiveCommand<Unit, IConversationItem[]> LoadItems { get; }
    public ReactiveCommand<Unit, Unit> SaveConversationsCmd { get; }

    public ConversationsVm()
    {
        LoadItems = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var ctx = Services.Container.Resolve<UserProfileDbContext>();

            var data = await ctx.Conversations
                                .Where(c => !c.IsTrash)
                                .Select(c => new
                                {
                                    c.Name,
                                    c.Category,
                                    c.IdConversation
                                })
                                .ToArrayAsync();

            var categories = data.GroupBy(d => d.Category)
                                 .Select(c =>
                                 {
                                     var cat = new ConversationCategoryVm
                                     {
                                         Name = c.Key,
                                     };

                                     cat.Items = new(c.Select(i => new ConversationPreviewVm
                                                      {
                                                          Id = i.IdConversation,
                                                          Name = i.Name,
                                                          Category = cat
                                                      })
                                                      .Prepend(cat.CreateNewItemPlaceholder()));

                                     return cat;
                                 })
                                 .Cast<IConversationItem>()
                                 .ToArray();

            return categories;
        });

        LoadItems.ObserveOnMainThread()
                 .Do(i => Items = new(i))
                 .Subscribe();

        var selectedConvoPreview = this.WhenAnyValue(vm => vm.SelectedConversationPreview)
                                       .OfType<ConversationPreviewVm>();

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
            .Subscribe();

        // Forward the current conversation to the SelectedConversation property
        selectedConvoPreview
            .Select(p => p.WhenAnyValue(vm => vm.Conversation))
            .Switch()
            .ObserveOnMainThread()
            .Do(p => SelectedConversation = p)
            .Subscribe();

        // When new item stops being new, insert a new 'new placeholder'
        selectedConvoPreview
            .Where(c => c.IsNewPlaceholder)
            .Select(c => c.WhenAnyValue(x => x.IsNewPlaceholder)
                          .Where(i => !i)
                          .Select(_ => c))
            .Switch()
            .ObserveOnMainThread()
            .Do(c => c.Category.Items.Insert(0, c.Category.CreateNewItemPlaceholder()))
            .Subscribe();

        // TODO: Add Categories table... also, after all, I think most of this logic should be in convo vm and batch called from here

        SaveConversationsCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Using(
                () => Services.Container.Resolve<UserProfileDbContext>(),
                ctx => Items.OfType<ConversationCategoryVm>()
                            .GetConversations()
                            .Select(c => c.Conversation)
                            .Where(c => c?.IsDirty == true)
                            .ToObservable()
                            .SelectMany(c => c.SaveCmd.Execute(new() { }))
                            .Concat(Observable.FromAsync(async () =>
                                              {
                                                  Debug.WriteLine($"Committing changes");
                                                  return await ctx.SaveChangesAsync();
                                              })
                                              .Select(_ => Unit.Default))
            ));

        //SaveConversationsCmd = ReactiveCommand.CreateFromTask(async (IConversationItem[] items) =>
        //{
        //    await using var ctx = Services.Container.Resolve<UserProfileDbContext>();

        //    var convos = Items.OfType<ConversationCategoryVm>()
        //                      .GetConversations()
        //                      .Select(c => c.Conversation)
        //                      .WhereNotNull()
        //                      .ToArray();

        //    foreach (var convo in convos.Where(c => c.IsDirty))
        //    {
        //        var dbconvo = convo.ToDbConversation();

        //        if (await ctx.Conversations.AllAsync(c => c.IdConversation != dbconvo.IdConversation))
        //            ctx.Conversations.Add(dbconvo);
        //        else
        //            ctx.Conversations.Update(dbconvo);
        //    }

        //    await ctx.SaveChangesAsync();
        //});
    }
}

public interface IConversationItem
{
    public string Name { get; set; }
}

public class ConversationCategoryVm : ViewModel, IConversationItem
{
    public string Name { get; set; }
    public ObservableCollection<ConversationPreviewVm> Items { get; set; }

    public ConversationPreviewVm CreateNewItemPlaceholder() =>
        new()
        {
            Name = "New Conversation",
            IsNewPlaceholder = true,
            Category = this
        };
}

// TODO: When there are no convos then there are no categories and no way to add new convo

public class ConversationPreviewVm : ActivatableViewModel, IConversationItem
{
    public string Id { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsNewPlaceholder { get; set; }
    [Reactive] public ConversationCategoryVm Category { get; set; }
    [Reactive] public ConversationVm Conversation { get; private set; }

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
                 .Subscribe();

        // Create some kind of name for the new item (that's not new anymore)
        this.WhenAnyValue(vm => vm.IsNewPlaceholder)
            .Skip(1)
            .Where(i => !i)
            .ObserveOnMainThread()
            .Do(_ => Name = $"Chat {Category.Items.Count}")
            .Subscribe();

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
                 .Subscribe();

        UpdateField(vm => vm.Category, (c, v) => c.Category = v?.Name).Subscribe();
        UpdateField(vm => vm.Name, (c, v) => c.Name = v).Subscribe();

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
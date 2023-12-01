namespace MdcAi.ChatUI.ViewModels;

using LocalDal;
using Microsoft.EntityFrameworkCore;
using System.Windows.Navigation;

public class ConversationsVm : ViewModel
{
    [Reactive] public ConversationVm SelectedConversation { get; set; }
    [Reactive] public object SelectedConversationPreview { get; set; }
    [Reactive] public ObservableCollectionExtended<IConversationItem> Items { get; private set; }

    public ReactiveCommand<Unit, IConversationItem[]> LoadItems { get; }

    public ConversationsVm()
    {
        LoadItems = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var db = Services.Container.Resolve<UserProfileDbContext>();

            var data = await db.Conversations
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

public class ConversationPreviewVm : ActivatableViewModel, IConversationItem
{
    public string Id { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsNewPlaceholder { get; set; }
    public ConversationCategoryVm Category { get; set; }
    [Reactive] public ConversationVm Conversation { get; private set; }

    public ConversationPreviewVm()
    {
        // If new, it should just instantiate an empty conversation and then wire it up so that convo updates this entry
        // Otherwise, it should load rest of the data from the db and instantiate a convo from that and wire it up

        // After a while, Conversation can be reset back to null if not used, and instantiated again when used (the loaded data
        // remains tho)

        // When system completion is initiated, clear the 'new item' flag
        Activator.Activated
                 .Take(1)
                 .Where(_ => Conversation == null && IsNewPlaceholder)
                 .Select(_ => Conversation = Services.GetRequired<ConversationVm>())
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
            .Do(_ => Conversation.Name = Name = $"Chat {Category.Items.Count}")
            .Subscribe();



        //this.WhenActivated(disposables =>
        //{
        //    Debug.WriteLine($"Activated {Name}");
        //    Disposable.Create(() => Debug.WriteLine($"Deactivated {Name}")).DisposeWith(disposables);
        //});
    }
}
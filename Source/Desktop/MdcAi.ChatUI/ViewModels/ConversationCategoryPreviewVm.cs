namespace MdcAi.ChatUI.ViewModels;

using CommunityToolkit.WinUI.UI;
using Microsoft.EntityFrameworkCore;

public class ConversationCategoryPreviewVm : ActivatableViewModel, IConversationPreviewItem
{
    [Reactive] public string Id { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public string IconGlyph { get; set; }
    [Reactive] public bool IsTrash { get; set; }
    public ConversationsVm Conversations { get; set; }
    public ObservableCollectionExtended<ConversationPreviewVm> Items { get; } = new();
    public AdvancedCollectionView ItemsView { get; }
    [Reactive] public object FullItem { get; private set; }
    [Reactive] public int ItemsCount { get; private set; }

    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }

    public ConversationCategoryPreviewVm()
    {
        ItemsView = new(Items, true)
        {
            Filter = item => !((ConversationPreviewVm)item).IsTrash
        };

        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.IsNewPlaceholder), SortDirection.Descending));
        ItemsView.SortDescriptions.Add(new(nameof(ConversationPreviewVm.CreatedTs), SortDirection.Descending));

        ItemsView.ObserveFilterProperty(nameof(ConversationPreviewVm.IsTrash));

        Items.WhenAnyValue(vm => vm.Count)
             .Do(i => ItemsCount = i)
             .SubscribeSafe();

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

        DeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await using var ctx = AppServices.GetUserProfileDb();
                await ctx.Categories
                         .Where(c => c.IdCategory == Id)
                         .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, true));
            },
            this.WhenAnyValue(vm => vm.Id).Select(id => id != "default"));

        DeleteCmd.ObserveOnMainThread()
                 .Do(_ =>
                 {
                     IsTrash = true;
                     Conversations.TrashBin.Add(this);
                 })
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
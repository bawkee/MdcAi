namespace MdcAi.ChatUI.ViewModels;

using Windows.Storage;
using OpenAiApi;
using LocalDal;
using Microsoft.EntityFrameworkCore;

public class ConversationVm : ActivatableViewModel
{
    public IOpenAiApi Api { get; }
    public SettingsVm GlobalSettings { get; }
    public string Id { get; set; }
    [Reactive] public DateTime CreatedTs { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public string IdCategory { get; set; }
    [Reactive] public string CategoryName { get; set; }
    [Reactive] public ChatMessageSelectorVm Head { get; set; }
    [Reactive] public ChatMessageSelectorVm Tail { get; private set; }
    [Reactive] public ChatMessageSelectorVm SelectedMessage { get; set; }
    [Reactive] public ChatMessageSelectorVm EditMessage { get; set; }
    [Reactive] public ChatSettingsVm Settings { get; set; } = new();
    [Reactive] public PromptVm Prompt { get; set; } = new();
    [Reactive] public bool IsOpenAIReady { get; private set; }
    [Reactive] public AiModel[] Models { get; private set; }
    [Reactive] public bool IsLoadingModels { get; private set; }
    [Reactive] public bool IsDirty { get; private set; } = true;
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public bool IsSendPromptEnabled { get; private set; }
    [Reactive] public bool IsLoading { get; private set; }

    public ReactiveCommand<Unit, Unit> GeneratePromptCmd { get; }
    public ReactiveCommand<Unit, Unit> EditSelectedCmd { get; }
    public ReactiveCommand<Unit, Unit> CancelEditCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteSelectedCmd { get; }
    public ReactiveCommand<Unit, Unit> RegenerateSelectedCmd { get; }
    public ReactiveCommand<Unit, Unit> PrevVersionCmd { get; }
    public ReactiveCommand<Unit, Unit> NextVersionCmd { get; }
    public ReactiveCommand<string, Unit> SelectCmd { get; }
    public ReactiveCommand<Unit, AiModel[]> LoadModelsCmd { get; }
    public ReactiveCommand<string, Unit> SelectModelCmd { get; }
    public ReactiveCommand<Unit, Unit> SendPromptCmd { get; }
    public ReactiveCommand<Unit, Unit> DebugCmd { get; }
    public ReactiveCommand<ConversationSaveOptions, Unit> SaveCmd { get; }
    public ReactiveCommand<Unit, DbConversation> LoadCmd { get; }

    [Reactive] public ObservableCollection<ChatMessageVm> Messages { get; set; }
    [Reactive] public WebViewRequestDto LastMessagesRequest { get; set; }
    public HashSet<string> MessageTrashBin { get; } = new();

    public ConversationVm(IOpenAiApi api, SettingsVm globalSettings)
    {
        Api = api;
        GlobalSettings = globalSettings;
        Id = Guid.NewGuid().ToString();
        CreatedTs = DateTime.Now;
        Name = "My Conversation";

        // When Head is set, automatically track the entire tree and all its forks to set the Tail. This structure is a tree but it 
        // renders a simple linked list so it is crucial that we always have the current head and tail.
        this.WhenAnyValue(vm => vm.Head)
            .Select(i => i == null ? Observable.Return((ChatMessageSelectorVm)null) : TrackNext(i))
            .Switch()
            .Do(t => Tail = t)
            .SubscribeSafe();

        // Automatically build the linked list when Tail changes. This is a flat list of the current state that we can use for rendering.
        this.WhenAnyValue(vm => vm.Tail)
            .Select(t => t?.WhenAnyValue(x => x.Message) ?? Observable.Return((ChatMessageVm)null))
            .Switch()
            .ObserveOnMainThread()
            .Select(_ => Head?.Message.GetNextMessages() ?? Enumerable.Empty<ChatMessageVm>())
            .Do(m => Messages = new(m.ToArray()))
            .SubscribeSafe();

        GeneratePromptCmd = ReactiveCommand.CreateFromObservable(
            () => Observable
                  .FromAsync(async () =>
                  {
                      string contents;

                      if (Debugging.NumberedMessages)
                          contents = $"Debug user {Debugging.UserMessageCounter++}";
                      else
                      {
                          var file = await StorageFile.GetFileFromApplicationUriAsync(
                              new Uri("ms-appx:///Assets/Dbg/Test2.md"));
                          contents = await FileIO.ReadTextAsync(file);
                      }

                      return new ChatMessageVm(this, ChatMessageRole.User)
                      {
                          Content = contents,
                          Previous = Tail?.Message,
                          Settings = new(Settings),
                      };
                  })
                  .ObserveOnMainThread()
                  .Do(msg =>
                  {
                      if (Head == null)
                          Head = msg.Selector;
                      else
                          Tail.Message.Next = msg;
                  })
                  .Select(_ => Unit.Default)
        );

        EditSelectedCmd = ReactiveCommand.Create(
            () =>
            {
                EditMessage = SelectedMessage;
                Prompt = new()
                {
                    Contents = EditMessage.Message.Content
                };
            },
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m?.Message.Role == ChatMessageRole.User));

        DeleteSelectedCmd = ReactiveCommand.CreateFromObservable(
            () =>
            {
                foreach (var trashMsg in SelectedMessage.Message.GetNextMessages().Select(m => m.Id))
                    MessageTrashBin.Add(trashMsg);

                return Tail.Message.StopCompletionCmd.Execute()
                           .Select(_ => SelectedMessage.DeleteCmd.Execute())
                           .Switch()
                           .Do(_ =>
                           {
                               if (SelectedMessage.Versions.Count == 0)
                               {
                                   if (SelectedMessage == Head)
                                       Head = null;
                                   SelectedMessage = null;
                               }
                           });
            },
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m?.Message.Role == ChatMessageRole.User));

        RegenerateSelectedCmd = ReactiveCommand.CreateFromObservable(
            () => SelectedMessage.Message.CompleteCmd.Execute(Unit.Default)
                                 .Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m?.Message.Role == ChatMessageRole.System && m == Tail));

        SendPromptCmd = ReactiveCommand.CreateFromObservable(
            () => Observable
                  .Return(new
                  {
                      Message = EditMessage == null ?
                          new ChatMessageVm(this, ChatMessageRole.User)
                          {
                              Content = Prompt.Contents,
                              Previous = Tail?.Message,
                              Settings = new(Settings),
                          } :
                          new ChatMessageVm(this, ChatMessageRole.User, EditMessage)
                          {
                              Content = Prompt.Contents,
                              Previous = EditMessage.Message.Previous,
                              Settings = new(Settings),
                          },
                      EditMessage
                  })
                  .Do(data =>
                  {
                      if (data.EditMessage != null)
                      {
                          data.EditMessage.Message = data.Message;
                          EditMessage = null;
                      }
                      else if (Head == null)
                          Head = data.Message.Selector;
                      else
                          Tail.Message.Next = data.Message;
                  })
                  .Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.IsSendPromptEnabled));

        SendPromptCmd.Do(_ => Prompt = new())
                     .SubscribeSafe();

        this.WhenAnyValue(vm => vm.IsLoadingModels, vm => vm.Prompt.Contents)
            .Do(x => IsSendPromptEnabled = !x.Item1 &&
                                           !string.IsNullOrEmpty(x.Item2))
            .SubscribeSafe();

        SelectCmd = ReactiveCommand.Create((string m) =>
        {
            var msg = Head.Message.GetNextMessages().FirstOrDefault(msg => msg.Id == m);
            if (msg != null)
                SelectedMessage = msg.Selector;
        });


        NextVersionCmd = ReactiveCommand.CreateFromObservable(
            () => SelectedMessage.NextCmd.Execute(),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .SelectMany(m => m == null ? Observable.Return(false) : m.NextCmd.CanExecute));

        PrevVersionCmd = ReactiveCommand.CreateFromObservable(
            () => SelectedMessage.PrevCmd.Execute(),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .SelectMany(m => m == null ? Observable.Return(false) : m.PrevCmd.CanExecute));

        LoadModelsCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Debugging.Enabled && Debugging.MockModels)
                return new[]
                {
                    new AiModel("gpt-3.5-turbo-16k-0613"),
                    new AiModel("gpt-3.5-turbo-16k"),
                    new AiModel("gpt-4-1106-preview"),
                    new AiModel("gpt-3.5-turbo"),
                    new AiModel("gpt-3.5-turbo-1106"),
                    new AiModel("gpt-4-vision-preview"),
                    new AiModel("gpt-4"),
                    new AiModel("gpt-3.5-turbo-instruct-0914"),
                    new AiModel("gpt-3.5-turbo-0613"),
                    new AiModel("gpt-3.5-turbo-0301"),
                    new AiModel("gpt-3.5-turbo-instruct"),
                    new AiModel("gpt-4-0613"),
                    new AiModel("gpt-4-0314")
                };
            return await api.GetModels();
        });

        LoadModelsCmd.ObserveOnMainThread()
                     .Do(models => Models = models.Where(m => m.ModelID
                                                               .StartsWith("gpt"))
                                                  .ToArray())
                     .SubscribeSafe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .SubscribeSafe();

        SelectModelCmd = ReactiveCommand.Create((string model) => { Settings.Model = model; });

        CancelEditCmd = ReactiveCommand.Create(
            () =>
            {
                EditMessage = null;
                Prompt = new();
            });

        LoadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await using var ctx = AppServices.Container.Resolve<UserProfileDbContext>();

            var convo = await ctx.Conversations
                                 .Include(c => c.Messages)
                                 .FirstOrDefaultAsync(c => c.IdConversation == Id);

            return convo;
        });

        LoadCmd.IsExecuting
               .ObserveOnMainThread()
               .Do(i => IsLoading = i)
               .SubscribeSafe();

        LoadCmd.ObserveOnMainThread()
               .Do(convo =>
               {
                   Name = convo.Name;
                   CreatedTs = convo.CreatedTs;
                   IdCategory = convo.IdCategory;
                   IsTrash = convo.IsTrash;
                   Head = convo.Messages.FromDbMessages(this);
                   IsDirty = false;
               })
               .SubscribeSafe();

        SaveCmd = ReactiveCommand.CreateFromTask(
            async (ConversationSaveOptions opt) =>
            {
                var ctx = opt.DbContext ?? AppServices.Container.Resolve<UserProfileDbContext>();
                var ctxScope = opt.DbContext == null ? ctx : Disposable.Empty;

                using (ctxScope)
                {
                    var dbconvo = this.ToDbConversation();

                    var existingConvo = await ctx.Conversations.FirstOrDefaultAsync(c => c.IdConversation == Id);

                    if (existingConvo == null)
                        ctx.Conversations.Add(dbconvo);
                    else
                    {
                        existingConvo.Name = dbconvo.Name;
                        existingConvo.IdCategory = dbconvo.IdCategory;
                        existingConvo.IsTrash = dbconvo.IsTrash;

                        await ctx.Messages.Where(m => MessageTrashBin.Contains(m.IdMessage)).ExecuteDeleteAsync();
                        await Task.WhenAll(dbconvo.Messages.Select(msg => ctx.Messages.Upsert(msg).RunAsync()));
                    }

                    await ctx.SaveChangesAsync();
                }
            },
            this.WhenAnyValue(vm => vm.IsDirty, vm => vm.Messages.Count)
                .Select(_ => IsDirty && Messages.Count > 0));

        SaveCmd.ObserveOnMainThread()
               .Do(_ => IsDirty = false)
               .SubscribeSafe();

        // Auto save whenever completion ends
        // TODO: Regenerate? Also, what if delete happens in midst of generating an answer?
        Observable.Merge(
                      this.WhenAnyValue(vm => vm.Tail)
                          .Where(t => t?.Message?.Role == ChatMessageRole.System)
                          .Select(t => t.Message.CompleteCmd
                                        .Select(_ => t.Message.WhenAnyValue(m => m.IsCompleting)
                                                      .Where(i => !i))
                                        .Switch())
                          .Switch()
                          .Select(_ => Unit.Default),
                      DeleteSelectedCmd)
                  .Throttle(TimeSpan.FromMilliseconds(500))
                  .Select(_ => new ConversationSaveOptions())
                  .InvokeCommand(SaveCmd);

        this.WhenAnyValue(vm => vm.GlobalSettings)
            .Select(s => s.WhenAnyValue(x => x.OpenAi.ApiKeys))
            .Switch()
            .Select(s => s != null && !string.IsNullOrEmpty(s))
            .ObserveOnMainThread()
            .Do(i => IsOpenAIReady = i)
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.SelectedMessage)
            .Select(_ => Unit.Default)
            .InvokeCommand(CancelEditCmd);

        DebugCmd = ReactiveCommand.Create(() =>
        {
            //var _ = JsonConvert.SerializeObject(this.ToDbConversation(), Formatting.Indented);
        });

        this.WhenAnyValue(vm => vm.IdCategory)
            .WhereNotNull()
            .SelectMany(id => Observable.FromAsync(async () =>
            {
                await using var ctx = AppServices.Container.Resolve<UserProfileDbContext>();
                return await ctx.Categories.FirstOrDefaultAsync(c => c.IdCategory == id);
            }))
            .ObserveOnMainThread()
            .Do(c => CategoryName = c.Name)
            .SubscribeSafe();

        Activator.Activated.Take(1)
                 .Select(_ => this.WhenAnyValue(vm => vm.IsOpenAIReady)
                                  .Where(i => i)
                                  .Select(_ => Unit.Default))
                 .Switch()
                 .InvokeCommand(LoadModelsCmd);

        if (Debugging.Enabled &&
            Debugging.MockMessages &&
            Debugging.AutoSendFirstMessage)
            Activator.Activated.Take(1).InvokeCommand(GeneratePromptCmd);

        this.WhenActivated(disposables =>
        {
            Debug.WriteLine($"Activated {GetType()} - {Name}");
            Disposable.Create(() => Debug.WriteLine($"Deactivated {GetType()} - {Name}")).DisposeWith(disposables);

            // Trigger completions when user posts a message
            this.WhenAnyValue(vm => vm.Tail)
                .Where(t => t?.Message.Role == ChatMessageRole.User)
                .Select(t => new
                {
                    Tail = t,
                    Completion = new ChatMessageVm(this, ChatMessageRole.System)
                    {
                        Previous = t.Message,
                        Settings = new(t.Message.Settings),
                    }
                })
                .Do(x => x.Tail.Message.Next = x.Completion)
                .Select(x => x.Completion.CompleteCmd.Execute())
                .Switch()
                .SubscribeSafe()
                .DisposeWith(disposables);

            // Build request data to communicate with WebView for rendering
            this.WhenAnyValue(vm => vm.Messages)
                .WhereNotNull()
                .Select(m =>
                {
                    if (m.Count > 0)
                        return m.Last()
                                .WhenAnyValue(vm => vm.HTMLContent)
                                .Throttle(TimeSpan.FromMilliseconds(50))
                                .Select(_ => m);
                    return Observable.Return(m);
                })
                .Switch()
                .Select(m => m.CreateWebViewSetMessageRequest())
                .ObserveOnMainThread()
                .Do(r =>
                {
                    //Debug.WriteLine($"Last message: {((WebViewSetMessagesRequestDto)r.Data).Messages.LastOrDefault()?.Content}");
                    LastMessagesRequest = r;
                })
                .SubscribeSafe()
                .DisposeWith(disposables);

            // TODO: Auto select last when loading from db as well and scroll to bottom

            // Auto select the message generated by the completion system            
            this.WhenAnyValue(vm => vm.Tail)
                .Select(t => t.WhenAnyValue(x => x.Message))
                .Switch()
                .SelectMany(t => t.CompleteCmd.WhenExecuting())
                // We actually need to wait for the Messages list to be created first, because WebView renders from this
                .Select(_ => this.WhenAnyValue(vm => vm.Messages)
                                 .Skip(1)
                                 .Take(1)
                                 .Where(m => m.Count > 0 && m.Last().Role == ChatMessageRole.System))
                .Switch()
                .Do(m => SelectedMessage = m.Last().Selector)
                .SubscribeSafe()
                .DisposeWith(disposables);

            this.WhenAnyValue(vm => vm.IsDirty)
                .Where(v => !v)
                .Select(_ => Observable.CombineLatest(
                                           this.WhenAnyValue(vm => vm.Messages)
                                               .Select(i => HashCode.Combine(i.Select(j => j.Content.GetHashCode()))),
                                           this.WhenAnyValue(vm => vm.Name).Select(i => i.GetHashCode()),
                                           this.WhenAnyValue(vm => vm.IdCategory).Select(i => i.GetHashCode()))
                                       .Select(i => HashCode.Combine(i))
                                       .Skip(1)
                                       .DistinctUntilChanged()
                                       .Take(1))
                .Switch()
                .Do(_ => IsDirty = true)
                .SubscribeSafe()
                .DisposeWith(disposables);
        });
    }

    // Allows you to track `Next` property of an item including all the subsequent items in the list. Always ticks the
    // last item (Tail).
    private IObservable<ChatMessageSelectorVm> TrackNext(ChatMessageSelectorVm vm) =>
        Observable.Merge(
            // A simple but effective way to exit the recursion, we stop at null but
            // keep monitoring none the less
            vm.WhenAnyValue(x => x.Message)
              .Select(m => m.WhenAnyValue(x => x.Next))
              .Switch()
              .Where(c => c == null)
              .Select(_ => vm.Message.Selector),
            // Here we have recursion. If you 'remove' an item by setting `Next` to null it will
            // become detached (`Switch` statement) once set to something else so no leaks here
            vm.WhenAnyValue(x => x.Message)
              .Select(m => m.WhenAnyValue(x => x.Next))
              .Switch()
              .Where(c => c != null)
              .Select(c => TrackNext(c.Selector))
              .Switch()
        );
}

public class ConversationSaveOptions
{
    public UserProfileDbContext DbContext { get; set; }
}

public class PromptVm : ViewModel
{
    [Reactive] public string Contents { get; set; }
}
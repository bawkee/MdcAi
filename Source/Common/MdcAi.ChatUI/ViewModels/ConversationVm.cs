namespace MdcAi.ChatUI.ViewModels;

using Windows.Storage;
using Mdc.OpenAiApi;
using Newtonsoft.Json;
using MdcAi.ChatUI.LocalDal;

public class ConversationVm : ActivatableViewModel
{
    public IOpenAIApi Api { get; }
    public SettingsVm GlobalSettings { get; }
    public string Id { get; set; }
    public DateTime CreatedTs { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public ChatMessageSelectorVm Head { get; set; }
    [Reactive] public ChatMessageSelectorVm Tail { get; set; }
    [Reactive] public ChatMessageSelectorVm SelectedMessage { get; set; }
    [Reactive] public ChatMessageSelectorVm EditMessage { get; set; }
    [Reactive] public ChatSettingsVm Settings { get; set; } = new();
    [Reactive] public PromptVm Prompt { get; set; } = new();
    [Reactive] public bool IsOpenAIReady { get; private set; }
    [Reactive] public AiModel[] Models { get; private set; }
    [Reactive] public bool IsLoadingModels { get; private set; }
    [Reactive] public bool IsNew { get; set; } = true;
    [Reactive] public bool IsDirty { get; private set; } = true;
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public string Category { get; set; }
    [Reactive] public bool IsSendPromptEnabled { get; private set; }

    public ReactiveCommand<Unit, Unit> AddCmd { get; }
    public ReactiveCommand<Unit, Unit> EditSelectedCmd { get; }
    public ReactiveCommand<Unit, Unit> CancelEditCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteSelectedCmd { get; }
    public ReactiveCommand<Unit, Unit> RegenerateSelectedCmd { get; }
    public ReactiveCommand<string, Unit> SelectCmd { get; }
    public ReactiveCommand<Unit, AiModel[]> LoadModelsCmd { get; }
    public ReactiveCommand<string, Unit> SelectModelCmd { get; }
    public ReactiveCommand<Unit, Unit> SendPromptCmd { get; }
    public ReactiveCommand<Unit, Unit> DebugCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveCmd { get; }
    public ReactiveCommand<Unit, DbConversation> LoadCmd { get; }

    [Reactive] public ObservableCollection<ChatMessageVm> Messages { get; set; }
    [Reactive] public WebViewRequestDto LastMessagesRequest { get; set; }

    public ConversationVm(IOpenAIApi api, SettingsVm globalSettings)
    {
        Api = api;
        GlobalSettings = globalSettings;
        Id = Guid.NewGuid().ToString();
        CreatedTs = DateTime.Now;
        Name = "My Conversation";

        this.WhenAnyValue(vm => vm.Head)
            .Select(i => i == null ? Observable.Return((ChatMessageSelectorVm)null) : TrackNext(i))
            .Switch()
            .Do(t => Tail = t)
            .Subscribe();

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
            .Subscribe();

        this.WhenAnyValue(vm => vm.Tail)
            .Select(t => t?.WhenAnyValue(x => x.Message) ?? Observable.Return((ChatMessageVm)null))
            .Switch()
            .ObserveOnMainThread()
            .Select(_ => Head?.Message.GetNextMessages() ?? Enumerable.Empty<ChatMessageVm>())
            .Do(m => Messages = new(m.ToArray()))
            .Subscribe();

        this.WhenAnyValue(vm => vm.Messages)
            .WhereNotNull()
            .Select(m =>
            {
                if (m.Count > 0 && m.Last().IsCompleting)
                    return m.Last()
                            .WhenAnyValue(vm => vm.HTMLContent)
                            .Throttle(TimeSpan.FromMilliseconds(50))
                            .Select(_ => m);
                return Observable.Return(m);
            })
            .Switch()
            .Select(m => m.CreateWebViewSetMessageRequest())
            .ObserveOnMainThread()
            .Do(r => LastMessagesRequest = r)
            .Subscribe();

        this.WhenAnyValue(vm => vm.Tail)
            .Select(t => t.WhenAnyValue(x => x.Message))
            .Switch()
            .SelectMany(t => t.CompleteCmd.WhenExecuting())
            .Select(_ => this.WhenAnyValue(vm => vm.Messages)
                             .Skip(1)
                             .Take(1)
                             .Where(m => m.Count > 0 && m.Last().Role == ChatMessageRole.System))
            .Switch()
            .Do(m => SelectedMessage = m.Last().Selector)
            .Subscribe();

        AddCmd = ReactiveCommand.CreateFromObservable(
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
            () => SelectedMessage?.Message.Role == ChatMessageRole.User ?
                SelectedMessage.DeleteCmd.Execute()
                               .Do(_ =>
                               {
                                   if (SelectedMessage.Versions.Count == 0)
                                   {
                                       if (SelectedMessage == Head)
                                           Head = null;
                                       SelectedMessage = null;
                                   }
                               }) :
                Observable.Return(Unit.Default),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m?.Message.Role == ChatMessageRole.User));

        RegenerateSelectedCmd = ReactiveCommand.CreateFromObservable(
            () => SelectedMessage.Message.CompleteCmd.Execute(Unit.Default)
                                 .Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m?.Message.Role == ChatMessageRole.System));

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
                     .Subscribe();

        this.WhenAnyValue(vm => vm.IsLoadingModels, vm => vm.Prompt.Contents)
            .Do(x => IsSendPromptEnabled = !x.Item1 &&
                                           !string.IsNullOrEmpty(x.Item2))
            .Subscribe();

        SelectCmd = ReactiveCommand.Create((string m) =>
        {
            var msg = Head.Message.GetNextMessages().FirstOrDefault(msg => msg.Id == m);
            if (msg != null)
                SelectedMessage = msg.Selector;
        });

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
                     .Subscribe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .Subscribe();

        SelectModelCmd = ReactiveCommand.Create((string model) => { Settings.Model = model; });

        CancelEditCmd = ReactiveCommand.Create(
            () =>
            {
                EditMessage = null;
                Prompt = new();
            });

        // load?

        this.WhenAnyValue(vm => vm.GlobalSettings)
            .Select(s => s.WhenAnyValue(x => x.OpenAi.ApiKeys))
            .Switch()
            .Select(s => s != null && !string.IsNullOrEmpty(s))
            .ObserveOnMainThread()
            .Do(i => IsOpenAIReady = i)
            .Subscribe();

        this.WhenAnyValue(vm => vm.SelectedMessage)
            .Select(_ => Unit.Default)
            .InvokeCommand(CancelEditCmd);

        //this.WhenAnyValue(vm => vm.Messages)
        //    .Skip(1)
        //    .Select(_ => ToDbConversation())
        //    .Do(convo =>
        //    {
        //        var json = JsonConvert.SerializeObject(convo, Formatting.Indented);
        //    })
        //    .Subscribe();

        //SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        //{
        //    await using var ctx = Services.Container.Resolve<UserProfileDbContext>();

        //    if (IsNew)
        //    {
        //        var convo = this.ToDbConversation();
        //        await ctx.AddAsync<DbConversation>(convo);
        //    }           
        //});

        DebugCmd = ReactiveCommand.Create(() =>
        {
            //var _ = JsonConvert.SerializeObject(this.ToDbConversation(), Formatting.Indented);
        });

        Activator.Activated.Take(1)
                 .Select(_ => this.WhenAnyValue(vm => vm.IsOpenAIReady)
                                  .Where(i => i)
                                  .Select(_ => Unit.Default))
                 .Switch()
                 .InvokeCommand(LoadModelsCmd);

        if (Debugging.Enabled &&
            Debugging.MockMessages &&
            Debugging.AutoSendFirstMessage)
            Activator.Activated.Take(1).InvokeCommand(AddCmd);

        this.WhenActivated(disposables =>
        {
            Debug.WriteLine($"Activated {GetType()} - {Name}");
            Disposable.Create(() => Debug.WriteLine($"Deactivated {GetType()} - {Name}")).DisposeWith(disposables);


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

public class PromptVm : ViewModel
{
    [Reactive] public string Contents { get; set; }
}
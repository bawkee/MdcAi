namespace MdcAi.ChatUI.ViewModels;

using Windows.Storage;
using OpenAiApi;
using LocalDal;
using Microsoft.EntityFrameworkCore;
using Mapster;
using Properties;

public class ConversationVm : ActivatableViewModel
{
    public IOpenAiApi Api { get; }
    public SettingsVm GlobalSettings { get; }
    public string Id { get; set; }
    public ChatSettingsVm Settings { get; }
    [Reactive] public ConversationsVm Conversations { get; set; }
    [Reactive] public DateTime CreatedTs { get; set; }
    [Reactive] public string Name { get; set; }
    [Reactive] public string IdCategory { get; set; }
    [Reactive] public string IdSettingsOverride { get; set; }
    [Reactive] public string CategoryName { get; set; }
    [Reactive] public ChatMessageSelectorVm Head { get; set; }
    [Reactive] public ChatMessageSelectorVm Tail { get; private set; }
    [Reactive] public ChatMessageSelectorVm SelectedMessage { get; set; }
    [Reactive] public ChatMessageSelectorVm EditMessage { get; set; }
    [Reactive] public PromptVm Prompt { get; set; } = new();
    [Reactive] public bool IsAIReady { get; private set; }
    [Reactive] public AiModel[] Models { get; private set; }
    [Reactive] public bool IsLoadingModels { get; private set; }
    [Reactive] public bool IsDirty { get; private set; } = true;
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public bool CanSendPrompt { get; private set; }
    [Reactive] public bool CanEdit { get; private set; }
    [Reactive] public bool CanRegenerate { get; private set; }
    [Reactive] public bool IsLoading { get; private set; }
    [Reactive] public bool IsCompleting { get; private set; }
    [Reactive] public bool SettingsOverriden { get; private set; }
    [Reactive] public bool IsNew { get; private set; }
    [Reactive] public bool IsEmpty { get; private set; } = true;
    [Reactive] public bool ShowGettingStartedTips { get; private set; }
    [Reactive] public bool ShowReadOnlyNotice { get; private set; }

    public ReactiveCommand<Unit, Unit> DebugGeneratePromptCmd { get; }
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
    public ReactiveCommand<Unit, Unit> SaveCmd { get; }
    public ReactiveCommand<Unit, DbConversation> LoadCmd { get; }
    public ReactiveCommand<Unit, Unit> ResetSettingsCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveSettingsCmd { get; set; }
    [Reactive] public ReactiveCommand<Unit, bool> EditSettingsCmd { get; set; }
    public ReactiveCommand<Unit, Unit> TurnOffGettingStartedTipCmd { get; }

    [Reactive] public ObservableCollection<ChatMessageVm> Messages { get; set; }
    [Reactive] public WebViewRequestDto LastMessagesRequest { get; set; }
    public HashSet<string> MessageTrashBin { get; } = new();

    public ConversationVm(IOpenAiApi api, SettingsVm globalSettings, ChatSettingsVm chatSettings)
    {
        Api = api;
        GlobalSettings = globalSettings;
        Settings = chatSettings;
        Id = Guid.NewGuid().ToString();
        CreatedTs = DateTime.Now;
        Name = "My Conversation";
        IsNew = true;

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

        this.WhenAnyValue(vm => vm.Messages)
            .WhereNotNull()
            .Select(m => m.WhenAnyValue(x => x.Count)
                          .Select(c => c > 0))
            .Switch()
            .Do(v => IsEmpty = !v)
            .SubscribeSafe();

        Observable.CombineLatest(
                      this.WhenAnyValue(vm => vm.IsEmpty),
                      GlobalChatSettings.Default.WhenAnyValue(s => s.ShowGettingStartedConvoTip))
                  .Select(l => l.All(x => x))
                  .ObserveOnMainThread()
                  .Do(v => ShowGettingStartedTips = v)
                  .SubscribeSafe();

        TurnOffGettingStartedTipCmd = ReactiveCommand.Create(
            () => { GlobalChatSettings.Default.ShowGettingStartedConvoTip = false; });

        EditSelectedCmd = ReactiveCommand.Create(
            () =>
            {
                EditMessage = SelectedMessage;
                Prompt = new()
                {
                    Contents = EditMessage.Message.Content
                };
            },
            this.WhenAnyValue(vm => vm.CanEdit));

        Observable.CombineLatest(
                      this.WhenAnyValue(vm => vm.SelectedMessage)
                          .Select(m => m?.Message.Role == ChatMessageRole.User),
                      this.WhenAnyValue(vm => vm.IsAIReady))
                  .Do(v => CanEdit = v.All(x => x))
                  .SubscribeSafe();

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
            this.WhenAnyValue(vm => vm.CanRegenerate));

        Observable.CombineLatest(
                      this.WhenAnyValue(vm => vm.SelectedMessage)
                          .Select(m => m?.Message.Role == ChatMessageRole.System && m == Tail),
                      this.WhenAnyValue(vm => vm.IsAIReady))
                  .Do(v => CanRegenerate = v.All(x => x))
                  .SubscribeSafe();

        SendPromptCmd = ReactiveCommand.CreateFromObservable(
            () => Observable
                  .Return(new
                  {
                      Message = EditMessage == null ?
                          new ChatMessageVm(this, ChatMessageRole.User)
                          {
                              Content = Prompt.Contents,
                              Previous = Tail?.Message,
                              Settings = Settings,
                          } :
                          new ChatMessageVm(this, ChatMessageRole.User, EditMessage)
                          {
                              Content = Prompt.Contents,
                              Previous = EditMessage.Message.Previous,
                              Settings = Settings,
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
            this.WhenAnyValue(vm => vm.CanSendPrompt));

        SendPromptCmd.Do(_ => Prompt = new())
                     .SubscribeSafe();

        this.WhenAnyValue(vm => vm.IsLoadingModels,
                          vm => vm.Prompt.Contents,
                          vm => vm.IsAIReady)
            .Do(x => CanSendPrompt = !x.Item1 &&
                                     !string.IsNullOrEmpty(x.Item2) &&
                                     IsAIReady)
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
                return ChatSettingsVm.MockModels;
            return await api.GetModels();
        });

        LoadModelsCmd.ObserveOnMainThread()
                     .Do(models => Models = models.Where(m => m.ModelID.StartsWith("gpt"))
                                                  .ToArray())
                     .SubscribeSafe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .SubscribeSafe();

        SelectModelCmd = ReactiveCommand.Create((string model) => { Settings.SelectedModel = model; });

        CancelEditCmd = ReactiveCommand.Create(
            () =>
            {
                EditMessage = null;
                Prompt = new();
            });

        LoadCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.FromAsync(async ct =>
                            {
                                await using var ctx = AppServices.GetUserProfileDb();
                                var convo = await ctx.Conversations
                                                     .Include(c => c.Messages)
                                                     .FirstOrDefaultAsync(c => c.IdConversation == Id);
                                return ct.IsCancellationRequested ? null : convo;
                            })
                            .WhereNotNull()
                            .ObserveOnMainThread()
                            .Do(convo =>
                            {
                                Name = convo.Name;
                                CreatedTs = convo.CreatedTs;
                                IdCategory = convo.IdCategory;
                                IdSettingsOverride = convo.IdSettingsOverride;
                                IsTrash = convo.IsTrash;
                                Head = convo.Messages.FromDbMessages(this);
                                IsDirty = false;
                                IsNew = false;
                            }));

        LoadCmd.IsExecuting
               .ObserveOnMainThread()
               .Do(i => IsLoading = i)
               .SubscribeSafe();

        // Load chat settings from the parent category or from an override if there is one
        Observable.CombineLatest(
                      this.WhenAnyValue(vm => vm.IdSettingsOverride),
                      this.WhenAnyValue(vm => vm.IdCategory).Where(c => c != null))
                  .Throttle(TimeSpan.FromMilliseconds(50))
                  .Select(_ => Observable.FromAsync(async () =>
                  {
                      string id;

                      if (IdSettingsOverride == null)
                      {
                          await using var ctx = AppServices.GetUserProfileDb();
                          id = ctx.Categories
                                  .First(c => c.IdCategory == IdCategory)
                                  .IdSettings;
                      }
                      else
                          id = IdSettingsOverride;

                      return id;
                  }))
                  .Switch()
                  .Where(id => Settings.IdSettings != id) // Don't reload
                  .SelectMany(id => Settings.LoadCmd.Execute(id))
                  .ObserveOnMainThread()
                  .SubscribeSafe();

        // Reload settings on subsequent activations since they can be changed externally (category editor)
        Activator.Activated
                 .Skip(1)
                 .Where(_ => IdSettingsOverride == null)
                 .InvokeCommand(Settings.ReloadCmd);

        // Save settings, or if convo is new then don't, it will be saved when saving the convo.
        SaveSettingsCmd = ReactiveCommand.CreateFromObservable(
            () => IsNew ?
                Observable.Return(Unit.Default) :
                Observable.Using(
                    () => AppServices.GetUserProfileDbTrans(),
                    // We have to save some conversation data and/or the settings (separate entity)
                    token => (IdSettingsOverride == null ?
                                 Observable.Return(Unit.Default) :
                                 Settings.SaveCmd.Execute(new()
                                 {
                                     Ctx = token.Ctx
                                 }))
                             .Select(_ => Observable.FromAsync(async () =>
                             {
                                 await token.Ctx.Conversations
                                            .Where(c => c.IdConversation == Id)
                                            .ExecuteUpdateAsync(c => c.SetProperty(p => p.IdSettingsOverride, IdSettingsOverride));
                             }))
                             .Switch()
                             .Do(_ => token.Trans.Commit())),
            this.WhenAnyValue(vm => vm.Settings.IsDirty));

        // Edit settins is a command that the view can create to show the edit dialog
        this.WhenAnyValue(vm => vm.EditSettingsCmd)
            .WhereNotNull()
            .Select(cmd => cmd.Where(v => v)
                              .Do(_ =>
                              {
                                  // If settings changed, convert the category settings into an 'override' one
                                  if (Settings.IsDirty)
                                      IdSettingsOverride ??= Settings.IdSettings = Id + "-setting";
                              })
                              .Where(_ => Settings.IdSettings == IdSettingsOverride))
            .Switch()
            .Select(_ => Unit.Default)
            .InvokeCommand(SaveSettingsCmd);

        this.WhenAnyValue(vm => vm.IdSettingsOverride)
            .Select(v => v != null)
            .ObserveOnMainThread()
            .Do(v => SettingsOverriden = v)
            .SubscribeSafe();

        ResetSettingsCmd = ReactiveCommand.CreateFromObservable(
            () =>
            {
                var idOverride = IdSettingsOverride;
                IdSettingsOverride = null;
                return IsNew ?
                    Observable.Return(Unit.Default) :
                    SaveSettingsCmd
                        .Execute()
                        .Select(_ => Observable.FromAsync(
                                    async () =>
                                    {
                                        // Delete the now unused setting override
                                        await using var ctx = AppServices.GetUserProfileDb();
                                        await ctx.ChatSettings.Where(s => s.IdSettings == idOverride).ExecuteDeleteAsync();
                                    }))
                        .Switch();
            },
            this.WhenAnyValue(vm => vm.SettingsOverriden));

        SaveCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Using(
                () => AppServices.GetUserProfileDbTrans(),
                token =>
                {
                    var save = Observable.FromAsync(
                        async () =>
                        {
                            var convo = this.ToDbConversation();
                            var existingConvo = await token.Ctx.Conversations.FirstOrDefaultAsync(c => c.IdConversation == Id);

                            if (existingConvo == null)
                                token.Ctx.Conversations.Add(convo);
                            else
                            {
                                this.Adapt(existingConvo);
                                await token.Ctx.Messages.Where(m => MessageTrashBin.Contains(m.IdMessage))
                                           .ExecuteDeleteAsync();
                                await Task.WhenAll(
                                    convo.Messages.Select(msg => token.Ctx.Messages.Upsert(msg).RunAsync()));
                            }

                            await token.Ctx.SaveChangesAsync();
                        });

                    return (SettingsOverriden ?
                               Settings.SaveCmd.Execute(new()
                               {
                                   Ctx = token.Ctx
                               }) : // Save settings first, a separate entity
                               Observable.Return(Unit.Default))
                           .Select(_ => save)
                           .Switch()
                           .Do(_ => token.Trans.Commit())
                           .ObserveOnMainThread()
                           .Do(_ =>
                           {
                               IsDirty = false;
                               IsNew = false;
                           });
                }),
            this.WhenAnyValue(vm => vm.IsDirty, vm => vm.Messages.Count)
                .Select(_ => IsDirty && Messages.Count > 0));

        this.WhenAnyValue(vm => vm.Tail)
            .Where(t => t?.Message?.Role == ChatMessageRole.System)
            .Select(t => t.Message.CompleteCmd
                          .Select(_ => t.Message.WhenAnyValue(m => m.IsCompleting))
                          .Switch())
            .Switch()
            .ObserveOnMainThread()
            .Do(i => IsCompleting = i)
            .SubscribeSafe();

        // Auto save whenever completion ends or message is deleted
        Observable.Merge(
                      this.WhenAnyValue(vm => vm.IsCompleting)
                          .Skip(1)
                          .Where(i => !i)
                          .Select(_ => Unit.Default),
                      DeleteSelectedCmd)
                  .Throttle(TimeSpan.FromMilliseconds(500))
                  .Select(_ => Unit.Default)
                  .InvokeCommand(SaveCmd);

        this.WhenAnyValue(vm => vm.GlobalSettings)
            .Select(s => s.WhenAnyValue(x => x.OpenAi.ApiKeys))
            .Switch()
            .Select(s => s != null && !string.IsNullOrEmpty(s))
            .ObserveOnMainThread()
            .Do(i => IsAIReady = i)
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.SelectedMessage)
            .Select(_ => Unit.Default)
            .InvokeCommand(CancelEditCmd);

        this.WhenAnyValue(vm => vm.IdCategory)
            .WhereNotNull()
            .SelectMany(id => Observable.FromAsync(async () =>
            {
                await using var ctx = AppServices.GetUserProfileDb();
                return await ctx.Categories.FirstOrDefaultAsync(c => c.IdCategory == id);
            }))
            .ObserveOnMainThread()
            .Do(c => CategoryName = c.Name)
            .SubscribeSafe();

        LoadCmd.Select(_ => this.WhenAnyValue(vm => vm.IsDirty)
                                .Where(v => !v))
               .Switch()
               .Select(_ => Observable.CombineLatest(
                                          this.WhenAnyValue(vm => vm.Messages)
                                              .Select(i => HashCode.Combine(i.Select(j => j.Content?.GetHashCode() ?? 0))),
                                          this.WhenAnyValue(vm => vm.Name).Select(i => i?.GetHashCode() ?? 0),
                                          this.WhenAnyValue(vm => vm.IdCategory).Select(i => i?.GetHashCode() ?? 0))
                                      .Select(i => HashCode.Combine(i))
                                      .Skip(1)
                                      .DistinctUntilChanged()
                                      .Take(1))
               .Switch()
               .Do(_ => IsDirty = true)
               .SubscribeSafe();

        this.WhenAnyValue(vm => vm.ShowGettingStartedTips,
                          vm => vm.IsNew,
                          vm => vm.IsAIReady)
            .Do(_ => ShowReadOnlyNotice = !ShowGettingStartedTips &&
                                          !IsNew &&
                                          !IsAIReady)
            .SubscribeSafe();

        Activator.Activated.Take(1)
                 .Select(_ => this.WhenAnyValue(vm => vm.IsAIReady)
                                  .Where(i => i)
                                  .Select(_ => Unit.Default))
                 .Switch()
                 .InvokeCommand(LoadModelsCmd);

        #region Debugging

        if (Debugging.Enabled &&
            Debugging.MockMessages &&
            Debugging.AutoSendFirstMessage)
            Activator.Activated.Take(1).InvokeCommand(DebugGeneratePromptCmd);

        DebugCmd = ReactiveCommand.Create(() =>
        {
            // Debug stuff?
        });

        DebugGeneratePromptCmd = ReactiveCommand.CreateFromObservable(
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
                          Settings = Settings,
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

        #endregion

        this.WhenActivated(disposables =>
        {
            //Debug.WriteLine($"Activated {Name}");
            //Disposable.Create(() => Debug.WriteLine($"Deactivated {Name}")).DisposeWith(disposables);

            // Trigger completions when user posts a message
            this.WhenAnyValue(vm => vm.Tail)
                .Where(t => t?.Message.Role == ChatMessageRole.User)
                .Select(t => new
                {
                    Tail = t,
                    Completion = new ChatMessageVm(this, ChatMessageRole.System)
                    {
                        Previous = t.Message,
                        Settings = t.Message.Settings,
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
                .Do(r => LastMessagesRequest = r)
                .SubscribeSafe()
                .DisposeWith(disposables);

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

    static ConversationVm()
    {
        TypeAdapterConfig<ConversationVm, DbConversation>
            .NewConfig()
            .IgnoreMember((member, _) => !member.Type.IsBuiltInConvertibleType());
    }
}

public class PromptVm : ViewModel
{
    [Reactive] public string Contents { get; set; }
}
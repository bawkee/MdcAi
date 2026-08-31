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

using Windows.Storage;
using OpenAiApi;
using LocalDal;
using Microsoft.EntityFrameworkCore;
using Mapster;
using Properties;
using MdcAi.ChatUI.Sessions;
using MdcAi.ChatCore.Sessions;

public class ConversationVm : ActivatableViewModel, ILogging
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

    /// <summary>The model this conversation uses right now. Independent of the persisted
    /// default (Settings.Model): the user can pick something transient, and loading a
    /// conversation defaults it to the model that produced the last AI reply.
    /// Deliberately lives on the conversation, NOT on ChatSettingsVm - the settings object
    /// is shared/persisted/reloaded by too many paths for it to also own working state.</summary>
    [Reactive] public string SelectedModel { get; set; }

    /// <summary>Pretty label for the model picker ("OpenRouter · Gemma 4 31B"), recomputed
    /// whenever the selection or the catalog changes. Null selection -> "".</summary>
    [Reactive] public string SelectedModelLabel { get; private set; }

    /// <summary>True once the user explicitly picked a model for this conversation (via the
    /// send-button picker). Auto-deriving on load never stomps a deliberate pick.</summary>
    private bool _modelUserPicked;

    /// <summary>The reasoning effort this conversation uses right now ("low"/"medium"/"high",
    /// or whatever the model advertises). Deliberately mirrors <see cref="SelectedModel"/>:
    /// it lives on the conversation (transient working state), is persisted per-message so
    /// reloads re-derive from the last reply, and falls back to the category default (or the
    /// level closest to medium when there's no stored default). Null = the current model has
    /// no effort support - no effort UI, nothing sent.</summary>
    [Reactive] public string SelectedEffort { get; set; }

    /// <summary>Effort label for the picker button ("Effort: Medium"). Empty when the current
    /// model has no effort support (or nothing resolved yet).</summary>
    [Reactive] public string SelectedEffortLabel { get; private set; }

    /// <summary>True once the user explicitly picked an effort level for this conversation.
    /// Mirror of <see cref="_modelUserPicked"/> - auto-deriving never stomps a deliberate pick.</summary>
    private bool _effortUserPicked;

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

    /// <summary>Workspace tools are OFF by default; enabling requires a workspace folder.
    /// Per-conversation on purpose - never silently global (DSH proposal §6.4).</summary>
    [Reactive] public bool ToolsEnabled { get; set; }

    /// <summary>Selected workspace folder for this conversation; null until tools are enabled.</summary>
    [Reactive] public string WorkspacePath { get; set; }

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
    public ReactiveCommand<string, Unit> SelectEffortCmd { get; }
    public ReactiveCommand<Unit, Unit> SendPromptCmd { get; }
    public ReactiveCommand<Unit, Unit> DebugCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveCmd { get; }
    public ReactiveCommand<Unit, DbConversation> LoadCmd { get; }
    public ReactiveCommand<Unit, Unit> ResetSettingsCmd { get; }
    public ReactiveCommand<Unit, Unit> SaveSettingsCmd { get; set; }
    [Reactive] public ReactiveCommand<Unit, bool> EditSettingsCmd { get; set; }
    public ReactiveCommand<Unit, Unit> TurnOffGettingStartedTipCmd { get; }

    /// <summary>Explicit agentic turn runner (DSH proposal §4): tool-enabled conversations run
    /// their whole step loop through ChatSessionService instead of the tail-driven surrogate.</summary>
    public ReactiveCommand<Unit, Unit> RunTurnCmd { get; }
    public ReactiveCommand<Unit, Unit> StopSessionCmd { get; }

    [Reactive] public ObservableCollection<ChatMessageVm> Messages { get; set; }
    [Reactive] public WebViewRequestDto LastMessagesRequest { get; set; }
    public HashSet<string> MessageTrashBin { get; } = new();

    private readonly ChatCore.Sessions.ChatSessionService _sessionService;
    private readonly Sessions.ConversationSessionController _controller;

    public ConversationVm(IOpenAiApi api, SettingsVm globalSettings, ChatSettingsVm chatSettings)
    {
        Api = api;
        GlobalSettings = globalSettings;
        Settings = chatSettings;
        Id = Guid.NewGuid().ToString();
        CreatedTs = DateTime.Now;
        Name = "My Conversation";
        IsNew = true;

        // Agentic machinery: one shared registry/service (stateless across turns) and one
        // turn controller, so a conversation is never executing two agentic turns at once.
        _sessionService = ConversationSessionServices.Create(api);
        _controller = new ConversationSessionController();

        // Explicit agentic turn runner. Tools-enabled conversations are the ONLY path through
        // ChatSessionService for now; the classic tail-driven subscription below stays for
        // tool-disabled chat until the full switch-over (DSH proposal §17.2).
        RunTurnCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var turn = BuildAgenticTurn();
            var sink = new ConversationChatSessionSink(this);
            sink.StartTurn(new SessionTurnContext(
                turn.TurnId,
                turn.TriggerMessageId,
                turn.ProviderKey,
                turn.Model,
                turn.Effort,
                turn.WorkspacePath,
                turn.Origin.ToString().ToLowerInvariant()));

            await _controller.RunAsync(async ct =>
                await _sessionService.RunTurnAsync(turn, sink, ct));
        });

        StopSessionCmd = ReactiveCommand.Create(
            () => _controller.Stop(),
            this.WhenAnyValue(vm => vm.IsCompleting));

        // In agentic mode the controller drives IsCompleting; the classic mode has its own
        // subscription below (they are mutually exclusive via ToolsEnabled).
        Observable.FromEvent(h => _controller.ActiveChanged += h, h => _controller.ActiveChanged -= h)
                  .StartWith(Unit.Default)
                  .Select(_ => _controller.IsTurnActive)
                  .ObserveOnMainThread()
                  .Where(_ => ToolsEnabled)
                  .Do(active => IsCompleting = active)
                  .SubscribeSafe();

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
            () =>
            {
                // Agentic regenerate = re-run ONLY the final model step over the already
                // accepted tool results. It must never repeat writes or PowerShell.
                if (ToolsEnabled)
                    return RegenerateAgenticTail();

                return SelectedMessage.Message.CompleteCmd.Execute(Unit.Default)
                                     .Select(_ => Unit.Default);
            },
            this.WhenAnyValue(vm => vm.CanRegenerate));

        Observable.CombineLatest(
                      this.WhenAnyValue(vm => vm.SelectedMessage)
                          .Select(m => m?.Message.Role == ChatMessageRole.Assistant && m == Tail),
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
                              Previous = Tail?.Message
                          } :
                          new ChatMessageVm(this, ChatMessageRole.User, EditMessage)
                          {
                              Content = Prompt.Contents,
                              Previous = EditMessage.Message.Previous
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

                      // Agentic conversation: the user message is appended, then the explicit
                      // turn runner takes over (the tail-driven subscription is off for tools).
                      if (ToolsEnabled)
                          RunTurnCmd.Execute().Subscribe();
                  })
                  .Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.CanSendPrompt));

        SendPromptCmd.Do(_ => Prompt = new())
                     .SubscribeSafe();

        this.WhenAnyValue(vm => vm.IsLoadingModels,
                          vm => vm.Prompt.Contents,
                          vm => vm.IsAIReady,
                          vm => vm.IsCompleting)
            .Do(x => CanSendPrompt = !x.Item1 &&
                                     !string.IsNullOrEmpty(x.Item2) &&
                                     x.Item3 &&
                                     !x.Item4)
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
            return await api.GetAllModels();
        });

        LoadModelsCmd.ObserveOnMainThread()
                     .Do(models => Models = models.Where(m => m.IsConversational || m.IsReasoning).ToArray())
                     .SubscribeSafe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .SubscribeSafe();

        // The picker label is a derived value of (working model, catalog) so it can't go
        // stale: it re-renders when either the selection or the loaded catalog changes
        // (e.g. raw id -> "Provider · Name" the moment models finish loading).
        this.WhenAnyValue(vm => vm.SelectedModel, vm => vm.Models)
            .Select(p => FormatModelLabel(p.Item1, p.Item2))
            .ObserveOnMainThread()
            .Do(l => SelectedModelLabel = l)
            .SubscribeSafe();

        // Same for the effort label ("Effort: Medium"); empty = the current model has no
        // effort support (or nothing resolved yet), which keeps effort-less models clean.
        this.WhenAnyValue(vm => vm.SelectedEffort)
            .ObserveOnMainThread()
            .Do(e => SelectedEffortLabel = e == null ? "" : $"Effort: {e}")
            .SubscribeSafe();

        // Trace who sets the working model and why (debug level, NLog app-*.log).
        this.WhenAnyValue(vm => vm.SelectedModel)
            .WhereNotNull()
            .Do(m => this.LogDebug("Working model -> {Model}", m))
            .SubscribeSafe();

        SelectModelCmd = ReactiveCommand.Create((string model) =>
        {
            _modelUserPicked = true;
            SelectedModel = model;
            this.LogDebug("User picked model {Model}", model);
        });

        SelectEffortCmd = ReactiveCommand.Create((string effort) =>
        {
            _effortUserPicked = true;
            SelectedEffort = effort;
            this.LogDebug("User picked effort {Effort}", effort);
        });

        // New conversations start at the persisted default; reloads re-derive from the last
        // reply when the conversation has one (per-message provenance) and else keep the
        // current selection. Runs on settings (re)load and when the message tree arrives,
        // so ordering never matters. The same triggers re-derive the working effort.
        Observable.Merge(
                      this.WhenAnyValue(vm => vm.Head).WhereNotNull().Select(_ => Unit.Default),
                      Settings.LoadCmd.Select(_ => Unit.Default))
                  .Throttle(TimeSpan.FromMilliseconds(50))
                  .ObserveOnMainThread()
                  .Do(_ =>
                  {
                      ApplySelectedModel();
                      ApplySelectedEffort();
                  })
                  .SubscribeSafe();

        // The effort depends on the model in use (its supported levels), so re-derive it
        // whenever the model or the catalog changes too - e.g. picking an effortless model
        // clears the effort, picking an effort-capable one snaps it back to a valid level.
        this.WhenAnyValue(vm => vm.SelectedModel, vm => vm.Models)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(_ => ApplySelectedEffort())
            .SubscribeSafe();

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
                                ToolsEnabled = convo.ToolsEnabled;
                                WorkspacePath = convo.WorkspacePath;
                                Head = convo.Messages.FromDbMessages(this);
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
                 .SelectMany(_ => Settings.ReloadCmd.Execute())
                 .ObserveOnMainThread()
                 .SubscribeSafe();

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
                            Debug.WriteLine("Saving");

                            var convo = this.ToDbConversation();
                            var existingConvo = await token.Ctx.Conversations.FirstOrDefaultAsync(c => c.IdConversation == Id);

                            if (existingConvo == null)
                                token.Ctx.Conversations.Add(convo);
                            else
                            {
                                this.Adapt(existingConvo);
                                await token.Ctx.Messages.Where(m => MessageTrashBin.Contains(m.IdMessage))
                                           .ExecuteDeleteAsync();
                                // Sequential upserts on ONE context - EF contexts are not
                                // thread-safe, so Task.WhenAll over the same context is unsafe
                                // (DSH proposal §6.5). Ordered writes inside the transaction.
                                foreach (var msg in convo.Messages)
                                    await token.Ctx.Messages.Upsert(msg).RunAsync();
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
                               IsNew = false;
                           });
                }));

        // Trigger completions when user posts a message. Kept at VM-constructor level (NOT
        // WhenActivated) on purpose: ReactiveCommand.Execute() cancels the running stream the
        // moment its subscription is disposed, and the old activation-bound chain got disposed
        // the instant the user switched to another conversation - killing the in-flight
        // completion (this was "the current conversation stops when I move away"). Each
        // conversation already owns its own VM instance (created lazily on first open, retained
        // afterwards), so ctor-level subscriptions let any number of conversations stream in
        // parallel without interfering with each other or with the visible UI.
        //
        // Agentic conversations (ToolsEnabled) deliberately do NOT use this tail-driven
        // surrogate: appending assistant/tool nodes changes Tail repeatedly, which would cancel
        // and replace work mid-turn. They run through RunTurnCmd explicitly (DSH §4).
        this.WhenAnyValue(vm => vm.Tail, vm => vm.ToolsEnabled)
            .Where(x => x.Item1?.Message.Role == ChatMessageRole.User && !x.Item2)
            .Select(x => new
            {
                Tail = x.Item1,
                Completion = new ChatMessageVm(this, ChatMessageRole.Assistant)
                {
                    Previous = x.Item1.Message
                }
            })
            .Do(x => x.Tail.Message.Next = x.Completion)
            .Select(x => x.Completion.CompleteCmd.Execute())
            .Switch()
            .Retry()
            .SubscribeSafe();

        // Classic (tools-disabled) IsCompleting follows the tail assistant's CompleteCmd; the
        // agentic path drives IsCompleting from the controller instead.
        this.WhenAnyValue(vm => vm.Tail, vm => vm.ToolsEnabled)
            .Where(x => x.Item1?.Message?.Role == ChatMessageRole.Assistant && !x.Item2)
            .Select(x => x.Item1.Message.CompleteCmd
                          .Select(_ => x.Item1.Message.WhenAnyValue(m => m.IsCompleting))
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
                      DeleteSelectedCmd,
                      NextVersionCmd,
                      PrevVersionCmd)
                  .Throttle(TimeSpan.FromMilliseconds(500))
                  .Select(_ => Unit.Default)
                  .InvokeCommand(SaveCmd);

        // The app is "AI ready" as soon as any provider has a usable key - the conversation's
        // model decides which provider actually serves it.
        this.WhenAnyValue(vm => vm.GlobalSettings)
            .Select(s => s.WhenAnyValue(x => x.IsAnyProviderConfigured))
            .Switch()
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

        this.WhenAnyValue(vm => vm.ShowGettingStartedTips,
                          vm => vm.IsNew,
                          vm => vm.IsAIReady)
            .Do(_ => ShowReadOnlyNotice = !ShowGettingStartedTips &&
                                          !IsNew &&
                                          !IsAIReady)
            .SubscribeSafe();

        // Load models once the app becomes usable, and reload whenever any provider's key changes
        // (each provider has its own catalog, and adding/removing a key changes what's shown).
        Activator.Activated.Take(1)
                 .Select(_ => Observable.Merge(
                                   this.WhenAnyValue(vm => vm.IsAIReady)
                                       .Where(i => i)
                                       .Select(_ => Unit.Default),
                                   GlobalSettings.OpenAi.WhenAnyValue(vm => vm.ApiKey)
                                       .Skip(1)
                                       .Throttle(TimeSpan.FromMilliseconds(400))
                                       .Select(_ => Unit.Default),
                                   GlobalSettings.OpenRouter.WhenAnyValue(vm => vm.ApiKey)
                                       .Skip(1)
                                       .Throttle(TimeSpan.FromMilliseconds(400))
                                       .Select(_ => Unit.Default)))
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
                          Previous = Tail?.Message
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

            // Build request data to communicate with WebView for rendering
            this.WhenAnyValue(vm => vm.Messages)
                .WhereNotNull()
                .Select(m =>
                {
                    if (m.Count > 0)
                    {
                        // Repush when either the answer or the thinking block re-renders, so
                        // the WebView gets reasoning deltas while the model is still thinking.
                        // Sampled at a fixed cadence (not a trailing debounce - see ChatMessageVm):
                        // during a continuous stream a Throttle would never fire, so the WebView
                        // only received the final burst at the end of generation.
                        var last = m.Last();
                        return Observable.Merge(
                                   last.WhenAnyValue(vm => vm.HTMLContent),
                                   last.WhenAnyValue(vm => vm.ReasoningHTMLContent))
                               .Sample(TimeSpan.FromMilliseconds(33))
                               .Select(_ => m);
                    }
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
                                 .Where(m => m.Count > 0 && m.Last().Role == ChatMessageRole.Assistant))
                .Switch()
                .Do(m => SelectedMessage = m.Last().Selector)
                .SubscribeSafe()
                .DisposeWith(disposables);
        });
    }

    // The conversation remembers the model of every AI reply on the message itself (per-message
    // provenance). When the conversation loads (head arrives) or settings (re)load, point the
    // working model at:
    //   1. the model that produced the last assistant message (modern chats), else
    //   2. the category's default, but only once this conversation's settings actually loaded
    //      (IdSettings set) - before that Settings.Model is still the ctor placeholder, and
    //      caching it would make the picker "pick the first/placeholder model" on legacy chats.
    // A deliberate user pick is never stomped. SelectedModel is an OUTPUT here, never a source:
    // treating it as a fallback was exactly why provisional values stuck around.
    private void ApplySelectedModel()
    {
        var lastAiModel = Head?.Message.GetNextMessages()
                               .LastOrDefault(m => m.Role == ChatMessageRole.Assistant && m.Model != null)
                               ?.Model;

        var categoryDefault = Settings.IdSettings != null ? Settings.Model : null;

        var next = ResolveWorkingModel(lastAiModel, categoryDefault, SelectedModel, _modelUserPicked);

        this.LogDebug("Applying working model (userPicked={UserPicked}): lastReply={LastReply ?? \"<none>\"}, " +
                      "loaded={Loaded}, default={Default}, current={Current}, -> {Next}",
                      _modelUserPicked, lastAiModel, Settings.IdSettings != null, Settings.Model, SelectedModel, next);

        SelectedModel = next;
    }

    // Pure decision so the load-scenario matrix is trivially unit-testable.
    internal static string ResolveWorkingModel(string lastReplyModel, string categoryDefault, string current, bool userPicked) =>
        userPicked ? current : lastReplyModel ?? categoryDefault ?? current;

    // Effort resolves exactly like Model, with one extra wrinkle: the target domain is the
    // current model's supported efforts. A deliberate user pick is kept whenever it's still
    // valid for the model; anything else (or an invalid pick) falls back to the last
    // reply's effort -> the category default -> the level closest to medium (legacy
    // categories carry null effort, so "pick the middle one" is what null means here).
    // Models with no effort support resolve to null (nothing shown, nothing sent).
    private void ApplySelectedEffort()
    {
        var lastAiEffort = Head?.Message.GetNextMessages()
                               .LastOrDefault(m => m.Role == ChatMessageRole.Assistant && m.Effort != null)
                               ?.Effort;

        var categoryDefault = Settings.IdSettings != null ? Settings.Effort : null;

        var supported = Models?.FirstOrDefault(m => m.ModelID == SelectedModel)?.SupportedEfforts;

        var next = ResolveWorkingEffort(lastAiEffort, categoryDefault, SelectedEffort, _effortUserPicked, supported);

        this.LogDebug("Applying working effort (userPicked={UserPicked}): lastReply={LastReply ?? \"<none>\"}, " +
                      "loaded={Loaded}, default={Default}, current={Current}, supported={Supported}, -> {Next}",
                      _effortUserPicked, lastAiEffort, Settings.IdSettings != null, Settings.Effort, SelectedEffort,
                      supported == null ? "<none>" : string.Join(",", supported), next);

        SelectedEffort = next;
    }

    // Pure decision so the effort load-scenario matrix is trivially unit-testable.
    internal static string ResolveWorkingEffort(string lastReplyEffort, string categoryDefaultEffort, string current,
                                                bool userPicked, string[] supportedEfforts)
    {
        if (supportedEfforts == null || supportedEfforts.Length == 0)
            return null;

        var candidate = userPicked ? current : lastReplyEffort ?? categoryDefaultEffort ?? current;

        return candidate != null && supportedEfforts.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? candidate
            : AiEffort.ClosestToMedium(supportedEfforts);
    }

    private static string FormatModelLabel(string modelId, AiModel[] models)
    {
        if (modelId == null)
            return "";

        var model = models?.FirstOrDefault(x => x.ModelID == modelId);

        if (model == null)
            return modelId;

        return $"{AiProviders.Get(model.ProviderKey).DisplayName} · {model.DisplayLabel}";
    }

    /// <summary>Which provider the current working model belongs to (catalog-stamped when
    /// available, legacy slash heuristic otherwise).</summary>
    public string ResolveProviderKey() =>
        Models?.FirstOrDefault(m => m.ModelID == SelectedModel)?.ProviderKey
        ?? AiProviders.GetProviderForModelId(SelectedModel).Key;

    /// <summary>
    /// Builds the immutable agentic turn request. Provider/model/effort/tool schema are stamped
    /// once at turn start and never vary mid-turn (DSH proposal §9.2).
    /// </summary>
    private ChatTurnRequest BuildAgenticTurn()
    {
        var trigger = Head?.Message.GetNextMessages()
                           .LastOrDefault(m => m.Role == ChatMessageRole.User);

        return new ChatTurnRequest(
            Id,
            "turn-" + Guid.NewGuid().ToString("N"),
            trigger?.Id,
            ResolveProviderKey(),
            SelectedModel,
            SelectedEffort,
            Settings.Premise,
            WorkspacePath,
            ToolsEnabled ? ConversationSessionServices.BuiltInToolNames : Array.Empty<string>(),
            ChatTurnOrigin.Human,
            null, // Phase 1: no approval UI yet - mutating/process tools deny by policy
            ChatTurnLimits.Default);
    }

    /// <summary>
    /// Agentic regenerate: drop the final assistant node and re-run the LAST model step from the
    /// already accepted tool results. Never repeats writes or PowerShell - the tool results are
    /// already in the transcript the next request is derived from.
    /// </summary>
    private IObservable<Unit> RegenerateAgenticTail()
    {
        return Observable.Defer(() =>
        {
            var tail = SelectedMessage.Message;

            if (tail.Previous != null)
                tail.Previous.Next = null;
            else
                Head = null;

            return RunTurnCmd.Execute();
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
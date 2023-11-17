namespace ReactRendererPlayground.ViewModels;

using ReactiveUI.Fody.Helpers;
using ReactiveUI;
using Sala.Extensions.WinUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using System.Security.Policy;
using System.Diagnostics;
using Sala.Extensions.Orm;
using System.Reactive.Subjects;
using System.Text.Json;
using Newtonsoft.Json;
using Markdig;
using System.Reactive.Disposables;
using DynamicData.Binding;
using Mdc.OpenAiApi;

public class ConversationVm : ActivatableViewModel
{
    [Reactive] public string Name { get; set; }
    [Reactive] public ChatMessageSelectorVm Head { get; set; }
    [Reactive] public ChatMessageSelectorVm Tail { get; set; }
    [Reactive] public ChatMessageSelectorVm SelectedMessage { get; set; }
    [Reactive] public SettingsVm Settings { get; set; } = new();

    public ReactiveCommand<Unit, Unit> AddCmd { get; }
    public ReactiveCommand<Unit, Unit> EditCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteSelectedCmd { get; }
    public ReactiveCommand<string, Unit> SelectCmd { get; }

    [Reactive] public ObservableCollection<ChatMessageVm> Messages { get; set; }
    [Reactive] public WebViewRequestDto LastWebViewRequest { get; set; }

    public ConversationVm()
    {
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
            .Do(r => LastWebViewRequest = r)
            .Subscribe();


        AddCmd = ReactiveCommand.CreateFromObservable(
            () => Observable
                  .FromAsync(async () =>
                  {
                      var file = await StorageFile.GetFileFromApplicationUriAsync(
                          new Uri("ms-appx:///Assets/sample2.md"));
                      var contents = await FileIO.ReadTextAsync(file);
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

        EditCmd = ReactiveCommand.CreateFromObservable(
            () => Observable
                  .FromAsync(async () =>
                  {
                      var file = await StorageFile.GetFileFromApplicationUriAsync(
                          new Uri("ms-appx:///Assets/sample6.md"));
                      var contents = await FileIO.ReadTextAsync(file);
                      return new ChatMessageVm(this, ChatMessageRole.User, SelectedMessage)
                      {
                          Content = contents,
                          Previous = Tail.Message,
                          Settings = new(Settings),
                      };
                  })
                  .ObserveOnMainThread()
                  .Do(msg => SelectedMessage.Message = msg)
                  .Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.SelectedMessage)
                .Select(m => m != null));

        DeleteSelectedCmd = ReactiveCommand.CreateFromObservable(
            () => SelectedMessage?.Message.Role == ChatMessageRole.User ?
                SelectedMessage.DeleteCmd.Execute()
                               .ObserveOnMainThread()
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
                .Select(m => m != null));

        //DeleteSelectedCmd = ReactiveCommand.Create(() =>
        //{
        //    if (SelectedMessage?.Message.Role != ChatMessageRole.User)
        //        return;


        //    if (SelectedMessage == Head && SelectedMessage.Versions.Count == 0)
        //        Head = null;
        //    else
        //    {
        //        //SelectedMessage.Message.Previous.Next = null;               
        //        //SelectedMessage.Versions.Remove(SelectedMessage.Message);
        //    }

        //    SelectedMessage = null;
        //});

        SelectCmd = ReactiveCommand.Create((string m) =>
        {
            var msg = Head.Message.GetNextMessages().FirstOrDefault(msg => msg.Id == m);
            if (msg != null)
                SelectedMessage = msg.Selector;
        });

        this.WhenActivated(disposables =>
        {
            Observable.Return(Unit.Default)
                      .InvokeCommand(AddCmd)
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

public class PromptVm : ViewModel
{
    [Reactive] public SettingsVm Settings { get; set; }
    [Reactive] public string Contents { get; set; }
}

public class SettingsVm : ViewModel
{
    [Reactive] [Map] public string Model { get; set; } = AiModel.GPT35Turbo;
    [Reactive] [Map] public bool Streaming { get; set; } = false;

    public SettingsVm(SettingsVm copyFrom = null) { copyFrom?.MapTo(this); }
}

public class ChatMessageSelectorVm : ViewModel
{
    public ObservableCollection<ChatMessageVm> Versions { get; } = new();
    [Reactive] public int Version { get; private set; }
    [Reactive] public ChatMessageVm Message { get; set; }
    public ReactiveCommand<Unit, Unit> NextCmd { get; }
    public ReactiveCommand<Unit, Unit> PrevCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }

    private readonly IConnectableObservable<Unit> _changed;

    public ChatMessageSelectorVm(ChatMessageVm message)
    {
        Versions.Add(Message = message);

        _changed = Observable.Merge(this.WhenAnyValue(vm => vm.Message)
                                        .Select(_ => Unit.Default),
                                    Versions.ObserveCollectionChanges()
                                            .Select(_ => Unit.Default))
                             .Publish();

        _changed.Connect();

        _changed.ObserveOnMainThread()
                .Do(_ => Version = GetCurrentIndex() + 1)
                .Subscribe();

        PrevCmd = ReactiveCommand.Create(() => TraverseVersions(-1), CanTranverseLive(-1));
        NextCmd = ReactiveCommand.Create(() => TraverseVersions(1), CanTranverseLive(1));

        DeleteCmd = ReactiveCommand.Create(() =>
        {
            var ver = Version;
            Versions.Remove(Message);
            if (Message.Previous != null)
                Message.Previous.Next = null; // Dereference
            Message.Previous = null; // Dereference
            if (Versions.Any())
                Message = Versions[Math.Min(ver, Versions.Count) - 1];
        });
    }

    private int GetCurrentIndex() => Versions.IndexOf(Message);

    private bool CanTraverse(int dir)
    {
        var current = GetCurrentIndex();
        if (current == -1)
            return false;
        var proposed = current + dir;
        return proposed >= 0 && proposed < Versions.Count;
    }

    private IObservable<bool> CanTranverseLive(int dir) =>
        _changed.Select(_ => CanTraverse(dir))
                .ObserveOnMainThread();

    private void TraverseVersions(int direction)
    {
        if (!CanTraverse(direction))
            return;
        Message = Versions[GetCurrentIndex() + direction];
    }
}

/// <summary>
/// This is a doubly linked list, plus each message can diverge and we keep all the versions.
/// </summary>
public class ChatMessageVm : ViewModel
{
    private static int _total = 0;

    public string Id { get; }
    public string Role { get; }
    public ChatMessageSelectorVm Selector { get; }
    [Reactive] public SettingsVm Settings { get; set; } = new();
    [Reactive] public string Content { get; set; }
    [Reactive] public string HTMLContent { get; set; }
    public DateTime CreatedTs { get; }
    public ConversationVm Conversation { get; }
    public ChatMessageVm Previous { get; set; } // Previous item        
    [Reactive] public ChatMessageVm Next { get; set; } // Next item
    [Reactive] public bool IsCompleting { get; private set; } // Whether completion is in progress

    public ReactiveCommand<Unit, Unit> CompleteCmd { get; }
    public ReactiveCommand<Unit, Unit> StopCompletionCmd { get; }

    public ChatMessageVm(ConversationVm conversation, string role, ChatMessageSelectorVm selector = null)
    {
        Conversation = conversation;
        CreatedTs = DateTime.Now;
        Role = role;
        Id = (_total++).ToString(); //Guid.NewGuid().ToString();
        if (selector != null)
        {
            Selector = selector;
            Selector.Versions.Add(this);
        }
        else
            Selector = new(this);

        StopCompletionCmd = ReactiveCommand.Create(() => { });

        CompleteCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Return(Unit.Default)
                            .Do(_ => Content = null)
                            .Select(_ =>
                            {
                                if (Settings.Streaming)
                                    return CreateReplyStream()
                                           .TakeUntil(StopCompletionCmd)
                                           .ObserveOn(RxApp.MainThreadScheduler)
                                           .Do(s => Content = string.Concat(Content, s))
                                           .Select(_ => Unit.Default);

                                return Observable.FromAsync(async () => await GenerateContent())
                                                 .ObserveOnMainThread()
                                                 .Do(c => Content = c)
                                                 .Select(_ => Unit.Default);
                            })
                            .Switch());

        CompleteCmd.IsExecuting
                   .ObserveOn(RxApp.MainThreadScheduler)
                   .Do(i => IsCompleting = i)
                   .Subscribe();

        this.WhenAnyValue(vm => vm.Content)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .Select(c =>
            {
                if (Role == ChatMessageRole.User)
                    return string.IsNullOrEmpty(c) ? "" : Markdown.ToHtml(c);

                // TODO: Only add caret if it's streaming and then pls remove it from the content when done
                var caretMd = "'%caret%'";
                var caretHtml = "<span id=\"caret\"/>";
                if (string.IsNullOrEmpty(c))
                    return caretHtml;
                return Markdown.ToHtml(c + caretMd)
                               .Replace(caretMd, caretHtml);
            })
            .Do(h => HTMLContent = h)
            .Subscribe();
    }

    private async Task<string> GenerateContent()
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/sample9.md"));
        var contents = await FileIO.ReadTextAsync(file);
        return contents;
    }

    private IObservable<string> CreateReplyStream() =>
        Observable
            .FromAsync(async () => await GenerateContent())
            .SelectMany(c => c.Split(' ')
                              .ToObservable()
                              .Select(s => Observable.Timer(TimeSpan.FromMilliseconds(200))
                                                     .Select(_ => s + ' '))
                              .Concat());

    //private ChatRequest CreateRequest()
    //{
    //    var messages = new List<ChatMessage>(new[] { Current.CreateMessageRequest() });
    //    var currentParent = Previous?.Current;

    //    while (currentParent != null)
    //    {
    //        //currentParent.Model = Settings.Model; // ???
    //        messages.Insert(0, currentParent.CreateMessageRequest());
    //        currentParent = currentParent.Previous?.Current;
    //    }

    //    var req = new ChatRequest
    //    {
    //        Messages = messages,
    //        Model = Settings.Model
    //    };

    //    return req;
    //}

    private ChatMessage CreateMessageRequest() =>
        new()
        {
            Content = Content,
            Role = Role
        };
}

public static class ChatMessageVmExt
{
    public static IEnumerable<ChatMessageVm> GetNextMessages(this ChatMessageVm head)
    {
        var message = head.Selector.Message;
        while (message != null)
        {
            yield return message;
            message = message.Next?.Selector.Message;
        }
    }

    public static WebViewChatMessageDto GetWebViewDto(this ChatMessageVm m)
    {
        if (m == null)
            return null;

        return new()
        {
            Id = m.Id,
            Role = m.Role,
            Content = m.HTMLContent ?? $"<p>{m.Content}</p>",
            Version = m.Selector.Version,
            VersionCount = m.Selector.Versions.Count,
            CreatedTs = m.CreatedTs
        };
    }

    public static WebViewRequestDto CreateWebViewSetMessageRequest(this IEnumerable<ChatMessageVm> messages)
    {
        return new()
        {
            Name = "SetMessages",
            Data = new WebViewSetMessagesRequestDto { Messages = messages.Select(m => m.GetWebViewDto()).ToArray() }
        };
    }
}

public class WebViewRequestDto
{
    public string Name { get; set; }
    public object Data { get; set; }
}

public class WebViewSetMessagesRequestDto
{
    public WebViewChatMessageDto[] Messages { get; set; }
}

public class WebViewChatMessageDto
{
    public string Id { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public int Version { get; set; }
    public int VersionCount { get; set; }
    public DateTime? CreatedTs { get; set; }
}
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
using Markdig;
using System.Web;
using OpenAiApi;

/// <summary>
/// This is a doubly linked list, plus each message can diverge and we keep all the versions.
/// </summary>
public class ChatMessageVm : ViewModel, ILogging
{
    public string Id { get; set; }
    public string Role { get; set; }
    public ChatMessageSelectorVm Selector { get; }
    [Reactive] public string Content { get; set; }
    [Reactive] public string HTMLContent { get; set; }
    public DateTime CreatedTs { get; set; }
    public ConversationVm Conversation { get; }
    public ChatMessageVm Previous { get; set; } // Previous item        
    [Reactive] public ChatMessageVm Next { get; set; } // Next item
    [Reactive] public bool IsCompleting { get; private set; } // Whether completion is in progress

    public ReactiveCommand<Unit, string> CompleteCmd { get; }
    public ReactiveCommand<Unit, Unit> StopCompletionCmd { get; }

    private static readonly MarkdownPipeline _mdPipeline;

    static ChatMessageVm() { _mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build(); }

    public ChatMessageVm(ConversationVm conversation,
                         string role,
                         ChatMessageSelectorVm selector = null)
    {
        Conversation = conversation;
        CreatedTs = DateTime.Now;
        Role = role;
        Id = Guid.NewGuid().ToString();

        if (selector != null)
        {
            Selector = selector;
            Selector.Versions.Add(this);
        }
        else
            Selector = new(this);

        CompleteCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.Return(Unit.Default)
                            .Do(_ => Content = null) // Just because there can be such a big delay when regenerating
                            .Select(_ => Conversation.Settings.Streaming ?
                                        CreateGenerationStream()
                                            .TakeUntil(StopCompletionCmd)
                                            .Scan("", (a, b) => a + b) :
                                        Observable.FromAsync(() => GenerateResponse())
                                                  .TakeUntil(StopCompletionCmd))
                            .Switch()
                            .Catch((Exception ex) => Observable.Throw<string>(new CompletionException(ex))));

        CompleteCmd.ObserveOnMainThread()
                   .Do(c => Content = c)
                   .SubscribeSafe();

        StopCompletionCmd = ReactiveCommand.Create(() => { }, CompleteCmd.IsExecuting);

        CompleteCmd.IsExecuting
                   .ObserveOnMainThread()
                   .Do(i => IsCompleting = i)
                   .SubscribeSafe();

        const string stopMd = " *[Answer Cut Short by User]*";
        const string caretMd = "'%caret%'";
        const string caretHtml = "<span id=\"caret\"/>";

        this.WhenAnyValue(vm => vm.Content)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .ObserveOnMainThread()
            .Select(c =>
            {
                if (Role == ChatMessageRole.User)
                    return string.IsNullOrEmpty(c) ? "" : ToUserHtml(c);

                if (Next != null)
                    return string.IsNullOrEmpty(c) ? "" : Markdown.ToHtml(c);

                if (string.IsNullOrEmpty(c))
                    return caretHtml;

                // Hacky hack
                var actualCaretMd = caretMd;

                if (c.Trim().EndsWith("```"))
                    actualCaretMd = $"\r\n{caretMd}";

                var html = Markdown.ToHtml(c + actualCaretMd, _mdPipeline)
                                   .Replace(caretMd, caretHtml);

                return html;
            })
            .Do(h => HTMLContent = h)
            .LogAndRetry(this)
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.Content)
            .Throttle(TimeSpan.FromMilliseconds(2000))
            .ObserveOnMainThread()
            .Do(_ => HTMLContent = HTMLContent?.Replace(caretHtml, ""))
            .SubscribeSafe();

        StopCompletionCmd.ObserveOnMainThread()
                         .Do(_ =>
                         {
                             var c = Content;

                             if (string.IsNullOrEmpty(c))
                                 Content = stopMd;
                             else
                                 Content = Content + "\r\n" + stopMd;
                         })
                         .SubscribeSafe();

        // Remove caret from the html altogether when done
        CompleteCmd.IsExecuting
                   .SkipWhile(i => !i)
                   .DistinctUntilChanged()
                   .Where(i => !i)
                   .Throttle(TimeSpan.FromMilliseconds(1000))
                   .ObserveOnMainThread()
                   .Do(_ => HTMLContent = HTMLContent.Replace(caretHtml, ""))
                   .SubscribeSafe();
    }

    private static string ToUserHtml(string content) =>
        HttpUtility.HtmlEncode(content)
                   .Replace("\r", "<br />");

    private async Task<string> GenerateResponse()
    {
        if (Debugging.Enabled && Debugging.MockMessages)
        {
            await Task.Delay(500);

            if (Debugging.NumberedMessages)
                return $"Debug system {Debugging.SystemMessageCounter++}";

            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Dbg/Test2.md"));
            var contents = await FileIO.ReadTextAsync(file);
            return contents;
        }

        var req = CreateRequest();
        var completions = await Conversation.Api.CreateChatCompletions(req);
        return completions.Choices.LastOrDefault()?.Message.Content;
    }

    private IObservable<string> CreateGenerationStream()
    {
        if (Debugging.Enabled && Debugging.MockMessages)
            return Observable
                   .FromAsync(() => GenerateResponse())
                   .SelectMany(c => c.Split(' ')
                                     .ToObservable()
                                     .Select(s => Observable.Timer(TimeSpan.FromMilliseconds(200))
                                                            .Select(_ => s + ' '))
                                     .Concat());

        return Conversation.Api.CreateChatCompletionsStream(CreateRequest())
                           .ToObservable()
                           .Select(m => m.Choices.LastOrDefault()?.Delta.Content);
    }

    private ChatRequest CreateRequest()
    {
        var messages = new List<ChatMessage>();
        var currentParent = Previous;

        while (currentParent != null)
        {
            messages.Insert(0, currentParent.CreateMessageRequest());
            currentParent = currentParent.Previous;
        }

        // This is the spice, hard coded, because not including this could lead to trouble such as various md syntax
        // bugs and the AI mistakenly thinking it's on the OpenAI's chat bot. I left room to answer whatever it wants
        // or is instructed to previously, but still make it aware that it's inside this app nonetheless.
        const string premiseSpice =
            " Use md syntax and be sure to specify language for code blocks. SIDE NOTE: " +
            "For your awareness (and if asked), you are an AI used inside MDC AI which is " +
            "a Windows desktop app.";

        messages.Insert(0,
                        new()
                        {
                            Role = ChatMessageRole.System,
                            Content = Conversation.Settings.Premise + premiseSpice
                        });

        var req = new ChatRequest
        {
            Messages = messages,
            Model = Conversation.Settings.SelectedModel
        };

        return req;
    }

    private ChatMessage CreateMessageRequest() =>
        new()
        {
            Content = Content,
            Role = Role
        };
}

public class CompletionException : Exception
{
    public CompletionException(Exception innerEx)
        : base("There was en error while generating the response. You may try again by clicking the Regenerate button.", innerEx) { }
}
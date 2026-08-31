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
using Newtonsoft.Json.Linq;

/// <summary>
/// This is a doubly linked list, plus each message can diverge and we keep all the versions.
/// A node is the TRANSCRIPT/presentation side of one protocol message: the raw wire fields
/// (tool calls, structured reasoning, tool call id, provider key) live here so the fork tree
/// round-trips WITHOUT reconstruction from display state (DSH proposal §5.3 / §9.1).
/// </summary>
public class ChatMessageVm : ViewModel, ILogging
{
    public string Id { get; set; }
    public string Role { get; set; }
    public ChatMessageSelectorVm Selector { get; }
    [Reactive] public string Content { get; set; }
    [Reactive] public string HTMLContent { get; set; }

    /// <summary>Model id that (re)generated this message. Stamped when a completion starts
    /// (streamed or not) so it survives pauses/edits; null on user messages.</summary>
    [Reactive] public string Model { get; set; }

    /// <summary>Reasoning effort that (re)generated this message ("low"/"medium"/"high", ...).
    /// Stamped alongside <see cref="Model"/> when a completion starts; null on user messages,
    /// on effort-less models, and on legacy rows. Powers the working-effort default for
    /// reloads, exactly like Model does.</summary>
    [Reactive] public string Effort { get; set; }

    /// <summary>Raw reasoning/thinking text the model emitted before its answer
    /// ("reasoning_content" on the wire). Aggregated from stream deltas alongside
    /// <see cref="Content"/>; null/empty on user messages and on models that never think.</summary>
    [Reactive] public string ReasoningContent { get; set; }

    /// <summary>Raw OpenRouter `reasoning` value (string or structured) - protocol replay state,
    /// independent of the display text. Null when absent.</summary>
    public JToken ReasoningRaw { get; set; }

    /// <summary>Raw ordered `reasoning_details` array (may be signed/encrypted); kept
    /// byte/sequence faithful for replay. Null when absent.</summary>
    public JArray ReasoningDetails { get; set; }

    /// <summary>Markdig-rendered HTML of <see cref="ReasoningContent"/> - what the renderer
    /// expands to when the user opens the thinking block. Empty string when there's nothing
    /// to show.</summary>
    [Reactive] public string ReasoningHTMLContent { get; set; }

    /// <summary>One-liner collapsed label for the reasoning block: the last non-empty line
    /// of <see cref="ReasoningContent"/> (the model's most recent thought). Null when empty.</summary>
    [Reactive] public string ReasoningPreview { get; private set; }

    public DateTime CreatedTs { get; set; }
    public ConversationVm Conversation { get; }
    public ChatMessageVm Previous { get; set; } // Previous item        
    [Reactive] public ChatMessageVm Next { get; set; } // Next item
    [Reactive] public bool IsCompleting { get; private set; } // Whether completion is in progress

    // --- Phase 1 agentic protocol surface ---

    /// <summary>Exact ordered tool calls of this assistant message (wire shape). Never
    /// reconstructed from tool cards; kept with the message.</summary>
    [Reactive] public ChatMessageToolCall[] ToolCalls { get; set; }

    /// <summary>Wire tool_call_id on a role:"tool" result message.</summary>
    [Reactive] public string ToolCallId { get; set; }

    /// <summary>Tool name on a role:"tool" result message.</summary>
    [Reactive] public string ToolName { get; set; }

    /// <summary>Canonical structured tool result (audit/UI); Content stays the exact bounded model-visible string.</summary>
    public JToken ToolResultJson { get; set; }

    /// <summary>Which provider produced this message; null on legacy rows (heuristic then).</summary>
    [Reactive] public string ProviderKey { get; set; }

    /// <summary>Provenance, not display: human | model | tool | goal | job | workspace_context | summary | subagent.</summary>
    [Reactive] public string Origin { get; set; }

    /// <summary>pending | streaming | completed | interrupted | failed. Null on legacy rows.</summary>
    [Reactive] public string CompletionState { get; set; }

    /// <summary>Provider finish_reason ("stop", "length", "tool_calls", ...).</summary>
    [Reactive] public string FinishReason { get; set; }

    /// <summary>True for intermediate model steps of an agentic turn - selectable for
    /// inspection but NOT editable/regenerable by the existing commands (DSH proposal §7.7).</summary>
    public bool IsIntermediate { get; set; }

    public ReactiveCommand<Unit, (string Content, string Reasoning)> CompleteCmd { get; }
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
                            .Do(_ =>
                            {
                                Content = null; // Just because there can be such a big delay when regenerating
                                ReasoningContent = null; // ... and the old reasoning is stale the moment we re-run
                                ReasoningHTMLContent = null;
                                Model = Conversation.SelectedModel; // Remember which model produced (this version of) this message
                                Effort = Conversation.SelectedEffort; // ... and which effort level was in play
                            })
                            .Select(_ => Conversation.Settings.Streaming ?
                                        CreateGenerationStream()
                                            .TakeUntil(StopCompletionCmd)
                                            .Scan((Content: "", Reasoning: ""),
                                                  (acc, d) => (acc.Content + d.Content, acc.Reasoning + d.Reasoning)) :
                                        Observable.FromAsync(() => GenerateResponse())
                                                  .TakeUntil(StopCompletionCmd))
                            .Switch()
                            .Catch((Exception ex) => Observable.Throw<(string, string)>(new CompletionException(ex))));

        CompleteCmd.ObserveOnMainThread()
                   .Do(c =>
                   {
                       Content = c.Content;
                       ReasoningContent = c.Reasoning;
                   })
                   .SubscribeSafe();

        StopCompletionCmd = ReactiveCommand.Create(() => { }, CompleteCmd.IsExecuting);

        CompleteCmd.IsExecuting
                   .ObserveOnMainThread()
                   .Do(i => IsCompleting = i)
                   .SubscribeSafe();

        const string stopMd = " *[Answer Cut Short by User]*";
        const string caretMd = "'%caret%'";
        const string caretHtml = "<span id=\"caret\"/>";

        // Render on a fixed cadence while tokens are streaming instead of waiting for
        // the stream to go quiet. Rx Throttle is a TRAILING debounce: during a
        // continuous stream (new delta every few ms) it never fires, so the whole
        // reply only appeared in one burst at the very end. Sample(~33ms) paints the
        // latest text at ~30fps - the tokens arrive at the same speed, the UI just
        // shows them live.
        this.WhenAnyValue(vm => vm.Content)
            .Sample(TimeSpan.FromMilliseconds(33))
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

        // Reasoning renders into its own block (the renderer collapses it behind a one-liner);
        // the preview is derived on the same cadence so both stay in step while streaming.
        this.WhenAnyValue(vm => vm.ReasoningContent)
            .Sample(TimeSpan.FromMilliseconds(33))
            .ObserveOnMainThread()
            .Do(r =>
            {
                ReasoningPreview = GetLastLine(r);

                ReasoningHTMLContent = string.IsNullOrEmpty(r)
                    ? ""
                    : $"<div class=\"reasoning-body\">{Markdown.ToHtml(r + (Next == null ? caretMd : ""), _mdPipeline).Replace(caretMd, caretHtml)}</div>";
            })
            .LogAndRetry(this)
            .SubscribeSafe();

        // Drop the streaming caret from both blocks a moment after the text settles.
        this.WhenAnyValue(vm => vm.Content)
            .Throttle(TimeSpan.FromMilliseconds(2000))
            .ObserveOnMainThread()
            .Do(_ =>
            {
                HTMLContent = HTMLContent?.Replace(caretHtml, "");
                ReasoningHTMLContent = ReasoningHTMLContent?.Replace(caretHtml, "");
            })
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
                   .Do(_ =>
                   {
                       HTMLContent = HTMLContent.Replace(caretHtml, "");
                       ReasoningHTMLContent = ReasoningHTMLContent?.Replace(caretHtml, "");
                   })
                   .SubscribeSafe();
    }

    private static string ToUserHtml(string content) =>
        HttpUtility.HtmlEncode(content)
                   .Replace("\r", "<br />");

    /// <summary>Last non-empty line of the reasoning text, trimmed - the collapsed one-liner.
    /// Null when there's nothing to show yet.</summary>
    private static string GetLastLine(string reasoning) =>
        string.IsNullOrWhiteSpace(reasoning)
            ? null
            : reasoning.Split('\n')
                       .LastOrDefault(l => !string.IsNullOrWhiteSpace(l))
                       ?.Trim();

    private async Task<(string Content, string Reasoning)> GenerateResponse()
    {
        if (Debugging.Enabled && Debugging.MockMessages)
        {
            await Task.Delay(500);

            if (Debugging.NumberedMessages)
                return ($"Debug system {Debugging.SystemMessageCounter++}",
                        $"I am the {Debugging.SystemMessageCounter}th mock reply. Let me reason about how to answer...");

            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Dbg/Test2.md"));
            var contents = await FileIO.ReadTextAsync(file);
            return (contents,
                    "I should think about how to answer this helpful but cynical question.\nLet me check the docs first, that usually helps.");
        }

        var req = CreateRequest();
        var completions = await Conversation.Api.CreateChatCompletions(req);
        var choice = completions.Choices.LastOrDefault();
        return (choice?.Message.Content, choice?.Message.ReasoningText);
    }

    private IObservable<(string Content, string Reasoning)> CreateGenerationStream()
    {
        if (Debugging.Enabled && Debugging.MockMessages)
            return Observable
                   .FromAsync(() => GenerateResponse())
                   .SelectMany(r => Observable.Concat(
                       // Mock the "thinking first" ordering real reasoning models use
                       (r.Reasoning == null ? Observable.Empty<(string, string)>() :
                           r.Reasoning.Split(' ')
                            .ToObservable()
                            .Select(s => Observable.Timer(TimeSpan.FromMilliseconds(150))
                                                         .Select(_ => ("", s + " ")))
                            .Concat()),
                       r.Content.Split(' ')
                        .ToObservable()
                        .Select(s => Observable.Timer(TimeSpan.FromMilliseconds(200))
                                             .Select(_ => (s + " ", "")))
                            .Concat()));

        return Conversation.Api.CreateChatCompletionsStream(CreateRequest())
                           .ToObservable()
                           .Select(m =>
                           {
                               // Real streams carry delta chunks; some fakes/tests return a full
                               // message-shaped result instead, so read whichever is present.
                               var delta = m.Choices.LastOrDefault()?.Delta;
                               if (delta != null)
                                   return (delta.Content ?? "", delta.ReasoningText ?? "");

                               var msg = m.Choices.LastOrDefault()?.Message;
                               return (msg?.Content ?? "", msg?.ReasoningText ?? "");
                           });
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

        var modelId = Conversation.SelectedModel;

        // This is the spice, hard coded, because not including this could lead to trouble such as various md syntax
        // bugs and the AI mistakenly thinking it's on the OpenAI's chat bot. I left room to answer whatever it wants
        // or is instructed to previously, but still make it aware that it's inside this app nonetheless.
        // NOTE: this used to be skipped for "reasoning" models because the o1-era ones rejected system
        // messages. That restriction is long gone (o3/o4/gpt-5 and OpenRouter reasoning models all accept
        // a system role fine), so the premise is always sent now.
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
            Model = Conversation.SelectedModel,
            // Only ever send reasoning_effort to models that support it; the conversation's
            // working effort is null for effortless models anyway, but the capability check
            // here is the belt-and-braces that guarantees we never send it to those.
            ReasoningEffort = Conversation.Models?
                                        .FirstOrDefault(m => m.ModelID == modelId)?
                                        .SupportedEfforts is { Length: > 0 }
                ? Conversation.SelectedEffort
                : null
        };

        return req;
    }

    /// <summary>
    /// The exact protocol message for this node - content, tool calls and reasoning preserved
    /// together (DSH proposal §5.3). This is what the fork projection feeds every request
    /// history; NEVER reconstruct a tool-calling assistant from display state.
    /// </summary>
    private ChatMessage CreateMessageRequest() =>
        new()
        {
            Role = Role,
            Content = Content,
            ReasoningContent = ReasoningContent,
            ReasoningRaw = ReasoningRaw?.DeepClone() as JToken,
            ReasoningDetails = ReasoningDetails?.DeepClone() as JArray,
            ToolCalls = ToolCalls?.Select(tc => new ChatMessageToolCall(tc)).ToArray(),
            ToolCallId = ToolCallId
        };
}

public class CompletionException : Exception
{
    public CompletionException(Exception innerEx)
        : base("There was en error while generating the response. " +
               $"You may try again by clicking the Regenerate button.\r\n\r\nError message:\r\n{innerEx.Message}",
               innerEx) { }
}
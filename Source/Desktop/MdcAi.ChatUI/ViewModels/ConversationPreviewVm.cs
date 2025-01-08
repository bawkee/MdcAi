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

using Microsoft.EntityFrameworkCore;
using OpenAiApi;

public class ConversationPreviewVm : ActivatableViewModel, IConversationPreviewItem
{
    [Reactive] public string Id { get; set; }
    [Reactive] public DateTime CreatedTs { get; init; }
    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsNewPlaceholder { get; set; }
    [Reactive] public bool IsTrash { get; set; }
    [Reactive] public ConversationCategoryPreviewVm Category { get; set; }
    [Reactive] public object FullItem { get; private set; }

    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }
    public ReactiveCommand<Unit, Unit> RenameCmd { get; }

    public ConversationPreviewVm()
    {
        Activator.Activated
                 .Take(1)
                 .Where(_ => FullItem == null && IsNewPlaceholder)
                 // Create a new conversation
                 .Select(_ =>
                 {
                     var convo = AppServices.Container.Resolve<ConversationVm>();
                     convo.Conversations = Category.Conversations;
                     return FullItem = convo;
                 })
                 .Cast<ConversationVm>()
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
                 .SubscribeSafe();

        var newConvoCreated = this.WhenAnyValue(vm => vm.IsNewPlaceholder)
                                  .Skip(1)
                                  .Where(i => !i);

        // Create some kind of name for the new item (that's not new anymore)
        newConvoCreated
            .ObserveOnMainThread()
            // Generic name
            .Do(_ => Name = $"Chat {Category.Items.Count}")
            // Auto suggest name
            .Where(_ => !Debugging.Enabled || Debugging.AutoSuggestNames)
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                var convo = (ConversationVm)FullItem;
                var result = await convo.Api.CreateChatCompletions(new()
                {
                    Messages = new List<ChatMessage>(
                        new[]
                        {                            
                            new ChatMessage(
                                ChatMessageRole.System,
                                // This prompt doesn't really give the expected results with 3.5
                                "Create a witty summary of the content with a maximum of 20 characters. Do not use " +
                                "punctuation or line breaks. The names should be complete words or phrases, avoiding " +
                                "any cutoffs. A sprinkle of humor is welcome, as long as it adheres to the character " +
                                "limit. Maximum 20 characters!"
                            ),
                            new ChatMessage(
                                ChatMessageRole.User,
                                $"CONTENT:\r\n\r\n{convo.Head.Message.Content}")
                        }),
                    Model = AiModel.Gpt35Turbo
                });

                var suggestion = result.Choices.Last().Message.Content.CompactWhitespace().Trim('\"');

                return suggestion;
            }))
            .ObserveOnMainThread()
            .Do(name => Name = name)
            .SubscribeSafe();

        // When new item stops being new, insert a new 'new placeholder'
        newConvoCreated
            .ObserveOnMainThread()
            .Do(c => Category.Items.Insert(0, Category.CreateNewItemPlaceholder()))
            .SubscribeSafe();

        // Load conversation from the list
        Activator.Activated
                 .Where(_ => FullItem == null && !IsNewPlaceholder)
                 .Select(_ =>
                 {
                     var convo = AppServices.Container.Resolve<ConversationVm>();
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
                 .Do(convo => FullItem = convo)
                 .SubscribeSafe();

        // Propagate data to/from full item
        this.WhenAnyValue(vm => vm.FullItem)
            .WhereNotNull()
            .Cast<ConversationVm>()
            .Select(c => Observable.Merge(
                        this.WhenAnyValue(vm => vm.Name)
                            .Do(v => c.Name = v)
                            .Select(_ => Unit.Default),
                        c.WhenAnyValue(vm => vm.Name)
                         .Do(v => Name = v)
                         .Select(_ => Unit.Default),
                        c.WhenAnyValue(vm => vm.Id)
                         .Do(v => Id = v)
                         .Select(_ => Unit.Default),
                        this.WhenAnyValue(vm => vm.Category)
                            .Do(v => c.IdCategory = v?.Id)
                            .Select(_ => Unit.Default)
                    ))
            .Switch()
            .SubscribeSafe();

        DeleteCmd = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await using var ctx = AppServices.GetUserProfileDb();
                await ctx.Conversations
                         .Where(c => c.IdConversation == Id)
                         .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, true));
            },
            this.WhenAnyValue(vm => vm.IsNewPlaceholder).Invert());

        DeleteCmd.ObserveOnMainThread()
                 .Do(_ =>
                 {
                     if (Category.Conversations.SelectedPreviewItem == this)
                         Category.Conversations.SelectedPreviewItem = null;
                     IsTrash = true;
                     Category.Conversations.TrashBin.Add(this);
                 })
                 .SubscribeSafe();

        RenameCmd = ReactiveCommand.CreateFromObservable(
            () => Category.Conversations.RenameIntr
                          .Handle(this)
                          .Where(name => name != null && name != Name)
                          .Select(name => Observable
                                          .FromAsync(
                                              async () =>
                                              {
                                                  await using var ctx = AppServices.GetUserProfileDb();
                                                  await ctx.Conversations
                                                           .Where(c => c.IdConversation == Id)
                                                           .ExecuteUpdateAsync(c => c.SetProperty(p => p.Name, name));
                                              })
                                          .ObserveOnMainThread()
                                          .Do(_ => Name = name))
                          .Switch(),
            this.WhenAnyValue(vm => vm.IsNewPlaceholder).Invert());
    }
}
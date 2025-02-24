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

using Mapster;
using LocalDal;
using Microsoft.EntityFrameworkCore;
using OpenAiApi;

public class ChatSettingsVm : ViewModel
{
    [Reactive] public string IdSettings { get; set; }
    [Reactive] public string Model { get; set; }
#if DEBUG
        = AiModel.Gpt35Turbo;
#else
        = AiModel.Gpt4Turbo;
#endif
    [Reactive] public string SelectedModel { get; set; } // Allows user to pick a different model
    [Reactive] public bool IsReasoningModel { get; set; }

    [Reactive] public bool Streaming { get; set; } = true;
    [Reactive] public decimal Temperature { get; set; } = 1m;
    [Reactive] public decimal TopP { get; set; } = 1m;
    [Reactive] public decimal FrequencyPenalty { get; set; } = 1m;
    [Reactive] public decimal PresencePenalty { get; set; } = 1m;
    [Reactive] public string Premise { get; set; } = "You are a helpful AI assistant.";
    [Reactive] public AiModel[] Models { get; private set; }
    [Reactive] public bool IsLoadingModels { get; private set; }

    [Reactive] public bool IsDirty { get; private set; }
    [Reactive] public bool IsDifferentFromOriginal { get; private set; }

    public ReactiveCommand<Unit, AiModel[]> LoadModelsCmd { get; }
    public ReactiveCommand<string, DbChatSettings> LoadCmd { get; }
    public ReactiveCommand<Unit, Unit> ReloadCmd { get; }
    public ReactiveCommand<ChatSettingsSaveOpts, Unit> SaveCmd { get; }

    public ChatSettingsVm(IOpenAiApi api)
    {
        var changes = TrackRelevantChanges();

        SaveCmd = ReactiveCommand.CreateFromObservable(
            (ChatSettingsSaveOpts opts) => Observable.FromAsync(
                async () =>
                {
                    var dbSettings = this.Adapt<DbChatSettings>();
                    await opts.Ctx.ChatSettings.Upsert(dbSettings).RunAsync();
                }),
            this.WhenAnyValue(vm => vm.IsDirty));

        LoadCmd = ReactiveCommand.CreateFromObservable(
            (string id) => Observable.FromAsync(
                                         async () =>
                                         {
                                             await using var ctx = AppServices.GetUserProfileDb();
                                             var dbSettings = await ctx.ChatSettings.FirstAsync(s => s.IdSettings == id);
                                             return dbSettings;
                                         })
                                     .ObserveOnMainThread()
                                     .Do(db => db.Adapt(this)));

        Observable.Merge(SaveCmd, LoadCmd.Select(_ => Unit.Default))
                  .ObserveOnMainThread()
                  .Do(_ =>
                  {
                      SelectedModel = Model;
                      changes.Clean();
                  })
                  .SubscribeSafe();
        
        this.WhenAnyValue(vm => vm.SelectedModel)
            .WhereNotNull()
            .Select(m => new AiModel(m).IsReasoning)
            .ObserveOnMainThread()
            .Do(v => IsReasoningModel = v)
            .SubscribeSafe();

        LoadCmd.ObserveOnMainThread()
               .Select(_ => TrackRelevantChanges())
               .SeriallyDispose()
               .SelectMany(c => c.Select(_ => c.IsDirty())
                                 .StartWith(false)
                                 .Do(i => IsDifferentFromOriginal = i))
               .SubscribeSafe();

        ReloadCmd = ReactiveCommand.CreateFromObservable(
            () => LoadCmd.Execute(IdSettings).Select(_ => Unit.Default),
            this.WhenAnyValue(vm => vm.IdSettings).Select(v => v != null));

        changes.Select(_ => changes.IsDirty())
               .Do(i => IsDirty = i)
               .SubscribeSafe();

        LoadModelsCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Debugging.Enabled && Debugging.MockModels)
                return MockModels;
            return await api.GetModels();
        });

        LoadModelsCmd.ObserveOnMainThread()
                     .Do(models => Models = models.Where(m => m.IsConversational || m.IsReasoning).ToArray())
                     .SubscribeSafe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .SubscribeSafe();
    }

    private ViewModelChangeTracker TrackRelevantChanges() =>
        TrackChanges(nameof(Streaming),
                     nameof(Temperature),
                     nameof(TopP),
                     nameof(FrequencyPenalty),
                     nameof(PresencePenalty),
                     nameof(Premise),
                     nameof(Model));

    public void CopyTo(ChatSettingsVm c)
    {
        c.TopP = TopP;
        c.FrequencyPenalty = FrequencyPenalty;
        c.PresencePenalty = PresencePenalty;
        c.Streaming = Streaming;
        c.Temperature = Temperature;
        c.Model = Model;
        c.Premise = Premise;
    }

    public static AiModel[] MockModels =
    {
        new("gpt-3.5-turbo-16k-0613"),
        new("gpt-3.5-turbo-16k"),
        new("gpt-4-1106-preview"),
        new("gpt-3.5-turbo"),
        new("gpt-3.5-turbo-1106"),
        new("gpt-4-vision-preview"),
        new("gpt-4"),
        new("gpt-3.5-turbo-instruct-0914"),
        new("gpt-3.5-turbo-0613"),
        new("gpt-3.5-turbo-0301"),
        new("gpt-3.5-turbo-instruct"),
        new("gpt-4-0613"),
        new("gpt-4-0314")
    };
}

public class ChatSettingsSaveOpts
{
    public UserProfileDbContext Ctx { get; set; }
}
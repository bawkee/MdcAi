namespace MdcAi.ChatUI.ViewModels;

using Mapster;
using MdcAi.ChatUI.LocalDal;
using Microsoft.EntityFrameworkCore;
using OpenAiApi;

public class ChatSettingsVm : ViewModel
{
    [Reactive] public string IdSettings { get; set; }
    [Reactive] public string Model { get; set; }
#if DEBUG
        = AiModel.GPT35Turbo;
#else
        = AiModel.GPT4Turbo;
#endif
    [Reactive] public string SelectedModel { get; set; } // Allows user to pick a different model

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
    public ReactiveCommand<Unit, Unit> SaveCmd { get; }

    public ChatSettingsVm(IOpenAiApi api)
    {
        var changes = TrackRelevantChanges();

        SaveCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.FromAsync(
                async () =>
                {
                    await using var ctx = AppServices.GetUserProfileDb();
                    var dbSettings = this.Adapt<DbChatSettings>();
                    await ctx.ChatSettings.Upsert(dbSettings).RunAsync();
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

        LoadCmd.ObserveOnMainThread()
               .Select(_ => TrackRelevantChanges())
               .SeriallyDispose()
               .SelectMany(c => c.Select(_ => c.IsDirty())
                                 .StartWith(false)
                                 .Do(i => IsDifferentFromOriginal = i))
               .SubscribeSafe();

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
                     .Do(models => Models = models.Where(m => m.ModelID.StartsWith("gpt"))
                                                  .ToArray())
                     .SubscribeSafe();

        LoadModelsCmd.IsExecuting
                     .ObserveOnMainThread()
                     .Do(i => IsLoadingModels = i)
                     .SubscribeSafe();        
    }

    // TODO: The change tracker is quite odd
    private ViewModelChangeTracker TrackRelevantChanges() =>
        TrackChanges(nameof(Streaming),
                     nameof(Temperature),
                     nameof(TopP),
                     nameof(FrequencyPenalty),
                     nameof(PresencePenalty),
                     nameof(Premise),
                     nameof(Model));

    // TODO: Interesting...
    public ChatSettingsVm Clone() => this.Adapt(AppServices.Container.Resolve<ChatSettingsVm>());

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
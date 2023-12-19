namespace MdcAi.ChatUI.ViewModels;

using Mapster;
using MdcAi.ChatUI.LocalDal;
using Microsoft.EntityFrameworkCore;

public class ConversationCategoryVm : ActivatableViewModel
{
    [Reactive] public string Name { get; set; }
    public string IdCategory { get; set; }
    [Reactive] public string IdSettings { get; set; }
    [Reactive] public string Description { get; set; }
    public IconsVm Icons { get; }
    public ChatSettingsVm Settings { get; }

    public ReactiveCommand<Unit, DbCategory> LoadCmd { get; }
    
    public ConversationCategoryVm(IconsVm icons, ChatSettingsVm settings)
    {
        Icons = icons;
        Settings = settings;

        LoadCmd = ReactiveCommand.CreateFromObservable(
            () => Observable.FromAsync(async () =>
                            {
                                await using var ctx = AppServices.GetUserProfileDb();
                                var cat = ctx.Categories.Include(c => c.Settings).First(c => c.IdCategory == IdCategory);
                                return cat;
                            })
                            .ObserveOnMainThread()
                            .Do(cat => cat.Adapt(this)));

        Activator.Activated
                 .Take(1)
                 .InvokeCommand(Icons.LoadIcons);

        Activator.Activated.Take(1).InvokeCommand(LoadCmd);

        LoadCmd.Select(_ => this.WhenAnyValue(vm => vm.IdSettings))
               .Switch()
               .WhereNotNull()
               .Select(id => Settings.LoadCmd.Execute(id))
               .Switch()
               .SubscribeSafe();

        Activator.Activated.Take(1).InvokeCommand(Settings.LoadModelsCmd);

        Settings.LoadCmd
                .Select(_ => Settings.WhenAnyValue(vm => vm.IsDirty))
                .Switch()
                .Throttle(TimeSpan.FromSeconds(1))
                .Select(_ => Unit.Default)
                .InvokeCommand(Settings.SaveCmd);

        this.WhenActivated(disposables =>
        {
            Debug.WriteLine($"Activated {Name}");
            Disposable.Create(() => Debug.WriteLine($"Deactivated {Name}")).DisposeWith(disposables);
        });
    }

    static ConversationCategoryVm()
    {
        TypeAdapterConfig<ConversationCategoryVm, DbCategory>
            .NewConfig()
            .IgnoreMember((member, _) => !member.Type.IsBuiltInConvertibleType());
    }
}
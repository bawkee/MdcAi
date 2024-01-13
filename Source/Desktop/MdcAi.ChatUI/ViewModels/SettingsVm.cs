namespace MdcAi.ChatUI.ViewModels;

using Properties;
using RxUIExt.Windsor;
using Windows.Storage;

[Singleton]
public class SettingsVm : ActivatableViewModel
{
    public OpenAiSettingsVm OpenAi { get; set; }
    [Reactive] public bool ShowGettingStartedConvoTip { get; set; }
    public ReactiveCommand<Unit, Unit> ShowPrivacyStatementCmd { get; set; }
    public ReactiveCommand<Unit, Unit> ShowAboutCmd { get; set; }
    public ReactiveCommand<Unit, Unit> OpenAppStorageCmd { get; set; }

    public SettingsVm(OpenAiSettingsVm openAi)
    {
        OpenAi = openAi;

        GlobalChatSettings.Default.WhenAnyValue(s => s.ShowGettingStartedConvoTip)
                          .ObserveOnMainThread()
                          .Do(v => ShowGettingStartedConvoTip = v)
                          .SubscribeSafe();

        this.WhenAnyValue(vm => vm.ShowGettingStartedConvoTip)
            .Skip(1)
            .Do(v =>
            {
                GlobalChatSettings.Default.ShowGettingStartedConvoTip = v;
                GlobalChatSettings.Default.Save();
            })
            .SubscribeSafe();

        OpenAppStorageCmd = ReactiveCommand.Create(() =>
        {
            ShellUtil.StartUrl(ApplicationData.Current.LocalFolder.Path);
        });
    }
}
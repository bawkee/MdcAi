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

public class OpenAiSettingsVm : ActivatableViewModel
{
    [Reactive] public string ApiKeys { get; set; }
    [Reactive] public string CurrentApiKey { get; private set; }
    [Reactive] public string OrganisationName { get; set; }

    public OpenAiSettingsVm()
    {
        ApiKeys = AppCredsManager.GetValue("ApiKeys");
        OrganisationName = AppCredsManager.GetValue("OrganisationName");

        this.WhenAnyValue(vm => vm.ApiKeys)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => AppCredsManager.SetValue("ApiKeys", v))
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.OrganisationName)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => AppCredsManager.SetValue("OrganisationName", v))
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.ApiKeys)
            .Do(keys =>
            {
                if (string.IsNullOrEmpty(keys))
                    CurrentApiKey = null;
                else
                    CurrentApiKey = keys.Split("\r\n")
                                        .FirstOrDefault();
            })
            .SubscribeSafe();
    }
}
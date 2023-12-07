using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace MdcAi.ChatUI.ViewModels;

[Singleton]
public class SettingsVm : ActivatableViewModel
{
    public OpenAiSettingsVm OpenAi { get; set; }

    public SettingsVm(OpenAiSettingsVm openAi) { OpenAi = openAi; }
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
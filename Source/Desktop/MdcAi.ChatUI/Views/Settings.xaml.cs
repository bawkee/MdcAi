// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views;

using ViewModels;
using RxUIExt.Windsor;
using Microsoft.UI.Xaml.Documents;

public sealed partial class Settings
{
    public Settings() { InitializeComponent(); }

    private void ShowPrivacyHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (ViewModel.ShowPrivacyStatementCmd is { } cmd)
            Observable.Return(Unit.Default).InvokeCommand(cmd);
    }

    private void AboutHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (ViewModel.ShowAboutCmd is { } cmd)
            Observable.Return(Unit.Default).InvokeCommand(cmd);
    }
}

[DoNotRegister]
public class SettingsBase : ReactiveUserControl<SettingsVm> { }
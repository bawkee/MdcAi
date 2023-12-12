// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views;

using ViewModels;
using RxUIExt.Windsor;

public sealed partial class Settings 
{
    public Settings()
    {
        InitializeComponent();
    }
}

[DoNotRegister]
public class SettingsBase : ReactiveUserControl<SettingsVm> { }
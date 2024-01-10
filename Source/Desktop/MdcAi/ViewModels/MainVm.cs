namespace MdcAi.ViewModels;

using MdcAi.ChatUI.ViewModels;

// MPV:
// TODO: About window
// TODO: Disclaimer
// TODO: Better welcoming experience (i.e. app just started, no API key)
// TODO: That 'root page' thing looks pointless
// TODO: Test light theme
// TODO: Provide a way to open up app folder and see the log files
// TODO: When making a new category auto select it

// Other
// TODO: Localisation

[Singleton]
public class MainVm : ActivatableViewModel
{
    public ConversationsVm Conversations { get; }
    public SettingsVm Settings { get; }

    [Reactive] public string Foo { get; set; }

    public MainVm(ConversationsVm conversations, SettingsVm settings)
    {
        Conversations = conversations;
        Settings = settings;

        Activator.Activated
                 .InvokeCommand(Conversations.LoadItems);
    }
}
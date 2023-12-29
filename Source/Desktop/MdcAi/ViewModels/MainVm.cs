namespace MdcAi.ViewModels;

using MdcAi.ChatUI.LocalDal;
using MdcAi.ChatUI.ViewModels;

// MPV:
// TODO: Suggestions and tips in new chat

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
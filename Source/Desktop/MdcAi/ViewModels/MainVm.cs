namespace MdcAi.ViewModels;

using MdcAi.ChatUI.ViewModels;

public class MainVm : ActivatableViewModel
{
    public ConversationVm Conversation { get; }
    public SettingsVm Settings { get; }

    public MainVm(ConversationVm conversation, SettingsVm settings)
    {
        Conversation = conversation;
        Settings = settings;
    }
}
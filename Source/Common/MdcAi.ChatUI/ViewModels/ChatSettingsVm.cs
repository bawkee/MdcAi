namespace MdcAi.ChatUI.ViewModels;

using Mdc.OpenAiApi;

public class ChatSettingsVm : ViewModel
{
    [Reactive, Map] public string Model { get; set; }
#if DEBUG
        = AiModel.GPT35Turbo;
#else
        = AiModel.GPT4Turbo;
#endif

    [Reactive, Map] public bool Streaming { get; set; } = true;

    [Reactive, Map]
    public string Premise { get; set; } =
        "You are a helpful but cynical and humorous assistant, a joker of sorts. You give short and hesitant answers. Use md syntax.";

    public ChatSettingsVm(ChatSettingsVm copyFrom = null) { copyFrom?.MapTo(this); }
}
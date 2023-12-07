// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using ViewModels;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using ReactiveMarbles.ObservableEvents;

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainVm ViewModel { get; }

        public MainWindow()
        {            
            ViewModel = Services.GetRequired<MainVm>();

            InitializeComponent();

            ExtendsContentIntoTitleBar = true;

            this.Events()
                .Activated
                .Take(1)
                .Do(_ => ViewModel.Activator.Activate())
                .SubscribeSafe();
        }        

        //private async void btn1_Click(object sender, RoutedEventArgs e)
        //{
        //    var api = new OpenAIApi("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");

        //    var models = await api.GetModels();

        //    //return;

        //    var res = await api.CreateChatCompletions(new()
        //    {
        //        Model = AiModel.GPT35Turbo,
        //        Messages = new List<ChatMessage>
        //        {
        //            new(ChatMessageRole.System, "You are a helpful comedian assistant."),
        //            new(ChatMessageRole.User, "What's up dawg?")
        //        }
        //    });

        //    Debug.WriteLine($"Response: {res.Choices.Last().Message.Content}");
        //}

        //private void btn2_Click(object sender, RoutedEventArgs e)
        //{
        //    var api = new OpenAIApi("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");

        //    var res = api.CreateChatCompletionsStream(new()
        //                 {
        //                     Model = AiModel.GPT35Turbo,
        //                     Messages = new List<ChatMessage>
        //                     {
        //                         new(ChatMessageRole.System, "You are a helpful comedian assistant."),
        //                         new(ChatMessageRole.User, "What's up dawg?")
        //                     }
        //                 })
        //                 .ToObservable();

        //    res.SubscribeSafe(x => { Debug.Write(x.Choices.Last().Delta.Content); }, () => Debug.WriteLine("\r\nCompleted"));

        //    Debug.WriteLine("Response:");

        //    //Debug.WriteLine($"Response: {res.Choices.Last().Message.Content}");
        //}

    }
}
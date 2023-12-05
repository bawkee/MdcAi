// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using ViewModels;
using Newtonsoft.Json;
using Mdc.OpenAiApi;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using Windows.Storage.Streams;
using Windows.Storage;
using System.Drawing.Text;
using System.Windows.Input;
using Windows.System;
using DynamicData;
using DynamicData.Kernel;
using ReactiveMarbles.ObservableEvents;
using System.Reactive.Disposables;

public sealed partial class Conversation : ILogging
{
    public Conversation()
    {
        InitializeComponent();

        var initCoreInt = Observable.FromAsync(async () =>
                                 {
                                     await ChatWebView.EnsureCoreWebView2Async();
                                     return ChatWebView.CoreWebView2;
                                 })
                                 .ObserveOnMainThread()                                 
                                 .Publish();

        initCoreInt.Connect();

        var initCore = initCoreInt.Replay();

        initCore.Connect();

        // Feed the WebView2 all the resources it needs.
        initCore.Select(core => core.Events()
                                    .WebResourceRequested
                                    .Select(e => new { Sender = core, Args = e.args }))
                .Switch()
                .SelectMany(async e =>
                {
                    await ProcessWebResource(e.Sender, e.Args);
                    return Unit.Default;
                })
                .LogErrors(this)
                .Subscribe();

        initCore.Do(core =>
                {
                    core.Settings.IsWebMessageEnabled = true;
                    core.AddWebResourceRequestedFilter("http://localhost:3431/*", CoreWebView2WebResourceContext.All);

                    if (Debugging.Enabled && Debugging.NpmRenderer)
                        ChatWebView.Source = new(@"http://localhost:3000/");
                    else
                        // Ideally localhost because otherwise you get security restrictions and need special flags to 
                        // circumvent it.
                        ChatWebView.Source = new(@"http://localhost:3431/index.html");
                })
                .LogErrors(this)
                .Subscribe();

        var messages = initCore
                       .Select(core => core.Events()
                                           .WebMessageReceived)
                       .Switch()
                       .Select(e =>
                       {
                           var req = JsonConvert.DeserializeObject<WebViewRequestDto>(e.args.WebMessageAsJson);
                           return req;
                       })
                       .Publish()
                       .RefCount();

        var webReady = messages.Where(r => r.Name == "Ready")
                               .Select(_ => Unit.Default)
                               .Replay();

        var isScrolledDown = true;

        messages.Where(m => m.Name == "IsScrollToBottom")
                .ObserveOnMainThread()
                .Do(m => isScrolledDown = (bool)m.Data)
                .Subscribe();

        Subject<Unit> scrollToBottom = new();

        scrollToBottom.Where(_ => isScrolledDown)
                      // We delay because of animations and other UI gimmicks so as not to prematurely scroll
                      .Throttle(TimeSpan.FromMilliseconds(500))
                      .ObserveOnMainThread()
                      .SelectMany(_ => Observable.FromAsync(async () => await ChatWebView.ScrollToBottom()))
                      .Subscribe();

        // Since this field is on autosize it will jump up and down and this will in turn cause webview to scroll up (for reasons unknown)
        // so to work around that we try to pick up these cues and schedule scrolling back down.
        PromptField.Events()
                   .BeforeTextChanging
                   .Where(e => e.args.NewText.Contains("\r") || e.args.NewText.Contains("\n"))
                   .Do(_ => scrollToBottom.OnNext(Unit.Default))
                   .Subscribe();

        webReady.Connect();

        initCore.Connect();

        this.WhenActivated(disposables =>
        {
            Debug.WriteLine($"Activated view {GetType()} - {GetHashCode()}");
            Disposable.Create(() => Debug.WriteLine($"Deactivated view {GetType()} - {GetHashCode()}")).DisposeWith(disposables);
        });

        this.WhenActivated((disposables, viewModel) =>
        {
            messages.Where(r => r.Name == "SetSelection")
                    .Do(r => viewModel.SelectedMessage = viewModel.Messages[Convert.ToInt32(r.Data)].Selector)
                    .LogErrors(this)
                    .Subscribe()
                    .DisposeWith(disposables);

            webReady.Select(_ => viewModel.WhenAnyValue(vm => vm.LastMessagesRequest)                                          
                                          .WhereNotNull())
                    .Switch()
                    .Do(r => ChatWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(r)))
                    .LogErrors(this)
                    .Subscribe()
                    .DisposeWith(disposables);

            webReady.Select(_ => viewModel.WhenAnyValue(vm => vm.SelectedMessage))
                    .Switch()
                    .Select(msg => msg?.Message == null ? -1 : viewModel.Messages.IndexOf(msg.Message))
                    .Do(i => SetSelectedMessage(i))
                    .Subscribe()
                    .DisposeWith(disposables);

            viewModel.WhenAnyValue(vm => vm.Models)
                     .Skip(1)
                     .WhereNotNull()
                     .ObserveOnMainThread()
                     .Do(models =>
                     {
                         modelsMenu.Items.Clear();
                         modelsMenu.Items.AddRange(models.Select(m => new MenuFlyoutItem()
                         {
                             Text = m.ModelID,
                             Command = viewModel.SelectModelCmd,
                             CommandParameter = (string)m
                         }));

                         modelsMenu.Items.Add(new MenuFlyoutSeparator());
                         modelsMenu.Items.Add(new MenuFlyoutItem()
                         {
                             Text = "Which one to pick? 😕",
                             Command = ReactiveCommand.CreateFromTask(async () =>
                             {
                                 var prompt = new ContentDialog
                                 {
                                     Content = "Dunno 🤷‍♂️",
                                     XamlRoot = XamlRoot,
                                     Title = "Explanation",
                                     CloseButtonText = "OK",
                                 };
                                 await prompt.ShowAsync();
                             })
                         });
                     })
                     .Subscribe()
                     .DisposeWith(disposables);

            PromptField.Events()
                       .PreviewKeyDown
                       .Select(e =>
                       {
                           if (e.Key == VirtualKey.Enter)
                           {
                               var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                               if (!isShift)
                               {
                                   e.Handled = true;
                                   if (!string.IsNullOrEmpty(viewModel.Prompt.Contents))
                                       return viewModel.SendPromptCmd.Execute();
                               }
                           }

                           return Observable.Return(Unit.Default);
                       })
                       .Switch()
                       .Subscribe()
                       .DisposeWith(disposables);

            viewModel.EditSelectedCmd
                     .Do(_ =>
                     {
                         PromptField.Focus(FocusState.Keyboard);
                         PromptField.SelectionStart = PromptField.Text.Length;
                     })
                     .Subscribe()
                     .DisposeWith(disposables);

            PromptField.Events()
                       .KeyDown
                       .Where(e => e.Key == VirtualKey.Escape)
                       .Select(_ => Unit.Default)
                       .InvokeCommand(viewModel.CancelEditCmd);
        });

        return;

        void SetSelectedMessage(int index) => ChatWebView.CoreWebView2.PostWebMessageAsJson(
            JsonConvert.SerializeObject(new WebViewRequestDto
            {
                Name = "SetSelection",
                Data = index
            }));
    }

    private static async Task ProcessWebResource(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var sourceUri = new Uri(e.Request.Uri);

        if (sourceUri.Host != "localhost")
            return;

        // This is the sort of fuckery you need when doing things procedurally, luckily WebView2 cares, so we got off easy here. The sneaky
        // Using statement will take care of all return paths so no worries.
        using var deferral = e.GetDeferral();

        var zipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/chatlist.zip"));
        await using var zipStream = await zipFile.OpenStreamForReadAsync();

        var path = sourceUri.AbsolutePath[1..];

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (archive.GetEntry(path) is { } entry)
        {
            var contentType = WebViewExtensions.MimeTypes[Path.GetExtension(path)];
            await using var entryStream = entry.Open();
            using var entryStreamRam = await UnloadStreamAsRandomAccess(entryStream);
            e.Response = core.Environment.CreateWebResourceResponse(entryStreamRam, 200, "OK", $"Content-Type: {contentType}");
        }

        deferral.Complete();
    }

    public class JsError
    {
        public string Message { get; set; }
        public string Stack { get; set; }
        public string Name { get; set; }
    }

    // Tomfoolery that we sadly need because zip files don't exactly support random access. Luckily for us, webview2 will
    // cache these things so we don't care really.
    public static async Task<IRandomAccessStream> UnloadStreamAsRandomAccess(Stream input)
    {
        var memoryStream = new MemoryStream();
        await input.CopyToAsync(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin); // Reset the memory stream position to the beginning.

        // Create ram we are going to return.
        var randomAccessStream = new InMemoryRandomAccessStream();

        // Use a thing to write another thing onto the third thing and flush the thing.
        var outputStream = randomAccessStream.GetOutputStreamAt(0);
        var writer = new DataWriter(outputStream);
        writer.WriteBytes(memoryStream.ToArray());
        await writer.StoreAsync();
        await outputStream.FlushAsync();

        // Detach the outputStream to avoid closing it when the writer is disposed.
        writer.DetachStream();
        writer.Dispose();

        // Set the position to the beginning, since the consumer of the RandomAccessStream might expect it.
        randomAccessStream.Seek(0);

        return randomAccessStream;
    }
}

[DoNotRegister]
public class ConversationBase : ReactiveUserControl<ConversationVm> { }
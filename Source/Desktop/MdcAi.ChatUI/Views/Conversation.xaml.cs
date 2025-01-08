#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.ChatUI.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using Microsoft.Web.WebView2.Core;
using ViewModels;
using Newtonsoft.Json;
using System.IO.Compression;
using Windows.Storage.Streams;
using Windows.Storage;
using System.Windows.Input;
using Windows.System;
using DynamicData;
using ReactiveMarbles.ObservableEvents;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using RxUIExt.Windsor;
using Microsoft.UI.Xaml.Data;
using OpenAiApi;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;

public sealed partial class Conversation : ILogging
{
    public Conversation()
    {
        InitializeComponent();

        var initCoreConn = Observable.FromAsync(async () =>
                                     {
                                         await ChatWebView.EnsureCoreWebView2Async();                                         
                                         // This can still return null, for whatever reason, means that WebView2 didn't work
                                         return ChatWebView.CoreWebView2;
                                     })
                                     .ObserveOnMainThread()
                                     .Publish();

        initCoreConn.Connect();

        var initCore = initCoreConn.Replay();

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
                .SubscribeSafe();

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
                .SubscribeSafe();

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
                .SubscribeSafe();

        Subject<Unit> scrollToBottom = new();

        scrollToBottom.Where(_ => isScrolledDown)
                      // We delay because of animations and other UI gimmicks so as not to prematurely scroll
                      .Throttle(TimeSpan.FromMilliseconds(500))
                      .ObserveOnMainThread()
                      .SelectMany(_ => Observable.FromAsync(async () => await ChatWebView.ScrollToBottom()))
                      .SubscribeSafe();

        // Since this field is on autosize it will jump up and down and this will in turn cause webview to scroll up (for reasons unknown)
        // so to work around that we try to pick up these cues and schedule scrolling back down.
        PromptField.Events()
                   .BeforeTextChanging
                   .Where(e => e.args.NewText.Contains("\r") || e.args.NewText.Contains("\n"))
                   .Do(_ => scrollToBottom.OnNext(Unit.Default))
                   .SubscribeSafe();

        webReady.Connect();

        initCore.Connect();

        //this.WhenActivated(disposables =>
        //{
        //    Debug.WriteLine($"Activated view {GetType()} - {GetHashCode()}");
        //    Disposable.Create(() => Debug.WriteLine($"Deactivated view {GetType()} - {GetHashCode()}")).DisposeWith(disposables);
        //});        

        this.WhenActivated((disposables, viewModel) =>
        {
            viewModel.EditSettingsCmd = ReactiveCommand.CreateFromTask(
                async () => await SettingsDialog.ShowAsync() == ContentDialogResult.Primary);

            messages.Where(r => r.Name == "SetSelection")
                    .Do(r => viewModel.SelectedMessage = viewModel.Messages[Convert.ToInt32(r.Data)].Selector)
                    .SubscribeSafe()
                    .DisposeWith(disposables);

            webReady.Select(_ => viewModel.WhenAnyValue(vm => vm.LastMessagesRequest)
                                          .WhereNotNull())
                    .Switch()
                    .Do(r =>
                    {
                        try
                        {
                            ChatWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(r));
                        }
                        catch (COMException cex)
                        {
                            if (cex.Data.Contains("Description"))
                            {
                                // Thanks WinUI for such wonderfully helpful catch-all COM exceptions
                                if (cex.Data["Description"]?.ToString()?.Contains("The object has been closed") ?? false)
                                    return; // We ignore this
                            }

                            throw;
                        }
                    })
                    .SubscribeSafe()
                    .DisposeWith(disposables);

            webReady.Select(_ => viewModel.WhenAnyValue(vm => vm.SelectedMessage))
                    .Switch()
                    .Select(msg => msg?.Message == null ? -1 : viewModel.Messages.IndexOf(msg.Message))
                    .Do(i => SetSelectedMessage(i))
                    .SubscribeSafe()
                    .DisposeWith(disposables);

            viewModel.WhenAnyValue(vm => vm.Models)
                     .WhereNotNull()
                     .ObserveOnMainThread()
                     .Do(models =>
                     {
                         ModelsMenu.Items.Clear();
                         ModelsMenu.Items.AddRange(models.Select(m => new MenuFlyoutItem()
                         {
                             Text = m.ModelID,
                             Command = viewModel.SelectModelCmd,
                             CommandParameter = (string)m
                         }));

                         ModelsMenu.Items.Add(new MenuFlyoutSeparator());
                         ModelsMenu.Items.Add(new MenuFlyoutItem()
                         {
                             Text = "Which one to pick? 😕",
                             Command = ReactiveCommand.CreateFromTask(async () =>
                             {
                                 var prompt = new ContentDialog
                                 {
                                     Content = "gpt-4-1106-preview ➡️\U0001faf0💲💲⚡⚡🚀🚀 (cheap, powerful, fast)\r" +
                                               "gpt-4 ➡️⚡⚡⚡ (powerful, very slow)\r" +
                                               "gpt-3.5-turbo-1106 ➡️\U0001faf0💲💲💲💲⚡🚀🚀🚀🚀 (very cheap, very fast)\r\r" +
                                               "You may experiment with other models but price is the same as above 3 and capabilities " +
                                               "are either same, lower, or don't make a difference in the context of this app (at this moment).\r\r" +
                                               "The first model, GPT4-Turbo, is a clear winner so it's a great general purpose model.\r\r" +
                                               "Use GPT-3 for mundane tasks such as translation, data conversion, log analysis, fast summaries, etc.",
                                     XamlRoot = XamlRoot,
                                     Title = "Explanation of GPT models as of January 2024",
                                     CloseButtonText = "OK",
                                 };
                                 await prompt.ShowAsync();
                             })
                         });
                     })
                     .SubscribeSafe()
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
                       .SubscribeSafe()
                       .DisposeWith(disposables);

            viewModel.EditSelectedCmd
                     .Do(_ =>
                     {
                         PromptField.Focus(FocusState.Keyboard);
                         PromptField.SelectionStart = PromptField.Text.Length;
                     })
                     .SubscribeSafe()
                     .DisposeWith(disposables);

            PromptField.Events()
                       .KeyDown
                       .Where(e => e.Key == VirtualKey.Escape)
                       .Select(_ => Unit.Default)
                       .InvokeCommand(viewModel.CancelEditCmd);

            PromptField.Focus(FocusState.Programmatic);

            viewModel.WhenAnyValue(vm => vm.Models)
                     .WhereNotNull()
                     .Do(models =>
                     {
                         // Setting combobox bindings in xaml will often, if not always, result in it clearing up the value. Only way to prevent it
                         // is to make sure its items are loaded before binding its SelectedValue to anything. Tickets are open for this since 2008.
                         ChatSettingModelDropdown.ClearValue(Selector.SelectedValueProperty);
                         ChatSettingModelDropdown.ItemsSource = models;
                         ChatSettingModelDropdown.SelectedValuePath = nameof(AiModel.ModelID);
                         BindingOperations.SetBinding(
                             ChatSettingModelDropdown,
                             Selector.SelectedValueProperty,
                             new Binding
                             {
                                 Path = new("Settings.Model"),
                                 Mode = BindingMode.TwoWay
                             });
                     })
                     .SubscribeSafe()
                     .DisposeWith(disposables);

            ViewModel.EditSettingsCmd
                     .ObserveOnMainThread()
                     .Do(_ => PromptField.Focus(FocusState.Programmatic))
                     .SubscribeSafe()
                     .DisposeWith(disposables);
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

        var zipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///MdcAi.ChatUI/Assets/ChatListUI.zip"));
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

    private void DontShowGettingStartedTip_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        Observable.Return(Unit.Default).InvokeCommand(ViewModel.TurnOffGettingStartedTipCmd);
    }

    private void TipsNavigationView_OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) { }

    private void TipsNavigationView_OnLoaded(object sender, RoutedEventArgs e)
    {
        TipsNavigationView.SelectedItem = TipsNavigationView.MenuItems.FirstOrDefault();
    }

    private void TipsNavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var navOptions = new FrameNavigationOptions()
        {
            TransitionInfoOverride = args.RecommendedNavigationTransitionInfo,
            IsNavigationStackEnabled = false
        };

        var pageType = ((NavigationViewItem)args.SelectedItem).Tag switch
        {
            nameof(GettingStartedTips.Categories) => typeof(GettingStartedTips.Categories),
            nameof(GettingStartedTips.Conversations) => typeof(GettingStartedTips.Conversations),
            nameof(GettingStartedTips.Editing) => typeof(GettingStartedTips.Editing),
            nameof(GettingStartedTips.Premise) => typeof(GettingStartedTips.Premise),
            nameof(GettingStartedTips.Settings) => typeof(GettingStartedTips.Settings),
            _ => throw new ArgumentOutOfRangeException()
        };

        TipsContentFrame.NavigateToType(pageType, null, navOptions);
    }

    private void ShowPrivacyHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (ViewModel.GlobalSettings.ShowPrivacyStatementCmd is { } cmd)
            Observable.Return(Unit.Default).InvokeCommand(cmd);
    }
}

[DoNotRegister]
public class ConversationBase : ReactiveUserControl<ConversationVm> { }
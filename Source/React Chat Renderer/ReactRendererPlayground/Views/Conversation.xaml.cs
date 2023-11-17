// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ReactRendererPlayground.Views;

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
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using ReactiveUI;
using System.Diagnostics;
using Windows.Storage;
using ReactRendererPlayground.ViewModels;
using Sala.Extensions.WinUI;
using System.Text;
using Newtonsoft.Json;
using Windows.UI.WebUI;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Mdc.OpenAiApi;

public sealed partial class Conversation : ConversationBase
{
    public Conversation()
    {
        InitializeComponent();

        DataContext = ViewModel = new ConversationVm();

        this.WhenActivated(disposables => { });

        // WEBVIEW2_USER_DATA_FOLDER is extremely important apparently
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--remote-debugging-port=9222 --enable-features=OverlayScrollbar,OverlayScrollbarWinStyle,OverlayScrollbarWinStyleAnimation"); 

        webView.CoreWebView2Initialized += WebView_CoreWebView2Initialized;
        //webView.PointerMoved += WebView_PointerMoved;

        Observable.FromAsync(async () =>
                  {
                      await webView.EnsureCoreWebView2Async();
                      // Also doesnt work
                      //webView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;                      
                      return webView.CoreWebView2;
                  })
                  .ObserveOn(RxApp.MainThreadScheduler)
                  .Do(core =>
                  {
                      core.Settings.IsWebMessageEnabled = true;
                      core.WebResourceRequested += CoreWebView2_WebResourceRequested;
                      core.WebMessageReceived += Core_WebMessageReceived;
                      core.AddWebResourceRequestedFilter("http://mdcai/*", CoreWebView2WebResourceContext.All);

                      webView.Source = new Uri(@"http://mdcai/index.html");
                      //webView.Source = new Uri(@"http://localhost:3000");
                      //webView.Source = new Uri(@"https://github.com/wooorm/starry-night#css");
                  })
                  .Subscribe();
    }

    private IDisposable _messagePoster;

    private void WebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        ViewModel.WhenAnyValue(vm => vm.Messages)
                 .WhereNotNull()
                 .Select(m => m.LastOrDefault())
                 .Where(m => m?.Role == ChatMessageRole.System)
                 .Select(m => m.WhenAnyValue(x => x.IsCompleting))
                 .Switch()
                 .DistinctUntilChanged()
                 .ObserveOnMainThread()
                 .Do(i => Debug.WriteLine($"Is completing: {i}"))
                 .Where(i => !i)
                 .Do(_ => webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(new WebViewRequestDto
                 {
                     Name = "HideCaret"
                 })))
                 .Subscribe();
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var req = JsonConvert.DeserializeObject<WebViewRequestDto>(args.WebMessageAsJson);

        switch (req.Name)
        {
            case "SetSelection":
                ViewModel.SelectedMessage = ViewModel.Messages[Convert.ToInt32(req.Data)].Selector;
                break;
            case "Ready":
                _messagePoster?.Dispose();
                _messagePoster = ViewModel.WhenAnyValue(vm => vm.LastWebViewRequest)
                                          .WhereNotNull()
                                          .Do(r => webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(r)))
                                          .Subscribe();
                break;
            case "LogError":
                var ex = ((JObject)req.Data).ToObject<JsError>();
                break;
        }
    }

    private void resendCmd_OnClick(object sender, RoutedEventArgs e)
    {
        webView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(ViewModel.LastWebViewRequest));
    }
   
    private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var sourceUri = new Uri(e.Request.Uri);

        if (sourceUri.Host != "mdcai")
            return;

        using var deferral = e.GetDeferral();

        var zipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/chatlist.zip"));
        await using var zipStream = await zipFile.OpenStreamForReadAsync();

        var path = sourceUri.AbsolutePath[1..];

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (archive.GetEntry(path) is { } entry)
        {
            var contentType = MimeTypes[Path.GetExtension(path)];
            using var entryStream = entry.Open();
            using var entryStreamRam = await UnloadStreamAsRandomAccess(entryStream);
            e.Response = sender.Environment.CreateWebResourceResponse(entryStreamRam, 200, "OK", $"Content-Type: {contentType}");
            Debug.WriteLine($"Loading file: {path}");
        }



        //try
        //{
        //    var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
        //    var stream = (await file.OpenStreamForReadAsync()).AsRandomAccessStream();
        //    var contentType = MimeTypes[Path.GetExtension(uri.LocalPath)];
        //    e.Response = sender.Environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}");
        //}
        //catch (FileNotFoundException)
        //{
        //    Debug.WriteLine($"WebView could not find '{uri.AbsolutePath}'");
        //}


        //var uri = new Uri(
        //    new Uri("ms-appx:///Assets/Web/"),
        //    new Uri(sourceUri.AbsolutePath[1..], UriKind.Relative));

        //using var deferral = e.GetDeferral();

        //try
        //{
        //    var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
        //    var stream = (await file.OpenStreamForReadAsync()).AsRandomAccessStream();
        //    var contentType = MimeTypes[Path.GetExtension(uri.LocalPath)];
        //    e.Response = sender.Environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}");
        //}
        //catch (FileNotFoundException)
        //{
        //    Debug.WriteLine($"WebView could not find '{uri.AbsolutePath}'");
        //}

        //StorageFile.GetFileFromApplicationUriAsync(uri);

        //switch (uri.AbsolutePath)
        //{
        //    case "/renderer.html":
        //    {
        //        var htmlContent = "<html><body>Hello World! <img src='https://local-resource/image.svg' /></body></html>";
        //        var htmlStream = new MemoryStream(Encoding.UTF8.GetBytes(htmlContent));
        //        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(htmlStream, 200, "OK", "Content-Type: text/html");
        //        break;
        //    }
        //    case "/image.svg":
        //    {
        //        // Assuming you have "image.svg" in your app's assets
        //        using var stream = File.OpenRead("path_to_your_app_assets/image.svg");
        //        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: image/svg+xml");
        //        break;
        //    }
        //}

        deferral.Complete();
    }

    private static readonly Dictionary<string, string> MimeTypes = new()
    {
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".js", "application/javascript" },
        { ".css", "text/css" },
        { ".png", "image/png" },
        { ".ico", "image/x-icon" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
    };

    public class JsError
    {
        public string Message { get; set; }
        public string Stack { get; set; }
        public string Name { get; set; }
    }

    public static async Task<IRandomAccessStream> UnloadStreamAsRandomAccess(Stream input)
    {
        var memoryStream = new MemoryStream();
        await input.CopyToAsync(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin); // Reset the memory stream position to the beginning.

        // Create an InMemoryRandomAccessStream for the RandomAccessStream we are going to return.
        var randomAccessStream = new InMemoryRandomAccessStream();

        // Use the memoryStream content to write into our RandomAccessStream.
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

public class ConversationBase : ReactiveUserControl<ConversationVm>
{
    /* Because WinUI3 is dumber than even WPF apparently */
}
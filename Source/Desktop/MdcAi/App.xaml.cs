// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi;

using Castle.MicroKernel.Registration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sala.Extensions.WinUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using Views;
using Mdc.OpenAiApi;
using LogLevel = NLog.LogLevel;
using NLog.Common;
using MdcAi.ChatUI.LocalDal;
using MdcAi.ChatUI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Windows.UI.Popups;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : ILogging
{
    public Window Window { get; private set; }

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // The DI container
        Services.Install();

        ConfigureNLog();

        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(
            // ReSharper disable once AsyncVoidLambda
            async exception => await HandleException(exception));

        UnhandledException += async (_, e) =>
        {
            await HandleException(e.Exception);
            e.Handled = true;
        };

        EnableAdditionalWebView2Optons(new (string name, string value)[]
        {
            //("enable-features", "OverlayScrollbar,OverlayScrollbarWinStyle,OverlayScrollbarWinStyleAnimation"),
#if DEBUG
            ("remote-debugging-port", "9222")
#endif
        });

        ServiceViewLocator.Register();        

        // Registery Entity Framework database for user profile management (chat lists etc)
        Services.Container.Register(
            Component.For<UserProfileDbContext>()
                     .UsingFactoryMethod(
                         _ => new UserProfileDbContext(Path.Combine(ApplicationData.Current.LocalFolder.Path, "Chats.db")))
                     .LifestyleTransient());

        Observable.FromAsync(async () =>
                  {
                      await using var db = Services.Container.Resolve<UserProfileDbContext>();
                      await db.Database.MigrateAsync();
                  })
                  .Subscribe();

        //Debug.WriteLine(db.Conversations.Count());

        //return;

        //db.Conversations.Add(new DbConversation
        //{
        //    IdConversation = Guid.NewGuid().ToString(),
        //    Name = "Test 1"
        //});

        //db.SaveChanges();

        Services.RegisterViewModelsAndViews("MdcAi.ChatUI");
        Services.RegisterViewModelsAndViews(Types.FromAssembly(Assembly.GetExecutingAssembly()));

        RegisterApi();

        InitializeComponent();
    }

    private void EnableAdditionalWebView2Optons((string name, string value)[] options) =>
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                                           string.Join(' ', options.Select(t => $"--{t.name}={t.value}")));

    private async Task HandleException(Exception ex)
    {
        // TODO: Must handle scenario where mutliple errors happen, they have to stack and be shown in the one single content dialog
        this.LogError(ex);
        var dialog = new ContentDialog
        {
            // TODO: A proper exception dialog with link to the log file
            Content = $"{ex.Message}\r\nPid: {Process.GetCurrentProcess().Id}",
            XamlRoot = Window.Content.XamlRoot,
            Title = "Something Broke 😳",
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    private void ConfigureNLog()
    {
        // Note that with WinUI it will never, no matter what, distribute or embed pdbs by default. You have to copy them manually.
        var config = new LoggingConfiguration();
        var dirPath = ApplicationData.Current.LocalFolder.Path;

        InternalLogger.LogFile = Path.Combine(dirPath, "nlog.log");
        InternalLogger.LogLevel = LogLevel.Warn;

        var logFile = Path.Combine(dirPath, "app-${processid}-${date:format=yyyy-MM-dd-HH.mm.ss:cached=true}.log");

        // TODO: Delete files older than 30 days
        var logTarget = new FileTarget("logfile")
        {
            FileName = logFile,
            Layout = "${longdate}|" +
                     "${level}|" +
                     "${logger}|" +
                     "${threadid}|" +
                     "${message}|" +
                     "${exception:" +
                     "format=type,message,stacktrace:" +
                     "innerformat=type,message,stacktrace:" +
                     "separator=\\n:" +
                     "exceptionDataSeparator=\\n:" +
                     "maxInnerExceptionLevel=10}"
        };

        config.AddRule(LogLevel.Trace, LogLevel.Fatal, logTarget);

        LogManager.Configuration = config;

        var factory = new LoggerFactory(new[] { new NLogLoggerProvider() });
        LoggingExtensions.LoggerFactory = factory;
        Services.RegisterLoggerFactory();
    }

    private void RegisterApi()
    {
        var settings = Services.GetRequired<SettingsVm>();
        var api = new OpenAIApi();

        Services.RegisterSingleton<IOpenAIApi>(api);

        settings.WhenAnyValue(vm => vm.OpenAi.CurrentApiKey,
                              vm => vm.OpenAi.OrganisationName)
                .Where(_ => !string.IsNullOrEmpty(settings.OpenAi.CurrentApiKey))
                .Do(_ => api.SetCredentials(settings.OpenAi.CurrentApiKey, settings.OpenAi.OrganisationName))
                .Subscribe();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
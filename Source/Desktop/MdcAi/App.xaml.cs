﻿#region Copyright Notice
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

namespace MdcAi;

using Castle.MicroKernel.Registration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using Views;
using OpenAiApi;
using LogLevel = NLog.LogLevel;
using NLog.Common;
using ChatUI.LocalDal;
using MdcAi.ChatUI.ViewModels;
using Microsoft.EntityFrameworkCore;
using RxUIExt.Windsor;
using System.Diagnostics;

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
        if (!Directory.Exists(AppServices.GetLocalDataFolder()))        
            Directory.CreateDirectory(AppServices.GetLocalDataFolder());        

        // The DI container
        AppServices.Install();

        ConfigureNLog();

        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(
            // ReSharper disable once AsyncVoidLambda
            async exception => await HandleException(exception));

        UnhandledException += async (_, e) =>
        {
            e.Handled = true;
            await HandleException(e.Exception);
        };

        EnableAdditionalWebView2Optons(new (string name, string value)[]
        {
            //("enable-features", "OverlayScrollbar,OverlayScrollbarWinStyle,OverlayScrollbarWinStyleAnimation"),
#if DEBUG
            ("remote-debugging-port", "9222")
#endif
        });

#if UNPACKAGED
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", AppServices.GetLocalDataFolder());
#endif

        CastleServiceViewLocator.Register(AppServices.Container, typeof(Window));

        // Register Entity Framework database for user profile management (chat lists etc)
        AppServices.Container.Register(
            Component.For<UserProfileDbContext>()
                     .UsingFactoryMethod(
                         _ => new UserProfileDbContext(Path.Combine(AppServices.GetLocalDataFolder(), "Chats.db"))
                         {
                             Log = message =>
                             {
                                 if (Debugging.Enabled && Debugging.LogSql && message.Contains("CommandExecuted"))
                                     Debug.WriteLine(message);
                             }
                         })
                     .LifestyleTransient());

        AppServices.Container.Register(Component.For<UserProfileDbContextWithTrans>().LifeStyle.Transient);

        Observable.FromAsync(async () =>
                  {
                      await using var db = AppServices.GetUserProfileDb();
                      await db.Database.MigrateAsync();
                  })
                  .SubscribeSafe();

        AppServices.Container.RegisterViewModelsAndViews("MdcAi.ChatUI");
        AppServices.Container.RegisterViewModelsAndViews(Types.FromAssembly(Assembly.GetExecutingAssembly()));

        RegisterApi();

        InitializeComponent();
    }

    private void EnableAdditionalWebView2Optons((string name, string value)[] options)
    {
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                                           string.Join(' ', options.Select(t => $"--{t.name}={t.value}")));
    }

    private bool _errorDialogOpen;

    private async Task HandleException(Exception ex)
    {
        this.LogError(ex);

        if (_errorDialogOpen)
            return;

        try
        {            
            _errorDialogOpen = true;

            var message = $"{ex.Message}\r\nPid: {Process.GetCurrentProcess().Id}";

            try
            {                
                var dialog = new ContentDialog
                {
                    Content = message,
                    XamlRoot = Window.Content.XamlRoot,
                    Title = "Something Broke 😳",
                    CloseButtonText = "OK",
                };

                await dialog.ShowAsync();
            }
            catch // Unfortunately WinUI is extremely fragile and this can happen surprisingly often
            {
                System.Windows.MessageBox.Show(message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);                
            }
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private void ConfigureNLog()
    {
        // Note that with WinUI it will never, no matter what, distribute or embed pdbs by default. You have to copy them manually.
        var config = new LoggingConfiguration();
        
        var dirPath = AppServices.GetLocalDataFolder();

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

        AppServices.Container.Register(Component.For<ILoggerFactory>()
                                                .Instance(LoggingExtensions.LoggerFactory)
                                                .LifeStyle.Singleton);
    }

    private void RegisterApi()
    {
        var settings = AppServices.Container.Resolve<SettingsVm>();
        var api = new OpenAiClient();

        AppServices.Container.Register(Component.For<IOpenAiApi>()
                                                .Instance(api)
                                                .LifeStyle.Singleton);

        settings.WhenAnyValue(vm => vm.OpenAi.ApiKey,
                              vm => vm.OpenAi.OrganisationName)
                .Where(_ => !string.IsNullOrEmpty(settings.OpenAi.ApiKey))
                .Do(_ => api.SetCredentials(settings.OpenAi.ApiKey, settings.OpenAi.OrganisationName))
                .SubscribeSafe();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DebugSettings.IsBindingTracingEnabled = Debugging.IsBindingTracingEnabled;

        Window = new MainWindow();
        Window.Activate();
    }
}
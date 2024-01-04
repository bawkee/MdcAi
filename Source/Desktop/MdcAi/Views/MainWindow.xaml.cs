// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactiveMarbles.ObservableEvents;
using MdcAi.ChatUI.Views.GettingStartedTips;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow
{
    public MainVm ViewModel { get; }

    public MainWindow()
    {
        ViewModel = AppServices.Container.Resolve<MainVm>();

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        this.Events()
            .Activated
            .Take(1)
            .Do(_ => ViewModel.Activator.Activate())
            .SubscribeSafe();

        PrivacyInfoWindow privacyInfoWnd = null;

        ViewModel.Settings.ShowPrivacyStatementCmd =
            ReactiveCommand.Create(() =>
            {
                privacyInfoWnd ??= new();
                privacyInfoWnd.Activate();
                privacyInfoWnd.Events()
                              .Closed
                              .Take(1)
                              .Do(_ => privacyInfoWnd = null)
                              .SubscribeSafe();
            });
    }
}
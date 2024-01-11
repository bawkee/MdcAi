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
using System.Runtime.InteropServices;
using WinRT.Interop;

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

        SetIcon("Assets\\Icon.ico");

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

        ViewModel.Settings.ShowAboutCmd =
            ReactiveCommand.CreateFromTask(async () => { await AboutDialog.ShowAsync(); });
    }

    private void SetIcon(string iconName)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var hIcon = PInvoke.User32.LoadImage(
            IntPtr.Zero,
            iconName,
            PInvoke.User32.ImageType.IMAGE_ICON,
            16,
            16,
            PInvoke.User32.LoadImageFlags.LR_LOADFROMFILE);
        PInvoke.User32.SendMessage(hwnd, PInvoke.User32.WindowMessage.WM_SETICON, (IntPtr)0, hIcon);
    }
}
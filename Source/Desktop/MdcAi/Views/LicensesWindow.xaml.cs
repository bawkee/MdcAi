// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.Views;

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
using Windows.Storage;
using CommunityToolkit.WinUI.UI.Controls;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LicensesWindow
{
    public LicensesWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        var appWindow = this.GetAppWindow();
        appWindow.Resize(new()
        {
            Width = 600,
            Height = appWindow.Size.Height
        });
    }

    private async void Licenses_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var privacyMd = await StorageFile.GetFileFromApplicationUriAsync(
            new("ms-appx:///Assets/Licenses.md"));

        MdTextBlock.Text = await FileIO.ReadTextAsync(privacyMd);
    }

    private void MdTextBlock_OnLinkClicked(object sender, LinkClickedEventArgs e) => 
        ShellUtil.StartUrl(e.Link);
}
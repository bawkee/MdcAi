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
using System.Windows.Controls;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using CommunityToolkit.WinUI.UI.Controls;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PrivacyInfoWindow : ILogging
{
    public PrivacyInfoWindow()
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

    private async void PrivacyInfoWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        var privacyMd = await StorageFile.GetFileFromApplicationUriAsync(
            new Uri("ms-appx:///Assets/PrivacyPolicy.md"));

        MdTextBlock.Text = await FileIO.ReadTextAsync(privacyMd);
    }

    private void MdTextBlock_OnLinkClicked(object sender, LinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Link,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new("Could not open the URL. App may not have the required system permissions to do this.", ex);
        }
    }
}
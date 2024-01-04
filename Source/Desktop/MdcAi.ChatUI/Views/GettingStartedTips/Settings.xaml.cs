// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MdcAi.ChatUI.Views.GettingStartedTips;

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
using Microsoft.UI.Xaml.Documents;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class Settings : Page
{
    public Settings() { InitializeComponent(); }

    private void CategoryHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args) =>
        this.SelectPageInNavigationView(nameof(Categories));

    private void PremiseHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args) =>
        this.SelectPageInNavigationView(nameof(Premise));

    private void ConversationsHyperlink_OnClick(Hyperlink sender, HyperlinkClickEventArgs args) =>
        this.SelectPageInNavigationView(nameof(Conversations));

}
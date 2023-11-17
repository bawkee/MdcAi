namespace Sala.Extensions.WinUI;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactiveUI;
using Splat;

/// <summary>
/// This content control will automatically load the View associated with
/// the ViewModel property and display it. This control is very useful
/// inside a DataTemplate to display the View associated with a ViewModel.
/// </summary>
public class ViewHost : ViewModelViewHost
{
    public ViewHost()
    {
        SetValue(VerticalContentAlignmentProperty, VerticalAlignment.Stretch);
        SetValue(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}
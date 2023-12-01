namespace Sala.Extensions.WinUI;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactiveUI;
using Splat;

/// <summary>
/// Configures caching for <see cref="ViewHost.CacheType"/>
/// </summary>
public enum ViewHostCacheType
{
    None,
    ByInstance,
    ByType
}

/// <summary>
/// This content control will automatically load the View associated with
/// the ViewModel property and display it. This control is very useful
/// inside a DataTemplate to display the View associated with a ViewModel.
/// </summary>
public class ViewHost : TransitioningContentControl, IViewFor, IEnableLogger
{
    public static readonly DependencyProperty CacheTypeProperty =
        DependencyProperty.Register(nameof(CacheType),
                                    typeof(ViewHostCacheType),
                                    typeof(ViewHost),
                                    new(default(ViewHostCacheType)));

    public static readonly DependencyProperty DefaultContentProperty =
        DependencyProperty.Register(nameof(DefaultContent),
                                    typeof(object),
                                    typeof(ViewModelViewHost),
                                    new(null));

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel),
                                    typeof(object),
                                    typeof(ViewModelViewHost),
                                    new(null));

    public static readonly DependencyProperty ViewContractObservableProperty =
        DependencyProperty.Register(nameof(ViewContractObservable),
                                    typeof(IObservable<string>),
                                    typeof(ViewModelViewHost),
                                    new(Observable.Return(default(string))));

    private string _viewContract;

    public ViewHost()
    {
        var platform = Locator.Current.GetService<IPlatformOperations>();
        Func<string> platformGetter = () => default;

        if (platform is null)
        {
            // NB: This used to be an error but WPF design mode can't read
            // good or do other stuff good.
            this.Log().Error("Couldn't find an IPlatformOperations implementation.");
        }
        else
        {
            platformGetter = () => platform.GetOrientation();
        }

        ViewContractObservable = ModeDetector.InUnitTestRunner() ?
            Observable.Never<string>() :
            Observable.FromEvent<SizeChangedEventHandler, string>(
                          eventHandler =>
                          {
                              //#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
                              void Handler(object _, SizeChangedEventArgs __) => eventHandler(platformGetter());
                              //#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
                              return Handler;
                          },
                          x => SizeChanged += x,
                          x => SizeChanged -= x)
                      .StartWith(platformGetter())
                      .DistinctUntilChanged();

        var contractChanged = this.WhenAnyObservable(x => x.ViewContractObservable).Do(x => _viewContract = x).StartWith(ViewContract);
        var viewModelChanged = this.WhenAnyValue(x => x.ViewModel).StartWith(ViewModel);
        var vmAndContract = contractChanged
            .CombineLatest(viewModelChanged, (contract, vm) => (ViewModel: vm, Contract: contract));

        this.WhenActivated(d =>
        {
            d(contractChanged
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(x => _viewContract = x ?? string.Empty));

            d(vmAndContract.DistinctUntilChanged().Subscribe(x => ResolveViewForViewModel(x.ViewModel, x.Contract)));
        });

        SetValue(VerticalContentAlignmentProperty, VerticalAlignment.Stretch);
        SetValue(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);

        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>
    /// Configures view caching, by view model type or instance, etc.
    /// </summary>
    public ViewHostCacheType CacheType
    {
        get => (ViewHostCacheType)GetValue(CacheTypeProperty);
        set => SetValue(CacheTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the view contract observable.
    /// </summary>
    public IObservable<string> ViewContractObservable
    {
        get => (IObservable<string>)GetValue(ViewContractObservableProperty);
        set => SetValue(ViewContractObservableProperty, value);
    }

    /// <summary>
    /// Gets or sets the content displayed by default when no content is set.
    /// </summary>
    public object DefaultContent
    {
        get => GetValue(DefaultContentProperty);
        set => SetValue(DefaultContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the ViewModel to display.
    /// </summary>
    public object ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Gets or sets the view contract.
    /// </summary>
    public string ViewContract
    {
        get => _viewContract;
        set => ViewContractObservable = Observable.Return(value);
    }

    private readonly Dictionary<(object, string), IViewFor> _cache = new();

    /// <summary>
    /// Gets or sets the view locator.
    /// </summary>
    public IViewLocator ViewLocator { get; set; }

    private void ResolveViewForViewModel(object viewModel, string contract)
    {
        if (viewModel is null)
        {
            // BUG: Apparently in WinUI if the View gets deactivated, it can never get activated again
            // So setting this to null will cause deactivation as it should, but setting it back to the 'deactivated'
            // view will do fuck all.
            //Content = DefaultContent;
            return;
        }

        if (GetCache(viewModel, contract, out var cachedViewInstance))
        {
            Debug.WriteLine($"Setting vm {viewModel.GetHashCode()}");
            if (CacheType == ViewHostCacheType.ByType)
                cachedViewInstance.ViewModel = viewModel;
            Content = cachedViewInstance;
            return;
        }

        var viewLocator = ViewLocator ?? ReactiveUI.ViewLocator.Current;
        var viewInstance = viewLocator.ResolveView(viewModel, contract) ?? viewLocator.ResolveView(viewModel);

        if (viewInstance is null)
        {
            Content = DefaultContent;
            this.Log().Warn($"The {nameof(ViewModelViewHost)} could not find a valid view for {viewModel.GetType()}.");
            return;
        }

        viewInstance.ViewModel = viewModel;

        SetCache(viewModel, contract, viewInstance);

        Content = viewInstance;
    }

    private bool GetCache(object vm, string contract, out IViewFor result)
    {
        if (CacheType == ViewHostCacheType.None)
        {
            result = default;
            return false;
        }

        var obj = CacheType switch
        {
            ViewHostCacheType.ByType => vm.GetType(),
            ViewHostCacheType.ByInstance => vm,
            _ => throw new NotImplementedException()
        };

        return _cache.TryGetValue((obj, contract), out result);
    }

    private void SetCache(object vm, string contract, IViewFor value)
    {
        if (CacheType == ViewHostCacheType.None)
            return;

        var obj = CacheType switch
        {
            ViewHostCacheType.ByType => vm.GetType(),
            ViewHostCacheType.ByInstance => vm,
            _ => throw new NotImplementedException()
        };

        _cache[(obj, contract)] = value;
    }
}
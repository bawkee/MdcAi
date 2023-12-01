namespace Sala.Extensions.WinUI;

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using MdcAi.Extensions;
using System.Diagnostics;

public static class RxUIExtensions
{
    /// <summary>
    /// Observes on <see cref="RxApp.MainThreadScheduler"/> scheduler.
    /// </summary>
    public static IObservable<T> ObserveOnMainThread<T>(this IObservable<T> source) =>
        source.ObserveOn(RxApp.MainThreadScheduler);

    /// <summary>
    /// When you subscribe to ThrownExceptions observable, by default, exceptions will not be thrown/propagated - they are
    /// treated as handled. This operator allows you to propagate them further downstream as unhandled.
    /// </summary>
    public static IDisposable PropagateExceptions(this IObservable<Exception> source) =>
        source.Subscribe(ex => RxApp.DefaultExceptionHandler.OnNext(ex));

    /// <summary>
    /// Registers a synchronous interaction handler that produces no output. Commonly used to display pop ups and windows.
    /// </summary>
    public static IDisposable RegisterHandler(this Interaction<Unit, Unit> source, Action action) =>
        source.RegisterHandler(ctx =>
        {
            action();
            ctx.SetOutput();
        });

    /// <summary>
    /// Registers a synchronous interaction handler that produces no output. Commonly used to display pop ups and windows.
    /// </summary>
    public static IDisposable RegisterHandler<TInput>(this Interaction<TInput, Unit> source, Action<TInput> action) =>
        source.RegisterHandler(ctx =>
        {
            action(ctx.Input);
            ctx.SetOutput();
        });

    /// <summary>
    /// Registers an interaction handler that has no input.
    /// </summary>
    public static IDisposable RegisterHandler<TOutput>(this Interaction<Unit, TOutput> source, Func<TOutput> func) =>
        source.RegisterHandler(ctx => ctx.SetOutput(func()));

    /// <summary>
    /// Sets the Unit.Default output to the interaction, allowing it to continue with no relevant output.
    /// </summary>
    public static void SetOutput(this IInteractionContext<Unit, Unit> source) =>
        source.SetOutput(Unit.Default);

    /// <summary>
    /// Sets the Unit.Default output to the interaction, allowing it to continue with no relevant output.
    /// </summary>
    public static void SetOutput<TInput>(this IInteractionContext<TInput, Unit> source) =>
        source.SetOutput(Unit.Default);

    /// <summary>
    /// Handles the interaction without input.
    /// </summary>
    public static IObservable<TOutput> Handle<TOutput>(this Interaction<Unit, TOutput> source) =>
        source.Handle(Unit.Default);

    /// <summary>
    /// Ticks a Unit when command starts executing.
    /// </summary>
    public static IObservable<Unit> WhenExecuting(this IReactiveCommand cmd) =>
        cmd.IsExecuting
           .PairWithPrevious()
           .Where(p => p.Item1 == false && p.Item2 == true)
           .Select(_ => Unit.Default);

    /// <summary>
    /// An idiomatic WhenActivated which combines view model and activation logic, crucial when you expect view model to change on a single view.
    /// </summary>
    public static IDisposable WhenActivated<TVm>(this IViewFor<TVm> item, Action<CompositeDisposable, TVm> block) where TVm : class =>
        item.WhenActivated(disposables =>
        {
            item.WhenAnyValue(v => v.ViewModel)
                .Select(vm => new { Vm = vm, Disposables = new CompositeDisposable() })
                .SeriallyDispose(x => x.Disposables)
                .Where(vm => vm != null)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Do(x => block(x.Disposables, x.Vm))
                .Subscribe()
                .DisposeWith(disposables);

            Disposable.Create(() => Debug.WriteLine($"Deactivated view for real this time")).DisposeWith(disposables);
        });
}
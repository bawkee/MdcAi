namespace Sala.Extensions.WinUI;

using ReactiveUI;

public abstract class ActivatableViewModel : ViewModel, IActivatableViewModel
{
    // https://reactiveui.net/docs/handbook/when-activated/
    public ViewModelActivator Activator { get; } = new();
}
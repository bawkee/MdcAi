namespace Sala.Extensions.WinUI;

using ReactiveUI;
using Splat;

/// <summary>
/// This is the ViewLocator you can use in apps and derive from if needed. RxUI uses 'Splat' for it DI/IoC needs which is
/// OK but it's obviously tailored for enabling cross platformness and has its shortcomings. I prefer Windsor/StructureMap,
/// especially since everybody is already familiar with them. I would rather not use any 'locator' at all but unfortunately
/// some of the nicer things in RxUI don't behave nicely without it (I tried) so I'll just wire it up with Windsor here and
/// hopefully forget that it exists.
/// </summary>
public class ServiceViewLocator : IViewLocator
{
    public static void Register() { Locator.CurrentMutable.Register<IViewLocator>(() => new ServiceViewLocator()); }

    public virtual IViewFor ResolveView<T>(T viewModel, string contract = null) =>
        Services.GetView(viewModel.GetType(), contract);
}
namespace MdcAi.Extensions.WinUI;

using Castle.MicroKernel.ModelBuilder.Inspectors;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;
using Castle.Windsor;

public static class AppServices
{
    // Service locator antipattern - with great power comes great... uh... productivity?
    public static IWindsorContainer Container { get; set; }

    public static IWindsorContainer Install()
    {
        Container = new WindsorContainer();

        // So we can use collections of implementations of the same base type
        Container.Kernel.Resolver.AddSubResolver(new CollectionResolver(Container.Kernel));

        // We don't want to inject properties, only ctors
        // https://stackoverflow.com/questions/178611
        var propInjector = Container.Kernel.ComponentModelBuilder
                                    .Contributors
                                    .OfType<PropertiesDependenciesModelInspector>()
                                    .Single();

        Container.Kernel.ComponentModelBuilder.RemoveContributor(propInjector);

        Container.Install();

        return Container;
    }    
}
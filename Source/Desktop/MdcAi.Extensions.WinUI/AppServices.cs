namespace MdcAi.Extensions.WinUI;

using Castle.MicroKernel.ModelBuilder.Inspectors;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;
using Castle.Windsor;
using ChatUI.LocalDal;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reactive.Disposables;

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
   
    public static UserProfileDbContext GetUserProfileDb() => Container.Resolve<UserProfileDbContext>();

    // TODO: Use UserProfileDbContextWithTrans???
    public static UserProfileDbContextWithTrans GetUserProfileDbTrans() => Container.Resolve<UserProfileDbContextWithTrans>();
}

public class UserProfileDbContextWithTrans : IDisposable
{
    private readonly CompositeDisposable _cd;
    public IDbContextTransaction Trans { get; }
    public UserProfileDbContext Ctx { get; }

    public UserProfileDbContextWithTrans(UserProfileDbContext ctx)
    {
        Ctx = ctx;
        Trans = Ctx.Database.BeginTransaction();
        _cd = new(Trans, Ctx);
    }

    public void Dispose() => _cd.Dispose();
}
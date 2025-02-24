﻿#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.Extensions.WinUI;

using Castle.MicroKernel.ModelBuilder.Inspectors;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;
using Castle.Windsor;
using ChatUI.LocalDal;
using System.IO.Compression;
using Windows.Foundation;
using Windows.Storage;

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

    /// <summary>
    /// Gets the ef context and encapsulates it with a transaction.
    /// </summary>   
    public static UserProfileDbContextWithTrans GetUserProfileDbTrans() => Container.Resolve<UserProfileDbContextWithTrans>();

    public static string GetLocalDataFolder()
    {
#if UNPACKAGED
        return $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\MDCAI";
#else
        return ApplicationData.Current.LocalFolder.Path;
#endif
    }

    public static IAsyncOperation<StorageFile> GetAppFile(string path)
    {
#if UNPACKAGED                
        return StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory, "Assets", path));
#else
        return StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///MdcAi.ChatUI/Assets/{path}"));
#endif

    }
}

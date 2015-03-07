using NuGet;
using NuGet.Client;
using NuGet.Client.V2;
using NuGet.Data;
using NuGet.Frameworks;
using NuGet.PackagingCore;
using NuGet.Resolver;
using NuGet.Services.Metadata.Catalog;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Cache;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace InstallParity
{
    class Program
    {
        static void Main(string[] args)
        {
            DataServicePackageRepository v2Repo = new DataServicePackageRepository(new Uri("https://www.nuget.org/api/v2/"));
            LocalPackageRepository localRepo = new LocalPackageRepository(@"f:\tmp\packages");

            var xy = v2Repo.FindPackages("Newtonsoft.Json", new VersionSpec(new NuGet.SemanticVersion("3.5.8")), false, false);

            WebRequestHandler handler = new WebRequestHandler()
            {
                // aggressive caching
                CachePolicy = new RequestCachePolicy(RequestCacheLevel.CacheIfAvailable),
            };
            DataClient client = new DataClient(handler);

            CollectorHttpClient collect = new CollectorHttpClient(handler);

            //CatalogIndexReader catalogReader = new CatalogIndexReader(new Uri("https://az320820.vo.msecnd.net/catalog-0/index.json"), collect);
            //var catalogEntries = catalogReader.GetRolledUpEntries().Result;



            //foreach (var entry in catalogEntries)


            foreach (var package in GetTopPackages(v2Repo))
            {

                var target = new PackageIdentity(package.Id, NuGetVersion.Parse(package.Version.ToString()));

                Console.WriteLine(target.ToString());

                var framework = NuGetFramework.Parse("net451");

                HashSet<PackageIdentity> v2Core = new HashSet<PackageIdentity>();
                HashSet<PackageIdentity> v3 = new HashSet<PackageIdentity>();
                HashSet<PackageIdentity> v3v2 = new HashSet<PackageIdentity>();

                try
                {
                    v2Core = ResolveV2Core(target, localRepo, v2Repo);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("V2Core failed");
                }

                try
                {
                    v3 = ResolveV3(client, target, framework);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("V3 failed");
                }

                try
                {
                    v3v2 = ResolveV3V2(target, v2Repo, framework);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("V3 failed");
                }

                var diff1 = v2Core.Except(v3, PackageIdentity.Comparer);
                var diff2 = v3.Except(v2Core, PackageIdentity.Comparer);
                var diff3 = v3.Except(v3v2, PackageIdentity.Comparer);

                if ((diff1.Count() + diff2.Count() + diff3.Count()) > 0)
                {
                    Console.WriteLine("Difference: " + target.ToString());
                }
            }
        }


        static HashSet<PackageIdentity> ResolveV3V2(PackageIdentity target, DataServicePackageRepository v2Repo, NuGetFramework framework)
        {
            V2DependencyInfoResource depInfo = new V2DependencyInfoResource(v2Repo);

            var targets = new PackageIdentity[] { target };

            var task = depInfo.ResolvePackages(targets, framework, target.Version.IsPrerelease);
            task.Wait();

            PackageResolver resolver = new PackageResolver(DependencyBehavior.Lowest);
            var resolved = resolver.Resolve(targets, task.Result);

            return new HashSet<PackageIdentity>(resolved,
                PackageIdentity.Comparer);
        }


        static HashSet<PackageIdentity> ResolveV3(DataClient client, PackageIdentity target, NuGetFramework framework)
        {
            V3RegistrationResource regResource = new V3RegistrationResource(client, new Uri("https://az320820.vo.msecnd.net/registrations-1/"));

            V3DependencyInfoResource depInfo = new V3DependencyInfoResource(client, regResource);

            var targets = new PackageIdentity[] { target };

            var task = depInfo.ResolvePackages(targets, framework, target.Version.IsPrerelease);
            task.Wait();

            PackageResolver resolver = new PackageResolver(DependencyBehavior.Lowest);
            var resolved = resolver.Resolve(targets, task.Result);

            return new HashSet<PackageIdentity>(resolved,
                PackageIdentity.Comparer);
        }

        static HashSet<PackageIdentity> ResolveV2Core(PackageIdentity target, LocalPackageRepository localRepo, DataServicePackageRepository v2Repo)
        {
            var package = v2Repo.FindPackage(target.Id, new NuGet.SemanticVersion(target.Version.ToNormalizedString()));

            IPackageOperationResolver resolver = new InstallWalker(localRepo,
                                                                   v2Repo,
                                                                   new FrameworkName(".NETFramework", new Version(4, 5, 1)),
                                                                   NullLogger.Instance,
                                                                   ignoreDependencies: false,
                                                                   allowPrereleaseVersions: target.Version.IsPrerelease,
                                                                   dependencyVersion: DependencyVersion.Lowest);

            return new HashSet<PackageIdentity>(resolver.ResolveOperations(package).Select(e => new PackageIdentity(e.Package.Id, NuGetVersion.Parse(e.Package.Version.ToString()))),
                PackageIdentity.Comparer);
        }

        static IEnumerable<IPackage> GetTopPackages(DataServicePackageRepository v2Repo)
        { 
            foreach (string id in TopIds)
            {
                foreach (var package in v2Repo.FindPackagesById(id))
                {
                    yield return package;
                }
            }

            yield break;
        }

        static List<string> TopIds
        {
            get
            {
                return new List<string>()
                {
                    "Newtonsoft.Json",
                    //"jQuery",
                    //"EntityFramework",
                    //"Microsoft.AspNet.WebPages",
                    //"Microsoft.AspNet.Mvc",
                    //"Microsoft.AspNet.Razor",
                    //"Microsoft.AspNet.WebApi.Client",
                    //"Microsoft.AspNet.WebApi.Core",
                    //"Microsoft.AspNet.WebApi.WebHost",
                    //"Microsoft.AspNet.WebApi",
                    //"WebGrease",
                    //"Microsoft.Net.Http",
                    //"jQuery.Validation",
                    //"Microsoft.jQuery.Unobtrusive.Validation",
                    //"Microsoft.AspNet.Web.Optimization",
                    //"jQuery.UI.Combined",
                    //"Microsoft.Data.OData",
                    //"Microsoft.Data.Edm",
                    //"System.Spatial",
                    //"Modernizr",
                    //"Microsoft.Web.Infrastructure",
                    //"Microsoft.jQuery.Unobtrusive.Ajax",
                    //"bootstrap",
                    //"Microsoft.Bcl",
                    //"Antlr",
                    //"Microsoft.Bcl.Build",
                    //"Microsoft.Owin",
                    //"Microsoft.AspNet.WebApi.OData",
                    //"Moq",
                    //"Microsoft.Owin.Host.SystemWeb",
                    //"Microsoft.Owin.Security",
                    //"knockoutjs",
                    //"WindowsAzure.Storage",
                    //"log4net",
                    //"Microsoft.AspNet.WebPages.WebData"
                };
            }
        }
    }
}

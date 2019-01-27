using System;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace PackagesConfigGenerator
{
    /// <summary>
    /// Create a packages.config file containing all versions of the given package ids.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            //var reader = new CatalogReader(new Uri());
            //reader.

            Run().Wait();
        }

        static async Task Run()
        {
            var xml = new XDocument();
            var packagesNode = new XElement("packages");
            xml.Add(packagesNode);
            var source = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            var resource = await source.GetResourceAsync<FindPackageByIdResource>();
            var ids = new[] { "newtonsoft.json", "castle.core", "EntityFramework", "bootstrap", "log4net", "System.Net.Http", "Microsoft.AspNet.SignalR.Client", "RavenDB.Client" };

            using (var cacheContext = new SourceCacheContext())
            {
                foreach (var id in ids)
                {
                    var versions = await resource.GetAllVersionsAsync(id, cacheContext, NullLogger.Instance, CancellationToken.None);

                    foreach (var version in versions)
                    {
                        packagesNode.Add(new XElement("package", new XAttribute("id", id), new XAttribute("version", version.ToNormalizedString()), new XAttribute("framework", "net46")));
                    }
                }
            }

            xml.Save("c:\\tmp\\out-packages.config");
        }
    }
}

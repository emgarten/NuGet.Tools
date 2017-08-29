using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace ValidateAssetsFile
{
    class Program
    {
        static void Main(string[] args)
        {
            var format = new LockFileFormat();

            var assetsFile = format.Read(@"D:\tmp\reactive.2.json");

            var source = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            var resource = source.GetResource<DependencyInfoResource>();

            foreach (var target in assetsFile.Targets)
            {
                Console.WriteLine(target.Name);

                var versions = target.Libraries.ToDictionary(e => e.Name, e => e.Version, StringComparer.OrdinalIgnoreCase);

                foreach (var library in target.Libraries)
                {
                    foreach (var dep in library.Dependencies)
                    {
                        if (!versions.TryGetValue(dep.Id, out var version))
                        {
                            Console.WriteLine($"{library.Name} -> {dep.Id} (missing)");
                        }
                        else if (!dep.VersionRange.Satisfies(version))
                        {
                            Console.WriteLine($"{library.Name} {dep.VersionRange.ToNormalizedString()} -> {dep.Id} {version.ToNormalizedString()} (outside range)");
                        }
                        else if (dep.VersionRange.MinVersion != version)
                        {
                            Console.WriteLine($"{library.Name} {dep.VersionRange.ToNormalizedString()} -> {dep.Id} {version.ToNormalizedString()} (non exact)");
                        }
                    }

                    var packages = resource.ResolvePackages(library.Name, NullLogger.Instance, CancellationToken.None).Result;

                    var package = packages.FirstOrDefault(p => p.Identity.Equals(new PackageIdentity(library.Name, library.Version)));

                    if (package == null)
                    {
                        Console.WriteLine($"{library.Name} {library.Version} not found!");
                    }
                    else if (package.Listed == false)
                    {
                        Console.WriteLine($"{library.Name} {library.Version} is unlisted!");
                    }
                }
            }
        }
    }
}

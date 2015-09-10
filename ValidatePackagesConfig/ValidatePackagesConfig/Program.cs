using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Protocol.Core.v2;
using NuGet.Frameworks;
using System.Threading;

namespace ValidatePackagesConfig
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine(".exe <packages.config> <packages dir>");
                return;
            }

            var configPath = new FileInfo(args[0]);
            var packagesDir = new DirectoryInfo(args[1]);

            var configReader = new PackagesConfigReader(File.OpenRead(configPath.FullName));

            var configPackages = configReader.GetPackages().ToList();

            var repo = Repository.Factory.GetCoreV2(packagesDir.FullName);

            var resource = repo.GetResource<DependencyInfoResource>();

            var packages = new List<SourcePackageDependencyInfo>();

            foreach (var package in configPackages)
            {
                var info = resource.ResolvePackage(package.PackageIdentity, package.TargetFramework, CancellationToken.None).Result;

                if (info == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Missing: " + package.PackageIdentity);
                    Console.ResetColor();
                }

                packages.Add(info);
            }

            foreach (var package in packages)
            {
                //Console.WriteLine(package);

                foreach (var otherPackage in packages)
                {
                    foreach (var dep in otherPackage.Dependencies
                        .Where(e => string.Equals(e.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        dep.SetIncludePrerelease();

                        if (dep.VersionRange.Satisfies(package.Version))
                        {
                            Console.WriteLine("{0} {1} -> {2}", otherPackage.Id, dep.VersionRange, package);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("{0} {1} -> {2}", otherPackage.Id, dep.VersionRange, package);
                            Console.ResetColor();
                        }
                    }
                }
            }
        }
    }
}

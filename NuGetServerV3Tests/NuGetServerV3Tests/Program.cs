using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Cache;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CatalogIndex;
using Newtonsoft.Json.Linq;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGet.Protocol.Core.v3;
using System.Threading;

namespace NuGetServerV3Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            // Cache as much as possible
            var handler = new WebRequestHandler()
            {
                CachePolicy = new RequestCachePolicy(RequestCacheLevel.CacheIfAvailable)
            };

            var httpClient = new HttpClient(handler);

            var packagesJson = new FileInfo("packages.json");

            if (!packagesJson.Exists)
            {
                Console.WriteLine("Reading catalog");

                var reader = new CatalogIndexReader(
                    new Uri("https://api.nuget.org/v3/catalog0/index.json"),
                    httpClient);

                var entries = reader.GetRolledUpEntries().Result.ToList();

                Console.WriteLine("Writing: " + packagesJson.FullName);

                var allVersionsMain = new JObject();
                var allVersions = new JArray();
                allVersionsMain.Add("packages", allVersions);

                foreach (var entry in entries.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Version))
                {
                    var entryObject = new JObject();
                    entryObject["id"] = entry.Id;
                    entryObject["version"] = entry.Version.ToNormalizedString();

                    allVersions.Add(entryObject);
                }

                using (var writer = new StreamWriter(packagesJson.FullName))
                {
                    writer.Write(allVersionsMain.ToString());
                }
            }

            Console.WriteLine("Reading: " + packagesJson.FullName);

            var packages = new List<PackageIdentity>();

            var packagesJsonData = JObject.Parse(packagesJson.OpenText().ReadToEnd());

            foreach (var entry in packagesJsonData["packages"])
            {
                packages.Add(
                    new PackageIdentity(entry["id"].ToString(), NuGetVersion.Parse(entry["version"].ToString())));
            }

            packagesJsonData = null;

            var repo = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

            ReportParityIssues(packages, repo).Wait();
            //ReportResolverIssues(packages, repo).Wait();
            //ReportFlatContainerIssues(packages, repo).Wait();

            Console.WriteLine("done");
            Console.ReadKey();
        }

        private static async Task ReportFlatContainerIssues(List<PackageIdentity> packages, SourceRepository repo)
        {
            var lockObj = new object();
            try
            {
                Console.WriteLine("flat container");

                var index = new Dictionary<string, SortedSet<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);

                foreach (var package in packages)
                {
                    if (!index.ContainsKey(package.Id))
                    {
                        index.Add(package.Id, new SortedSet<NuGetVersion>());
                    }

                    index[package.Id].Add(package.Version);
                }

                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = 16;

                Parallel.ForEach(index.Keys, options, packageId =>
                {
                    Console.WriteLine(packageId);

                    try
                    {
                        var catalogVersions = index[packageId];

                        var findResource = repo.GetResource<FindPackageByIdResource>();
                        findResource.CacheContext = new SourceCacheContext()
                        {
                            NoCache = false
                        };

                        findResource.Logger = NuGet.Logging.NullLogger.Instance;

                        var findAllVersions = findResource.GetAllVersionsAsync(packageId, CancellationToken.None).Result;

                        foreach (var version in findAllVersions.Except(catalogVersions))
                        {
                            Write("only-in-flat", string.Format("Package {0} Version: {1}", packageId, version.ToNormalizedString()));
                        }

                        foreach (var version in catalogVersions.Except(findAllVersions))
                        {
                            Write("missing-from-flat", string.Format("Package {0} Version: {1}", packageId, version.ToNormalizedString()));
                        }
                    }
                    catch (Exception ex)
                    {
                        Write("errors", string.Format("All versions - Package {0} Exception {1}", packageId, ex));
                    }
                });
            }
            catch (Exception ex)
            {
                Write("errors", string.Format("Exception {0}", ex));
            }
        }

        private static async Task ReportParityIssues(List<PackageIdentity> packages, SourceRepository repo)
        {
            var lockObj = new object();
            try
            {
                Console.WriteLine("Running parity test");

                var index = new Dictionary<string, SortedSet<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);

                foreach (var package in packages)
                {
                    if (!index.ContainsKey(package.Id))
                    {
                        index.Add(package.Id, new SortedSet<NuGetVersion>());
                    }

                    index[package.Id].Add(package.Version);
                }

                ParallelOptions options = new ParallelOptions();
                options.MaxDegreeOfParallelism = 16;

                Parallel.ForEach(index.Keys, options, packageId =>
                {
                    Console.WriteLine(packageId);

                    try
                    {
                        var catalogVersions = index[packageId];

                        var dependencyResource = repo.GetResource<DependencyInfoResource>();
                        var findResource = repo.GetResource<FindPackageByIdResource>();
                        findResource.CacheContext = new SourceCacheContext()
                        {
                            NoCache = true
                        };

                        findResource.Logger = NuGet.Logging.NullLogger.Instance;

                        var packageInfos = dependencyResource.ResolvePackages(packageId, CancellationToken.None).Result;
                        var packageInfoVersions = packageInfos.Select(v => v.Identity.Version).ToList();

                        foreach (var version in index[packageId])
                        {
                            if (!packageInfoVersions.Contains(version))
                            {
                                Console.WriteLine("Skipping: " + packageId + " " + version.ToNormalizedString());
                                continue;
                            }

                            var package = new PackageIdentity(packageId, version);

                            try
                            {
                                var packageInfo = packageInfos.Single(group => group.Identity.Equals(package));
                                var groupCount = packageInfo.DependencyGroups.SelectMany(e => e.Packages).Count();

                                var findInfo = findResource.GetDependencyInfoAsync(package.Id, package.Version, CancellationToken.None).Result;

                                var findCount = findInfo.DependencyGroups.SelectMany(e => e.Packages).Count();

                                if (groupCount != findCount)
                                {
                                    Write("dependency-group-diff", string.Format("Package {0} Counts {1}/{2} ", package, groupCount, findCount));
                                }
                            }
                            catch (Exception ex)
                            {
                                Write("errors", string.Format("Package {0} Exception {1}", package, ex));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Write("errors", string.Format("All versions - Package {0} Exception {1}", packageId, ex));
                    }
                });
            }
            catch (Exception ex)
            {
                Write("errors", string.Format("Exception {0}", ex));
            }
        }

        private static object _lockObj = new Object();
        private static void Write(string log, string message)
        {
            lock (_lockObj)
            {
                using (var writer = new StreamWriter(log + ".txt", true))
                {
                    writer.WriteLine(message);
                }
            }
        }

        private static async Task ReportResolverIssues(List<PackageIdentity> packages, SourceRepository repo)
        {
            var lockObj = new object();
            using (var missingWriter = new StreamWriter("resolver-missing.txt", false))
            using (var errorWriter = new StreamWriter("resolver-errors.txt", false))
            using (var writer = new StreamWriter("resolver-test.txt", false))
            {
                try
                {
                    Console.WriteLine("Running resolver test");

                    var index = new Dictionary<string, SortedSet<NuGetVersion>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var package in packages)
                    {
                        if (!index.ContainsKey(package.Id))
                        {
                            index.Add(package.Id, new SortedSet<NuGetVersion>());
                        }

                        index[package.Id].Add(package.Version);
                    }

                    ParallelOptions options = new ParallelOptions();
                    options.MaxDegreeOfParallelism = 32;

                    Parallel.ForEach(index.Keys, options, packageId =>
                    {
                        Console.WriteLine(packageId + " Versions: " + index[packageId].Count);

                        try
                        {
                            var dependencyResource = repo.GetResource<DependencyInfoResource>();
                            var groups = dependencyResource.ResolvePackages(packageId, CancellationToken.None).Result;

                            foreach (var version in index[packageId])
                            {
                                var package = new PackageIdentity(packageId, version);

                                try
                                {
                                    var packageInfos = groups.Where(group => group.Identity.Equals(package));

                                    if (packageInfos.Count() != 1)
                                    {
                                        missingWriter.WriteLine(package.Id.ToLowerInvariant() + "," + package.Version.ToNormalizedString().ToLowerInvariant());
                                        missingWriter.Flush();
                                        continue;
                                    }

                                    var packageInfo = packageInfos.Single();

                                    foreach (var group in packageInfo.DependencyGroups)
                                    {
                                        foreach (var depPackage in group.Packages)
                                        {
                                            if (depPackage.VersionRange != null
                                                && depPackage.VersionRange.IsMinInclusive
                                                && depPackage.VersionRange.HasLowerBound
                                                && depPackage.VersionRange.MinVersion.IsPrerelease
                                                && depPackage.VersionRange.MinVersion.Major > 0)
                                            {
                                                SortedSet<NuGetVersion> candidates;
                                                if (index.TryGetValue(depPackage.Id, out candidates))
                                                {
                                                    var original = candidates
                                                        .Where(v => depPackage.VersionRange.Satisfies(v))
                                                        .OrderBy(v => v, VersionComparer.Default)
                                                        .FirstOrDefault();

                                                    if (original != null
                                                        && original.IsPrerelease
                                                        && !VersionComparer.Version.Equals(
                                                            depPackage.VersionRange.MinVersion,
                                                            original))
                                                    {
                                                        var next = candidates
                                                            .Where(v => !v.IsPrerelease && depPackage.VersionRange.Satisfies(v))
                                                            .OrderBy(v => v, VersionComparer.Default)
                                                            .FirstOrDefault()?.ToNormalizedString() ?? "NONE";

                                                        string message = string.Format("Package: {0} Dependency group TxM: {1} Dependency: {2} Lowest available match: {3} Next stable: {4}",
                                                                package,
                                                                group.TargetFramework.GetShortFolderName(),
                                                                depPackage,
                                                                original,
                                                                next);

                                                        lock (lockObj)
                                                        {
                                                            Console.WriteLine(message);

                                                            writer.WriteLine(message);

                                                            writer.Flush();
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    lock (lockObj)
                                    {
                                        errorWriter.WriteLine("Package {0} Exception {1}", package, ex);
                                        errorWriter.Flush();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (lockObj)
                            {
                                errorWriter.WriteLine("All failed - Package {0} Exception {1}", packageId, ex);
                                errorWriter.Flush();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        errorWriter.WriteLine("Exception {0}", ex);
                        errorWriter.Flush();
                    }
                }
            }
        }
    }
}

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

            ReportResolverIssues(packages, repo).Wait();

            Console.WriteLine("done");
            Console.ReadKey();
        }

        private static async Task ReportResolverIssues(List<PackageIdentity> packages, SourceRepository repo)
        {
            var lockObj = new object();
            using (var writer = new StreamWriter("resolvertest.txt", false))
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

                    Parallel.ForEach(packages, options, package =>
                    {
                        Console.WriteLine(package);

                        var dependencyResource = repo.GetResource<DependencyInfoResource>();

                        // Get the latest version only
                        // var package = new PackageIdentity(packageId, index[packageId].OrderByDescending(v => v).First());

                        try
                        {
                            var groups = dependencyResource.ResolvePackages(package.Id, CancellationToken.None).Result;

                            var packageInfo = groups.FirstOrDefault(group => group.Identity.Equals(package));

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
                                writer.WriteLine("Package {0} Exception {1}", package.Id, ex);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        writer.WriteLine("Exception {0}", ex);
                    }
                }
            }
        }
    }
}

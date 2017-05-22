using NuGet.Frameworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGetAssetsReader;
using NuGet.ContentModel;
using NuGet.Client;
using NuGet.Protocol;
using NuGet.Common;

namespace SoftFallbackRunner
{
    public class Program
    {
        private static readonly string _runtimeJson = @"D:\tmp\rids.json";
        private static readonly string _output = "d:\\tmp\\";
        private static readonly string _input = @"C:\Users\justin\.nuget\packages";
        private static readonly SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
        private static readonly NuGetFramework nca20 = NuGetFramework.Parse("netcoreapp2.0");
        private static readonly NuGetFramework net461 = NuGetFramework.Parse("net461");
        private static readonly NuGetFramework pcl = NuGetFramework.Parse("portable-net45+win8");
        private static readonly NuGetFramework dotnet = NuGetFramework.Parse("dotnet5.6");
        private static readonly NuGetFramework dnx = NuGetFramework.Parse("dnxcore50");

        public static void Main(string[] args)
        {
            Run().Wait();
        }

        public static async Task Run()
        {
            Directory.CreateDirectory(_output);

            var packages = LocalFolderUtility.GetPackagesV3(_input, NullLogger.Instance);
            var tasks = new List<Task>();
            var max = 8;

            var ids = new SortedSet<string>(packages.Select(e => e.Identity.Id), StringComparer.OrdinalIgnoreCase);

            var selectedPackages = new List<LocalPackageInfo>();

            foreach (var id in ids)
            {
                var package = LocalFolderUtility.GetPackagesV3(_input, id, NullLogger.Instance)
                    .OrderByDescending(e => e.Identity.Version)
                    .First();

                selectedPackages.Add(package);
            }

            foreach (var package in selectedPackages)
            {
                if (tasks.Count > max)
                {
                    var task = await Task.WhenAny(tasks);
                    tasks.Remove(task);
                }

                tasks.Add(Task.Run(async () => await ProcessNupkg(package.Path)));
            }

            await Task.WhenAll(tasks);
        }

        public static async Task Log(string test, string path)
        {
            Console.WriteLine($"{test}: {path}");
            var logPath = Path.Combine(_output, test + ".txt");

            await _sem.WaitAsync();

            try
            {
                File.AppendAllLines(logPath, new[] { path });
            }
            finally
            {
                _sem.Release();
            }
        }

        public static async Task ProcessNupkg(string path)
        {
            var contentItems = AssetsReader.GetContentItems(path);
            var conventions = AssetsReader.GetConventions(_runtimeJson);
            var rids = AssetsReader.GetAssetRIDs(contentItems, conventions);

            await DoAssetsChangeWithSoftImportNet461(path, contentItems, conventions, rids);
            await DoAssetsChangeWithSoftImportOldStyle(path, contentItems, conventions, rids);
        }

        public static Task DoAssetsChangeWithSoftImportNet461(string packagePath, ContentItemCollection contentItems, ManagedCodeConventions conventions, ISet<string> rids)
        {
            return DoAssetsChangeWithSoftImport("DoAssetsChangeWithSoftImportNet461", packagePath, contentItems, conventions, rids, nca20, net461);
        }

        public static Task DoAssetsChangeWithSoftImportOldStyle(string packagePath, ContentItemCollection contentItems, ManagedCodeConventions conventions, ISet<string> rids)
        {
            return DoAssetsChangeWithSoftImport("DoAssetsChangeWithSoftImportOldStyle", packagePath, contentItems, conventions, rids, nca20, dotnet, pcl, dnx);
        }

        public static async Task DoAssetsChangeWithSoftImport(string test, string packagePath, ContentItemCollection contentItems, ManagedCodeConventions conventions, ISet<string> rids, NuGetFramework projectFramework, params NuGetFramework[] fallbacks)
        {
            var allRids = new List<string>()
            {
                null
            };

            allRids.AddRange(rids);

            foreach (var rid in allRids)
            {
                var current = GetAssetsWithCurrentFallback(contentItems, conventions, rid, projectFramework, fallbacks);
                var soft = GetAssetsWithSoftFallback(contentItems, conventions, rid, projectFramework, fallbacks);

                var softFiltered = soft.Where(e => !e.EndsWith("_._", StringComparison.Ordinal));
                var currentFiltered = current.Where(e => !e.EndsWith("_._", StringComparison.Ordinal));

                if (!currentFiltered.SequenceEqual(softFiltered, StringComparer.Ordinal))
                {
                    await Log(test, packagePath);
                    break;
                }
            }
        }

        public static ISet<string> GetAssetsWithSoftFallback(ContentItemCollection contentItems, ManagedCodeConventions conventions, string rid, NuGetFramework projectFramework, params NuGetFramework[] fallbacks)
        {
            var frameworks = new List<NuGetFramework>()
            {
                projectFramework
            };

            frameworks.AddRange(fallbacks);

            foreach (var framework in frameworks)
            {
                var assets = AssetsReader.GetAssets(contentItems, conventions, framework, rid);

                if (assets.Count > 0)
                {
                    return assets;
                }
            }

            return new SortedSet<string>(StringComparer.Ordinal);
        }

        public static ISet<string> GetAssetsWithCurrentFallback(ContentItemCollection contentItems, ManagedCodeConventions conventions, string rid, NuGetFramework projectFramework, params NuGetFramework[] fallbacks)
        {
            var framework = AssetsReader.GetFallbackFramework(projectFramework, fallbacks);

            return AssetsReader.GetAssets(contentItems, conventions, framework, rid);
        }
    }
}

using NuGet.Client;
using NuGet.Commands;
using NuGet.Common;
using NuGet.ContentModel;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.RuntimeModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftFallbackTests
{
    class Program
    {
        static void Main(string[] args)
        {
            var packages = LocalFolderUtility.GetPackagesV2(@"D:\tmp\inputPackages", NullLogger.Instance);

            foreach (var package in packages)
            {
                var reader = package.GetReader();

                SoftFallbackDifference(reader);
            }
        }

        static bool SoftFallbackDifference(LocalPackageInfo package)
        {
            var ns20 = NuGetFramework.Parse("netcoreapp2.0");

            var files = reader.GetFiles().ToList();

            var conventions = new ManagedCodeConventions(new RuntimeGraph());

            var ns20Criteria = conventions.Criteria.ForFrameworkAndRuntime(ns20, null);

            var library = new LockFileLibrary();

            var graph = RestoreTargetGraph.Create(new RuntimeGraph(), Enumerable.Empty<GraphNode<RemoteResolveResult>>(), new RemoteWalkContext(new SourceCacheContext(), NullLogger.Instance), NullLogger.Instance, ns20, null);

            var repositoryPackage = new NuGet.Repositories.LocalPackageInfo(package.Identity.Id, package.Identity.Version, package.Path, )

            LockFileUtils.CreateLockFileTargetLibrary(library, package, graph, LibraryIncludeFlags.All);
        }

        static List<string> GetRids(List<ContentItemGroup> groups, NuGetFramework framework, string assetType)
        {
            var results = new List<string>();

            var groupsForFramework = GetContentGroupsForFramework(
                framework,
                groups,
            ManagedCodeConventions.PropertyNames.RuntimeIdentifier);

            // Loop through RID groups
            foreach (var group in groups)
            {
                var rid = (string)group.Properties[ManagedCodeConventions.PropertyNames.RuntimeIdentifier];

                // Create lock file entries for each assembly.
                foreach (var item in group.Items)
                {
                    results.Add(rid);
                }
            }

            return results;
        }

        static List<ContentItemGroup> GetContentGroupsForFramework(
            NuGetFramework framework,
            List<ContentItemGroup> contentGroups,
            string primaryKey)
        {
            var groups = new List<ContentItemGroup>();

            // Group by primary key and find the nearest TxM under each.
            var primaryGroups = new Dictionary<string, List<ContentItemGroup>>(StringComparer.Ordinal);

            foreach (var group in contentGroups)
            {
                object keyObj;
                if (group.Properties.TryGetValue(primaryKey, out keyObj))
                {
                    string key = (string)keyObj;

                    List<ContentItemGroup> index;
                    if (!primaryGroups.TryGetValue(key, out index))
                    {
                        index = new List<ContentItemGroup>(1);
                        primaryGroups.Add(key, index);
                    }

                    index.Add(group);
                }
            }

            // Find the nearest TxM within each primary key group.
            foreach (var primaryGroup in primaryGroups)
            {
                var groupedItems = primaryGroup.Value;

                var nearestGroup = NuGetFrameworkUtility.GetNearest<ContentItemGroup>(groupedItems, framework,
                    group =>
                    {
                        // In the case of /native there is no TxM, here any should be used.
                        object frameworkObj;
                        if (group.Properties.TryGetValue(
                            ManagedCodeConventions.PropertyNames.TargetFrameworkMoniker,
                            out frameworkObj))
                        {
                            return (NuGetFramework)frameworkObj;
                        }

                        return NuGetFramework.AnyFramework;
                    });

                // If a compatible group exists within the secondary key add it to the results
                if (nearestGroup != null)
                {
                    groups.Add(nearestGroup);
                }
            }

            return groups;
        }
    }
}
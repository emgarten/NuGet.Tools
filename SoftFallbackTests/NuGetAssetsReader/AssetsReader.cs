using NuGet.Client;
using NuGet.Commands;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.RuntimeModel;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace NuGetAssetsReader
{
    public static class AssetsReader
    {
        /// <summary>
        /// Get all RIDs used for package assets.
        /// </summary>
        public static ISet<string> GetAssetRIDs(ContentItemCollection contentItems, ManagedCodeConventions conventions)
        {
            return new SortedSet<string>(
                conventions.GetAllPatterns()
                           .SelectMany(pattern => contentItems.FindItemGroups(pattern))
                           .SelectMany(e => e.GetRID()),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// All package assets for the framework/rid
        /// </summary>
        public static ISet<string> GetAssets(ContentItemCollection contentItems, ManagedCodeConventions conventions, NuGetFramework framework, string runtimeIdentifier)
        {
            var criteriaSet = GetCriteria(conventions, framework, runtimeIdentifier);

            return new SortedSet<string>(
                conventions.GetAllPatterns().SelectMany(pattern => GetAssetsFromCriteria(contentItems, criteriaSet, pattern)),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Create ManagedCodeConventions
        /// </summary>
        public static ManagedCodeConventions GetConventions(string runtimeJsonPath)
        {
            var runtimeGraph = JsonRuntimeFormat.ReadRuntimeGraph(runtimeJsonPath);
            return new ManagedCodeConventions(runtimeGraph);
        }

        /// <summary>
        /// Create a fallback framework.
        /// </summary>
        public static FallbackFramework GetFallbackFramework(NuGetFramework framework, params NuGetFramework[] fallbacks)
        {
            return new FallbackFramework(framework, fallbacks.ToList());
        }

        private static IEnumerable<string> GetAssetsFromCriteria(ContentItemCollection contentItems, IReadOnlyList<SelectionCriteria> criteriaSet, PatternSet pattern)
        {
            foreach (var criteria in criteriaSet)
            {
                var group = contentItems.FindBestItemGroup(criteria, pattern);

                if (group != null)
                {
                    return group.Items.Select(e => e.Path);
                }
            }

            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Read content items from a package.
        /// </summary>
        public static ContentItemCollection GetContentItems(string path)
        {
            return GetContentItems(new PackageArchiveReader(path));
        }

        /// <summary>
        /// Read content items from a package.
        /// </summary>
        public static ContentItemCollection GetContentItems(PackageArchiveReader package)
        {
            var contentItems = new ContentItemCollection();

            contentItems.Load(package.GetFiles());

            return contentItems;
        }

        private static IReadOnlyList<SelectionCriteria> GetCriteria(
            ManagedCodeConventions conventions,
            NuGetFramework framework,
            string runtimeIdentifier)
        {
            var managedCriteria = new List<SelectionCriteria>();

            var fallbackFramework = framework as FallbackFramework;

            if (fallbackFramework == null)
            {
                var standardCriteria = conventions.Criteria.ForFrameworkAndRuntime(
                    framework,
                    runtimeIdentifier);

                managedCriteria.Add(standardCriteria);
            }
            else
            {
                // Add project framework
                var primaryFramework = NuGetFramework.Parse(fallbackFramework.DotNetFrameworkName);
                var primaryCriteria = conventions.Criteria.ForFrameworkAndRuntime(
                    primaryFramework,
                    runtimeIdentifier);

                managedCriteria.Add(primaryCriteria);

                // Add fallback frameworks in order
                foreach (var fallback in fallbackFramework.Fallback)
                {
                    var fallbackCriteria = conventions.Criteria.ForFrameworkAndRuntime(
                        fallback,
                        runtimeIdentifier);

                    managedCriteria.Add(fallbackCriteria);
                }
            }

            return managedCriteria;
        }
    }
}
